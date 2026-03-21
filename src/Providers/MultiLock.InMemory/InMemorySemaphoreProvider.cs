using Microsoft.Extensions.Logging;

namespace MultiLock.InMemory;

/// <summary>
/// In-memory implementation of the semaphore provider.
/// This provider is intended for testing and development scenarios only.
/// It does not provide true distributed coordination across multiple processes or machines.
/// </summary>
public sealed class InMemorySemaphoreProvider : ISemaphoreProvider
{
    private readonly ILogger<InMemorySemaphoreProvider> logger;
    // All access is serialized through @lock, so plain Dictionary is sufficient.
    private readonly Dictionary<string, Dictionary<string, SemaphoreSlotRecord>> semaphores = new();
    private readonly object @lock = new();
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemorySemaphoreProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public InMemorySemaphoreProvider(ILogger<InMemorySemaphoreProvider> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<bool> TryAcquireAsync(
        string semaphoreName,
        string holderId,
        int maxCount,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan slotTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);
        ParameterValidation.ValidateMaxCount(maxCount);
        ParameterValidation.ValidateMetadata(metadata);
        ParameterValidation.ValidateSlotTimeout(slotTimeout);

        lock (@lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset expiryTime = now.Subtract(slotTimeout);

            if (!semaphores.TryGetValue(semaphoreName, out Dictionary<string, SemaphoreSlotRecord>? holders))
            {
                holders = new Dictionary<string, SemaphoreSlotRecord>();
                semaphores[semaphoreName] = holders;
            }

            // Clean up expired slots
            CleanupExpiredSlots(holders, expiryTime);

            // Check if holder already has a slot
            if (holders.TryGetValue(holderId, out SemaphoreSlotRecord? existingSlot))
            {
                // Renew the existing slot
                var renewedSlot = new SemaphoreSlotRecord(holderId, existingSlot.AcquiredAt, now, new Dictionary<string, string>(metadata));
                holders[holderId] = renewedSlot;

                logger.LogDebug("Renewed existing slot for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);

                return Task.FromResult(true);
            }

            // Check if there's room for a new slot
            if (holders.Count >= maxCount)
            {
                logger.LogDebug("Semaphore {SemaphoreName} is full ({CurrentCount}/{MaxCount}). Cannot acquire slot for holder {HolderId}",
                    semaphoreName, holders.Count, maxCount, holderId);

                return Task.FromResult(false);
            }

            // Acquire a new slot
            var newSlot = new SemaphoreSlotRecord(holderId, now, now, new Dictionary<string, string>(metadata));
            holders[holderId] = newSlot;

            logger.LogInformation("Successfully acquired slot for holder {HolderId} in semaphore {SemaphoreName} ({CurrentCount}/{MaxCount})",
                holderId, semaphoreName, holders.Count, maxCount);

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task ReleaseAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);

        lock (@lock)
        {
            if (!semaphores.TryGetValue(semaphoreName, out Dictionary<string, SemaphoreSlotRecord>? holders))
                return Task.CompletedTask;

            if (holders.Remove(holderId))
            {
                logger.LogInformation("Released slot for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> UpdateHeartbeatAsync(
        string semaphoreName,
        string holderId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);
        ParameterValidation.ValidateMetadata(metadata);

        lock (@lock)
        {
            if (!semaphores.TryGetValue(semaphoreName, out Dictionary<string, SemaphoreSlotRecord>? holders))
            {
                logger.LogWarning("Heartbeat update failed for holder {HolderId} in semaphore {SemaphoreName} - semaphore not found",
                    holderId, semaphoreName);
                return Task.FromResult(false);
            }

            if (!holders.TryGetValue(holderId, out SemaphoreSlotRecord? existingSlot))
            {
                logger.LogWarning("Heartbeat update failed for holder {HolderId} in semaphore {SemaphoreName} - not a current holder",
                    holderId, semaphoreName);
                return Task.FromResult(false);
            }

            var updatedSlot = new SemaphoreSlotRecord(
                holderId,
                existingSlot.AcquiredAt,
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>(metadata));

            holders[holderId] = updatedSlot;

            logger.LogDebug("Updated heartbeat for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<int> GetCurrentCountAsync(
        string semaphoreName,
        TimeSpan slotTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateSlotTimeout(slotTimeout);

        lock (@lock)
        {
            if (!semaphores.TryGetValue(semaphoreName, out Dictionary<string, SemaphoreSlotRecord>? holders))
                return Task.FromResult(0);

            DateTimeOffset expiryTime = DateTimeOffset.UtcNow.Subtract(slotTimeout);
            int count = holders.Values.Count(slot => slot.LastHeartbeat >= expiryTime);
            return Task.FromResult(count);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SemaphoreHolder>> GetHoldersAsync(
        string semaphoreName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);

        lock (@lock)
        {
            if (!semaphores.TryGetValue(semaphoreName, out Dictionary<string, SemaphoreSlotRecord>? holders))
                return Task.FromResult<IReadOnlyList<SemaphoreHolder>>(Array.Empty<SemaphoreHolder>());

            List<SemaphoreHolder> result = holders.Values
                .Select(slot => new SemaphoreHolder(slot.HolderId, slot.AcquiredAt, slot.LastHeartbeat, slot.Metadata))
                .ToList();

            return Task.FromResult<IReadOnlyList<SemaphoreHolder>>(result);
        }
    }

    /// <inheritdoc />
    public Task<bool> IsHoldingAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);

        lock (@lock)
        {
            if (!semaphores.TryGetValue(semaphoreName, out Dictionary<string, SemaphoreSlotRecord>? holders))
                return Task.FromResult(false);

            return Task.FromResult(holders.ContainsKey(holderId));
        }
    }

    /// <inheritdoc />
    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!isDisposed);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Serialize with the same lock used by all read/write operations so concurrent
        // Acquire/Release/Heartbeat calls see a fully consistent disposed state.
        lock (@lock)
        {
            if (isDisposed) return;
            isDisposed = true;
            semaphores.Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (!isDisposed) return;
        throw new ObjectDisposedException(nameof(InMemorySemaphoreProvider));
    }

    private static void CleanupExpiredSlots(Dictionary<string, SemaphoreSlotRecord> holders, DateTimeOffset expiryTime)
    {
        List<string> expiredKeys = holders
            .Where(kvp => kvp.Value.LastHeartbeat < expiryTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (string key in expiredKeys)
            holders.Remove(key);
    }
}

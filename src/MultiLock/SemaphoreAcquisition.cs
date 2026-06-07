using Microsoft.Extensions.Logging;

namespace MultiLock;

/// <summary>
/// Represents an acquired semaphore slot that releases the slot when disposed.
/// </summary>
/// <remarks>
/// Obtain an instance from <see cref="ISemaphoreService.AcquireScopeAsync"/> for a scoped
/// "acquire, do work, release on dispose" pattern, or construct one directly over an
/// <see cref="ISemaphoreProvider"/> for low-level use.
/// </remarks>
public sealed class SemaphoreAcquisition : IAsyncDisposable, IDisposable
{
    private readonly Func<ValueTask> releaseAsync;
    private readonly ILogger? logger;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreAcquisition"/> class that releases
    /// directly through the supplied provider.
    /// </summary>
    /// <param name="provider">The semaphore provider.</param>
    /// <param name="semaphoreName">The name of the semaphore.</param>
    /// <param name="holderId">The holder ID.</param>
    /// <param name="acquiredAt">The time the slot was acquired.</param>
    /// <param name="metadata">The metadata associated with the holder.</param>
    /// <param name="logger">An optional logger used to report release failures during disposal.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> or <paramref name="metadata"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="semaphoreName"/> or <paramref name="holderId"/> is null or whitespace.</exception>
    public SemaphoreAcquisition(
        ISemaphoreProvider provider,
        string semaphoreName,
        string holderId,
        DateTimeOffset acquiredAt,
        IReadOnlyDictionary<string, string> metadata,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(semaphoreName);
        ArgumentException.ThrowIfNullOrWhiteSpace(holderId);
        ArgumentNullException.ThrowIfNull(metadata);

        HolderId = holderId;
        AcquiredAt = acquiredAt;
        Metadata = metadata;
        this.logger = logger;
        releaseAsync = () => new ValueTask(provider.ReleaseAsync(semaphoreName, holderId));
    }

    // Internal constructor used by SemaphoreService so that disposing the scope releases through
    // the service (stopping the heartbeat and updating status), not directly through the provider.
    internal SemaphoreAcquisition(
        string holderId,
        DateTimeOffset acquiredAt,
        IReadOnlyDictionary<string, string> metadata,
        Func<ValueTask> releaseAsync,
        ILogger? logger)
    {
        HolderId = holderId;
        AcquiredAt = acquiredAt;
        Metadata = metadata;
        this.releaseAsync = releaseAsync;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the unique identifier of the holder.
    /// </summary>
    public string HolderId { get; }

    /// <summary>
    /// Gets the time when the slot was acquired.
    /// </summary>
    public DateTimeOffset AcquiredAt { get; }

    /// <summary>
    /// Gets the metadata associated with the holder.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Gets a value indicating whether the acquisition has been disposed.
    /// </summary>
    public bool IsDisposed => isDisposed;

    /// <summary>
    /// Releases the semaphore slot asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        try
        {
            await releaseAsync();
        }
        catch (Exception ex)
        {
            // Never throw from disposal (it would mask other exceptions), but do not fail silently:
            // a release failure means the slot lingers until its heartbeat expires, which is worth
            // surfacing in logs.
            logger?.LogWarning(ex, "Failed to release semaphore slot for holder {HolderId} during disposal", HolderId);
        }
    }

    /// <summary>
    /// Releases the semaphore slot synchronously.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="DisposeAsync"/> in async contexts. This synchronous path blocks the
    /// calling thread and should not be used when a synchronization context is active.
    /// </remarks>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

using System.Text;
using System.Text.Json;
using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;

namespace MultiLock.Consul;

/// <summary>
/// Consul implementation of the semaphore provider.
/// Uses Consul sessions and key-value store for distributed semaphore coordination.
/// </summary>
/// <remarks>
/// Each holder writes its own session-bound key, so acquisition is bounded <em>optimistically</em>
/// rather than via a single atomic compare-and-set: a strongly consistent pre-check is followed by
/// the acquire and a strongly consistent post-check that rolls back (destroys the session, which
/// deletes the key) if the live count exceeded <c>maxCount</c>. This never grants more than
/// <c>maxCount</c> slots under consistent reads, but under heavy contention competing callers may
/// transiently roll each other back and spuriously report the semaphore as full; callers retry on
/// their normal acquisition interval. All count reads use <see cref="ConsistencyMode.Consistent"/>;
/// correctness depends on that consistency guarantee.
/// </remarks>
public sealed class ConsulSemaphoreProvider : ISemaphoreProvider, IAsyncDisposable
{
    private readonly ConsulSemaphoreOptions options;
    private readonly ILogger<ConsulSemaphoreProvider> logger;
    private readonly ConsulClient consulClient;
    private readonly Dictionary<string, Dictionary<string, string>> activeSessions = new();
    private readonly object sessionLock = new();
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsulSemaphoreProvider"/> class.
    /// </summary>
    /// <param name="options">The Consul options.</param>
    /// <param name="logger">The logger.</param>
    public ConsulSemaphoreProvider(
        IOptions<ConsulSemaphoreOptions> options,
        ILogger<ConsulSemaphoreProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();

        var consulConfig = new ConsulClientConfiguration
        {
            Address = new Uri(this.options.Address)
        };

        if (!string.IsNullOrEmpty(this.options.Datacenter))
            consulConfig.Datacenter = this.options.Datacenter;

        if (!string.IsNullOrEmpty(this.options.Token))
            consulConfig.Token = this.options.Token;

        consulClient = new ConsulClient(consulConfig);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(
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

        try
        {
            // Check if we already have a session for this holder
            string? existingSessionId = GetExistingSession(semaphoreName, holderId);
            if (existingSessionId != null)
            {
                // Renew the session, but only treat this as a successful renewal if the holder key
                // still exists. The session can outlive its key (e.g. the key was deleted out of
                // band), in which case the slot is genuinely lost and we must re-acquire rather than
                // falsely report success.
                WriteResult<SessionEntry>? renewResult = await consulClient.Session.Renew(existingSessionId, cancellationToken);
                if (renewResult.Response != null
                    && await UpdateHolderKeyAsync(semaphoreName, holderId, existingSessionId, metadata, cancellationToken))
                {
                    logger.LogInformation("Renewed slot for holder {HolderId} in semaphore {SemaphoreName}",
                        holderId, semaphoreName);
                    return true;
                }

                // Renewal failed or the slot was lost: drop the stale session before re-acquiring so
                // it does not linger and consume capacity until its TTL expires.
                RemoveSession(semaphoreName, holderId);
                try
                {
                    await consulClient.Session.Destroy(existingSessionId, cancellationToken);
                }
                catch (Exception destroyEx)
                {
                    logger.LogDebug(destroyEx,
                        "Failed to destroy stale session {SessionId} for holder {HolderId} before re-acquire",
                        existingSessionId, holderId);
                }
            }

            // Pre-check: fast-fail before spending a session creation round-trip. This is only an
            // optimisation hint; correctness is enforced by the post-acquisition check below, which
            // (like this one) reads with strong consistency so the count reflects committed state.
            int currentCount = await GetCurrentCountAsync(semaphoreName, slotTimeout, cancellationToken);
            if (currentCount >= maxCount)
            {
                logger.LogDebug("Failed to acquire slot for holder {HolderId} in semaphore {SemaphoreName} - full ({CurrentCount}/{MaxCount})",
                    holderId, semaphoreName, currentCount, maxCount);
                return false;
            }

            // Create a session for this holder.
            // Track it locally so we can destroy it on any failure path before AddSession commits it.
            string sessionId = await CreateSessionAsync(holderId, cancellationToken);
            bool sessionRegistered = false;

            try
            {
                string holderKey = GetHolderKey(semaphoreName, holderId);

                var holderData = new HolderData
                {
                    HolderId = holderId,
                    AcquiredAt = DateTimeOffset.UtcNow,
                    LastHeartbeat = DateTimeOffset.UtcNow,
                    Metadata = new Dictionary<string, string>(metadata),
                    SessionId = sessionId
                };

                string json = JsonSerializer.Serialize(holderData);
                byte[] value = Encoding.UTF8.GetBytes(json);

                var kvPair = new KVPair(holderKey)
                {
                    Value = value,
                    Session = sessionId
                };

                WriteResult<bool>? acquireResult = await consulClient.KV.Acquire(kvPair, cancellationToken);

                if (!acquireResult.Response)
                {
                    logger.LogDebug("Failed to acquire slot for holder {HolderId} in semaphore {SemaphoreName}",
                        holderId, semaphoreName);
                    return false;
                }

                // Post-acquisition safety check: multiple callers may have passed the pre-check
                // simultaneously. If our acquisition pushed the live count over maxCount, roll back
                // by destroying the session, which auto-deletes the holder key (SessionBehavior.Delete).
                int postCount = await GetCurrentCountAsync(semaphoreName, slotTimeout, cancellationToken);
                if (postCount > maxCount)
                {
                    logger.LogDebug(
                        "Rolled back slot for holder {HolderId} in semaphore {SemaphoreName} - concurrent acquisition exceeded maxCount",
                        holderId, semaphoreName);
                    return false;
                }

                AddSession(semaphoreName, holderId, sessionId);
                sessionRegistered = true;
                logger.LogInformation("Acquired slot for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
                return true;
            }
            finally
            {
                // Destroy the Consul session if we didn't successfully register it locally.
                // This prevents orphaned sessions when an exception occurs between CreateSession
                // and AddSession, or when we decide to roll back (Acquire returned false, post-count exceeded).
                if (!sessionRegistered)
                {
                    try
                    {
                        await consulClient.Session.Destroy(sessionId, CancellationToken.None);
                    }
                    catch (Exception cleanupEx)
                    {
                        logger.LogWarning(cleanupEx,
                            "Failed to destroy orphaned Consul session {SessionId} for holder {HolderId}",
                            sessionId, holderId);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not SemaphoreException)
        {
            logger.LogError(ex, "Error acquiring slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to acquire semaphore slot", ex);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);

        try
        {
            // Look up without removing first: if Destroy fails the local mapping is preserved,
            // so a subsequent ReleaseAsync call can retry.
            string? sessionId = GetExistingSession(semaphoreName, holderId);
            if (sessionId == null)
            {
                logger.LogDebug("No active session found for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
                return;
            }

            await consulClient.Session.Destroy(sessionId, cancellationToken);
            // Only remove from local state after a successful Destroy.
            RemoveSession(semaphoreName, holderId);
            logger.LogInformation("Released slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error releasing slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to release semaphore slot", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateHeartbeatAsync(
        string semaphoreName,
        string holderId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);
        ParameterValidation.ValidateMetadata(metadata);

        try
        {
            string? sessionId = GetExistingSession(semaphoreName, holderId);
            if (sessionId == null)
            {
                logger.LogWarning("Heartbeat update failed for holder {HolderId} in semaphore {SemaphoreName} - not a holder",
                    holderId, semaphoreName);
                return false;
            }

            WriteResult<SessionEntry>? renewResult = await consulClient.Session.Renew(sessionId, cancellationToken);
            if (renewResult.Response == null)
            {
                RemoveSession(semaphoreName, holderId);
                return false;
            }

            // If the holder key has been reaped the slot is lost even though the session renewed;
            // drop local tracking and report the heartbeat as failed so the service re-acquires.
            if (!await UpdateHolderKeyAsync(semaphoreName, holderId, sessionId, metadata, cancellationToken))
            {
                RemoveSession(semaphoreName, holderId);
                logger.LogWarning("Heartbeat update for holder {HolderId} in semaphore {SemaphoreName} found no holder key - slot lost",
                    holderId, semaphoreName);
                return false;
            }

            logger.LogDebug("Updated heartbeat for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating heartbeat for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to update heartbeat", ex);
        }
    }

    /// <inheritdoc />
    public async Task<int> GetCurrentCountAsync(
        string semaphoreName,
        TimeSpan slotTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);

        try
        {
            // Consul session TTL governs expiry, so listing active KV entries gives the live count.
            // Use a strongly consistent read (not the default, which may serve a stale follower
            // response): the acquire path relies on this count for its pre/post capacity checks, so
            // an under-count from a stale replica could let the holder count exceed maxCount.
            string prefix = GetSemaphorePrefix(semaphoreName);
            var queryOptions = new QueryOptions { Consistency = ConsistencyMode.Consistent };
            QueryResult<KVPair[]>? listResult = await consulClient.KV.List(prefix, queryOptions, cancellationToken);
            return listResult.Response?.Length ?? 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting current count for semaphore {SemaphoreName}", semaphoreName);
            throw new SemaphoreProviderException("Failed to get current count", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemaphoreHolder>> GetHoldersAsync(
        string semaphoreName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);

        try
        {
            string prefix = GetSemaphorePrefix(semaphoreName);
            QueryResult<KVPair[]>? listResult = await consulClient.KV.List(prefix, cancellationToken);

            if (listResult.Response == null)
                return [];

            var holders = new List<SemaphoreHolder>();
            foreach (KVPair kvPair in listResult.Response)
            {
                if (kvPair.Value == null) continue;
                HolderData? holderData = JsonSerializer.Deserialize<HolderData>(Encoding.UTF8.GetString(kvPair.Value));
                if (holderData != null)
                    holders.Add(new SemaphoreHolder(holderData.HolderId, holderData.AcquiredAt, holderData.LastHeartbeat, holderData.Metadata));
                else
                    logger.LogWarning("Skipping holder key {Key} in semaphore {SemaphoreName} - data could not be deserialized",
                        kvPair.Key, semaphoreName);
            }
            return holders;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting holders for semaphore {SemaphoreName}", semaphoreName);
            throw new SemaphoreProviderException("Failed to get holders", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHoldingAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);

        try
        {
            string holderKey = GetHolderKey(semaphoreName, holderId);
            QueryResult<KVPair>? getResult = await consulClient.KV.Get(holderKey, cancellationToken);
            return getResult.Response?.Value != null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if holder {HolderId} is holding semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to check holding status", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
            return false;

        try
        {
            QueryResult<Dictionary<string, Dictionary<string, dynamic>>>? result = await consulClient.Agent.Self(cancellationToken);
            return result.StatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for Consul semaphore provider");
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        var sessionsToDestroy = new List<string>();
        lock (sessionLock)
        {
            foreach (Dictionary<string, string> holderSessions in activeSessions.Values)
                sessionsToDestroy.AddRange(holderSessions.Values);
            activeSessions.Clear();
        }

        foreach (string sessionId in sessionsToDestroy)
        {
            try
            {
                await consulClient.Session.Destroy(sessionId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error destroying session {SessionId} during disposal", sessionId);
            }
        }

        consulClient.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Prefer <see cref="DisposeAsync"/> in async contexts. This synchronous path blocks the
    /// calling thread and should not be used when a synchronization context is active.
    /// </remarks>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void ThrowIfDisposed()
    {
        if (!isDisposed) return;
        throw new ObjectDisposedException(nameof(ConsulSemaphoreProvider));
    }

    private async Task<string> CreateSessionAsync(string holderId, CancellationToken cancellationToken)
    {
        var sessionEntry = new SessionEntry
        {
            Name = $"semaphore-{holderId}",
            TTL = options.SessionTtl,
            LockDelay = options.SessionLockDelay,
            Behavior = SessionBehavior.Delete
        };

        WriteResult<string>? createResult = await consulClient.Session.Create(sessionEntry, cancellationToken);
        if (string.IsNullOrEmpty(createResult?.Response))
            throw new SemaphoreProviderException("Failed to create Consul session: empty session ID returned.");
        return createResult.Response;
    }

    /// <summary>
    /// Refreshes the holder key's heartbeat and metadata.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the holder key still exists (and the heartbeat was refreshed);
    /// <c>false</c> if the key no longer exists, meaning the slot has been lost.
    /// </returns>
    private async Task<bool> UpdateHolderKeyAsync(
        string semaphoreName,
        string holderId,
        string sessionId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        string holderKey = GetHolderKey(semaphoreName, holderId);
        QueryResult<KVPair>? getResult = await consulClient.KV.Get(holderKey, cancellationToken);

        if (getResult.Response?.Value == null)
            return false;

        HolderData? existingData = JsonSerializer.Deserialize<HolderData>(Encoding.UTF8.GetString(getResult.Response.Value));
        if (existingData == null)
        {
            logger.LogWarning(
                "Holder key {HolderKey} in semaphore {SemaphoreName} contained data that could not be deserialized; treating the slot as lost",
                holderKey, semaphoreName);
            return false;
        }

        existingData.LastHeartbeat = DateTimeOffset.UtcNow;
        existingData.Metadata = new Dictionary<string, string>(metadata);

        string json = JsonSerializer.Serialize(existingData);
        byte[] value = Encoding.UTF8.GetBytes(json);

        var kvPair = new KVPair(holderKey)
        {
            Value = value,
            Session = sessionId,
            ModifyIndex = getResult.Response.ModifyIndex
        };

        WriteResult<bool> casResult = await consulClient.KV.CAS(kvPair, cancellationToken);
        if (!casResult.Response)
            logger.LogDebug("CAS heartbeat update for holder {HolderId} in semaphore {SemaphoreName} was rejected (concurrent modification)", holderId, semaphoreName);

        // The key existed; a rejected CAS just means a concurrent refresh won the race, which still
        // means the holder is alive. Only a missing key (handled above) indicates a lost slot.
        return true;
    }

    private string GetSemaphorePrefix(string semaphoreName)
    {
        return $"{options.KeyPrefix}/{semaphoreName}/";
    }

    private string GetHolderKey(string semaphoreName, string holderId)
    {
        return $"{options.KeyPrefix}/{semaphoreName}/{holderId}";
    }

    private string? GetExistingSession(string semaphoreName, string holderId)
    {
        lock (sessionLock)
        {
            if (activeSessions.TryGetValue(semaphoreName, out Dictionary<string, string>? holderSessions))
                if (holderSessions.TryGetValue(holderId, out string? sessionId))
                    return sessionId;
            return null;
        }
    }

    private void AddSession(string semaphoreName, string holderId, string sessionId)
    {
        lock (sessionLock)
        {
            if (!activeSessions.TryGetValue(semaphoreName, out Dictionary<string, string>? holderSessions))
            {
                holderSessions = new Dictionary<string, string>();
                activeSessions[semaphoreName] = holderSessions;
            }
            holderSessions[holderId] = sessionId;
        }
    }

    private string? RemoveSession(string semaphoreName, string holderId)
    {
        lock (sessionLock)
        {
            if (!activeSessions.TryGetValue(semaphoreName, out Dictionary<string, string>? holderSessions))
                return null;
            if (!holderSessions.Remove(holderId, out string? sessionId))
                return null;
            if (holderSessions.Count == 0)
                activeSessions.Remove(semaphoreName);
            return sessionId;
        }
    }

    /// <summary>
    /// Internal class to represent holder data stored in Consul.
    /// </summary>
    private sealed class HolderData
    {
        public string HolderId { get; init; } = string.Empty;
        public DateTimeOffset AcquiredAt { get; init; }
        public DateTimeOffset LastHeartbeat { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public string SessionId { get; init; } = string.Empty;
    }
}

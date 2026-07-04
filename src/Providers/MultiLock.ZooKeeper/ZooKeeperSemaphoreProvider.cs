using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;
using org.apache.zookeeper;
using org.apache.zookeeper.data;

namespace MultiLock.ZooKeeper;

/// <summary>
/// ZooKeeper implementation of the semaphore provider.
/// Uses ZooKeeper ephemeral sequential nodes for distributed counting semaphore.
/// </summary>
/// <remarks>
/// Expiry semantics differ from the other providers. A slot is held by an <em>ephemeral</em> znode
/// that lives as long as the holder's ZooKeeper session, so liveness is governed by the ZooKeeper
/// session timeout rather than by the <c>slotTimeout</c> heartbeat window: the <c>slotTimeout</c>
/// argument is not used to expire slots, and <see cref="GetCurrentCountAsync"/> counts live znodes
/// regardless of last-heartbeat age. A holder that stops calling <see cref="UpdateHeartbeatAsync"/>
/// but keeps its session alive therefore retains its slot until the session ends, at which point
/// ZooKeeper removes the ephemeral node automatically and frees the slot. If the session expires,
/// the slot is lost immediately; the next <see cref="UpdateHeartbeatAsync"/> call detects this
/// (its node no longer exists) and reports the slot as lost so the service re-acquires.
/// Tune the configured session timeout to match the desired failure-detection window.
/// </remarks>
public sealed class ZooKeeperSemaphoreProvider : Watcher, ISemaphoreProvider, IAsyncDisposable
{
    private readonly ZooKeeperSemaphoreOptions options;
    private readonly ILogger<ZooKeeperSemaphoreProvider> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly Dictionary<string, Dictionary<string, string>> activeNodes = new();
    private readonly object nodeLock = new();
    private volatile bool isInitialized;
    private volatile bool isDisposed;
    private volatile bool sessionExpired;
    private org.apache.zookeeper.ZooKeeper? zooKeeper;
    private TaskCompletionSource? connectionCompletionSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZooKeeperSemaphoreProvider"/> class.
    /// </summary>
    /// <param name="options">The ZooKeeper options.</param>
    /// <param name="logger">The logger.</param>
    public ZooKeeperSemaphoreProvider(
        IOptions<ZooKeeperSemaphoreOptions> options,
        ILogger<ZooKeeperSemaphoreProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string semaphorePath = GetSemaphorePath(semaphoreName);
            await EnsurePathExistsAsync(semaphorePath);

            // Check if we already have a node for this semaphore/holder
            string? existingPath = GetExistingNode(semaphoreName, holderId);

            if (existingPath != null)
            {
                Stat? stat = await zooKeeper!.existsAsync(existingPath);
                if (stat != null)
                {
                    // Already holding a slot
                    logger.LogDebug("Holder {HolderId} already has a slot in semaphore {SemaphoreName}",
                        holderId, semaphoreName);
                    return true;
                }
                // Node doesn't exist anymore, remove from tracking
                RemoveNode(semaphoreName, holderId);
            }

            // Get current count
            ChildrenResult? children = await zooKeeper!.getChildrenAsync(semaphorePath);
            if (children.Children.Count >= maxCount)
            {
                logger.LogDebug("Semaphore {SemaphoreName} is full ({Count}/{MaxCount})",
                    semaphoreName, children.Children.Count, maxCount);
                return false;
            }

            // Create ephemeral sequential node
            string createdPath = await CreateHolderNodeAsync(semaphorePath, holderId, metadata);
            AddNode(semaphoreName, holderId, createdPath);

            // Verify we're within the limit (race condition check)
            children = await zooKeeper.getChildrenAsync(semaphorePath);
            // Order by the ZooKeeper-assigned sequence suffix, NOT the full node name: names are
            // "{holderId}-{seq}", so a lexicographic sort is dominated by the holder ID and does
            // not reflect creation order. Sorting by name would let a later creator whose ID sorts
            // before an established holder's rank itself within maxCount and be admitted, pushing
            // the live holder count past the limit. Sequence order makes the position check match
            // actual acquisition order so exactly the first maxCount creators keep their slots.
            var sortedChildren = children.Children.OrderBy(GetSequenceNumber).ToList();
            string createdNodeName = createdPath[(createdPath.LastIndexOf('/') + 1)..];
            int position = sortedChildren.IndexOf(createdNodeName);

            // position == -1 means the node was already deleted (e.g., session expired between
            // create and the children refresh); treat it the same as being over the limit.
            if (position < 0 || position >= maxCount)
            {
                // We're over the limit or the node disappeared - release our slot
                RemoveNode(semaphoreName, holderId);
                try
                {
                    await zooKeeper.deleteAsync(createdPath);
                }
                catch (KeeperException.NoNodeException)
                {
                    // Already gone, nothing to do
                }
                logger.LogDebug("Semaphore {SemaphoreName} became full during acquisition (position={Position}), releasing slot",
                    semaphoreName, position);
                return false;
            }

            logger.LogInformation("Holder {HolderId} acquired slot in semaphore {SemaphoreName} (position {Position}/{MaxCount})",
                holderId, semaphoreName, position + 1, maxCount);
            return true;
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            HandleSessionExpired(ex, "acquiring slot", holderId, semaphoreName);
            throw new SemaphoreProviderException("ZooKeeper session expired", ex);
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string? nodePath = RemoveNode(semaphoreName, holderId);
            if (nodePath == null)
            {
                logger.LogWarning("No active node found for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
                return;
            }

            await zooKeeper!.deleteAsync(nodePath);
            logger.LogInformation("Holder {HolderId} released slot in semaphore {SemaphoreName}",
                holderId, semaphoreName);
        }
        catch (KeeperException.NoNodeException)
        {
            // Node already deleted, ignore
            logger.LogDebug("Node already deleted for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            HandleSessionExpired(ex, "releasing slot", holderId, semaphoreName);
            throw new SemaphoreProviderException("ZooKeeper session expired", ex);
        }
        catch (Exception ex) when (ex is not SemaphoreException)
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string? nodePath = GetExistingNode(semaphoreName, holderId);
            if (nodePath == null)
                return false;

            DataResult? result = await zooKeeper!.getDataAsync(nodePath);
            if (result?.Data == null)
                return false;

            HolderData? existingData = JsonSerializer.Deserialize<HolderData>(Encoding.UTF8.GetString(result.Data));
            if (existingData?.HolderId != holderId)
                return false;

            existingData.LastHeartbeat = DateTimeOffset.UtcNow;
            existingData.Metadata = new Dictionary<string, string>(metadata);

            string json = JsonSerializer.Serialize(existingData);
            byte[] data = Encoding.UTF8.GetBytes(json);
            await zooKeeper.setDataAsync(nodePath, data, result.Stat.getVersion());

            logger.LogDebug("Updated heartbeat for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            return true;
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            HandleSessionExpired(ex, "updating heartbeat", holderId, semaphoreName);
            return false;
        }
        catch (Exception ex) when (ex is KeeperException.NoNodeException or KeeperException.BadVersionException)
        {
            // The node was removed (e.g. session lapsed and the ephemeral node was reaped, or it was
            // deleted out of band) or updated concurrently: the slot is lost. Drop stale tracking and
            // report the heartbeat as failed so the service re-acquires, per the provider contract.
            RemoveNode(semaphoreName, holderId);
            logger.LogWarning("Heartbeat for holder {HolderId} in semaphore {SemaphoreName} found no live node - slot lost",
                holderId, semaphoreName);
            return false;
        }
        catch (Exception ex) when (ex is not SemaphoreException)
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            // ZooKeeper ephemeral nodes are automatically deleted when the session expires,
            // so counting children gives the live holder count without additional filtering.
            string semaphorePath = GetSemaphorePath(semaphoreName);
            Stat? pathStat = await zooKeeper!.existsAsync(semaphorePath);
            if (pathStat == null)
                return 0;

            ChildrenResult? children = await zooKeeper.getChildrenAsync(semaphorePath);
            return children.Children.Count;
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            logger.LogWarning(ex, "ZooKeeper session expired while getting count for semaphore {SemaphoreName}",
                semaphoreName);
            sessionExpired = true;
            isInitialized = false;
            throw new SemaphoreProviderException("ZooKeeper session expired", ex);
        }
        catch (Exception ex) when (ex is not SemaphoreException)
        {
            logger.LogError(ex, "Error getting count for semaphore {SemaphoreName}", semaphoreName);
            throw new SemaphoreProviderException("Failed to get semaphore count", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemaphoreHolder>> GetHoldersAsync(
        string semaphoreName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string semaphorePath = GetSemaphorePath(semaphoreName);
            Stat? pathStat = await zooKeeper!.existsAsync(semaphorePath);
            if (pathStat == null)
                return [];

            ChildrenResult? children = await zooKeeper.getChildrenAsync(semaphorePath);
            var holders = new List<SemaphoreHolder>();

            foreach (string child in children.Children)
            {
                string childPath = $"{semaphorePath}/{child}";
                try
                {
                    DataResult? result = await zooKeeper.getDataAsync(childPath);
                    HolderData? holderData = JsonSerializer.Deserialize<HolderData>(Encoding.UTF8.GetString(result.Data));
                    if (holderData != null)
                    {
                        holders.Add(new SemaphoreHolder(
                            holderData.HolderId,
                            holderData.AcquiredAt,
                            holderData.LastHeartbeat,
                            holderData.Metadata));
                    }
                }
                catch (KeeperException.NoNodeException)
                {
                    // Node was deleted between listing and reading, skip
                }
            }

            return holders;
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            logger.LogWarning(ex, "ZooKeeper session expired while getting holders for semaphore {SemaphoreName}",
                semaphoreName);
            sessionExpired = true;
            isInitialized = false;
            throw new SemaphoreProviderException("ZooKeeper session expired", ex);
        }
        catch (Exception ex) when (ex is not SemaphoreException)
        {
            logger.LogError(ex, "Error getting holders for semaphore {SemaphoreName}", semaphoreName);
            throw new SemaphoreProviderException("Failed to get semaphore holders", ex);
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string? nodePath = GetExistingNode(semaphoreName, holderId);
            if (nodePath == null)
                return false;

            Stat? stat = await zooKeeper!.existsAsync(nodePath);
            return stat != null;
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            HandleSessionExpired(ex, "checking holding status", holderId, semaphoreName);
            return false;
        }
        catch (Exception ex) when (ex is not SemaphoreException)
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
            await EnsureInitializedAsync(cancellationToken);
            org.apache.zookeeper.ZooKeeper.States? state = zooKeeper?.getState();
            return state == org.apache.zookeeper.ZooKeeper.States.CONNECTED;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for ZooKeeper semaphore provider");
            return false;
        }
    }

    /// <summary>
    /// Asynchronously disposes the ZooKeeper semaphore provider and releases all resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        if (zooKeeper != null)
        {
            try
            {
                await zooKeeper.closeAsync();
            }
            catch (Exception ex) when (ex is not SystemException)
            {
                logger.LogWarning(ex, "Error closing ZooKeeper connection during disposal");
            }
        }

        initializationLock.Dispose();
    }

    /// <summary>
    /// Synchronously disposes the ZooKeeper semaphore provider.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="DisposeAsync"/> in async contexts. This synchronous path blocks the
    /// calling thread and should not be used when a synchronization context is active.
    /// </remarks>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override Task process(WatchedEvent @event)
    {
        logger.LogDebug("ZooKeeper event received: {EventType} for path {Path}", @event.get_Type(), @event.getPath());

        if (@event.get_Type() != Event.EventType.None)
            return Task.CompletedTask;

        Event.KeeperState state = @event.getState();
        switch (state)
        {
            case Event.KeeperState.Expired:
                logger.LogWarning("ZooKeeper session expired, marking for reconnection");
                sessionExpired = true;
                isInitialized = false;
                lock (nodeLock)
                {
                    activeNodes.Clear();
                }
                break;
            case Event.KeeperState.Disconnected:
                logger.LogWarning("ZooKeeper connection lost");
                break;
            case Event.KeeperState.SyncConnected:
                logger.LogInformation("ZooKeeper connection established/restored");
                connectionCompletionSource?.TrySetResult();
                break;
        }

        return Task.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (!isDisposed) return;
        throw new ObjectDisposedException(nameof(ZooKeeperSemaphoreProvider));
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (isInitialized && !sessionExpired)
            return;

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (isInitialized && !sessionExpired)
                return;

            if (sessionExpired && zooKeeper != null)
            {
                try
                {
                    await zooKeeper.closeAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error closing expired ZooKeeper session");
                }
                zooKeeper = null;
                sessionExpired = false;
            }

            string connectionString = options.ConnectionString;
            int sessionTimeout = (int)options.SessionTimeout.TotalMilliseconds;

            connectionCompletionSource = new TaskCompletionSource();
            zooKeeper = new org.apache.zookeeper.ZooKeeper(connectionString, sessionTimeout, this);

            if (zooKeeper.getState() == org.apache.zookeeper.ZooKeeper.States.CONNECTED)
                connectionCompletionSource.TrySetResult();

            TimeSpan connectionTimeout = options.ConnectionTimeout;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(connectionTimeout);

            try
            {
                // WaitAsync links the TaskCompletionSource to the timeout/caller token so that
                // cancellation actually propagates; awaiting the Task directly never cancels.
                await connectionCompletionSource.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Close the ZooKeeper instance that was created but never connected, so that
                // its background socket/watcher threads are not leaked.
                var leakedZk = zooKeeper;
                zooKeeper = null;
                if (leakedZk != null)
                {
                    try
                    {
                        await leakedZk.closeAsync();
                    }
                    catch (Exception closeEx)
                    {
                        logger.LogWarning(closeEx, "Error closing ZooKeeper instance after connection timeout");
                    }
                }
                throw new SemaphoreProviderException("Failed to connect to ZooKeeper within timeout");
            }
            finally
            {
                connectionCompletionSource = null;
            }

            if (options.AutoCreateRootPath)
                await EnsurePathExistsAsync(options.RootPath);

            isInitialized = true;
            logger.LogInformation("ZooKeeper connection established to {ConnectionString}", options.ConnectionString);
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task EnsurePathExistsAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return;

        Stat? stat = await zooKeeper!.existsAsync(path);
        if (stat != null)
            return;

        string parentPath = path[..path.LastIndexOf('/')];
        if (!string.IsNullOrEmpty(parentPath) && parentPath != "/")
            await EnsurePathExistsAsync(parentPath);

        try
        {
            await zooKeeper.createAsync(path, [], ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT);
            logger.LogDebug("Created ZooKeeper path: {Path}", path);
        }
        catch (KeeperException.NodeExistsException)
        {
            // Path was created by another process, ignore
        }
    }

    private async Task<string> CreateHolderNodeAsync(
        string semaphorePath,
        string holderId,
        IReadOnlyDictionary<string, string> metadata)
    {
        var holderData = new HolderData
        {
            HolderId = holderId,
            AcquiredAt = DateTimeOffset.UtcNow,
            LastHeartbeat = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>(metadata)
        };

        string json = JsonSerializer.Serialize(holderData);
        byte[] data = Encoding.UTF8.GetBytes(json);

        string nodePath = $"{semaphorePath}/{holderId}-";
        return await zooKeeper!.createAsync(nodePath, data, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.EPHEMERAL_SEQUENTIAL);
    }

    private string GetSemaphorePath(string semaphoreName)
    {
        return $"{options.RootPath}/{semaphoreName}";
    }

    // Extracts the ZooKeeper-assigned sequence number from a sequential node name of the form
    // "{holderId}-{seq}". Holder IDs may themselves contain hyphens, so take the suffix after the
    // LAST hyphen. Nodes whose suffix cannot be parsed (unexpected foreign nodes) sort last so
    // they can never displace a validly created holder from its position.
    internal static long GetSequenceNumber(string nodeName)
    {
        int separatorIndex = nodeName.LastIndexOf('-');
        if (separatorIndex >= 0 && separatorIndex < nodeName.Length - 1
            && long.TryParse(nodeName[(separatorIndex + 1)..], out long sequence))
        {
            return sequence;
        }

        return long.MaxValue;
    }

    private string? GetExistingNode(string semaphoreName, string holderId)
    {
        lock (nodeLock)
        {
            if (activeNodes.TryGetValue(semaphoreName, out Dictionary<string, string>? holderNodes))
                if (holderNodes.TryGetValue(holderId, out string? nodePath))
                    return nodePath;
            return null;
        }
    }

    private void AddNode(string semaphoreName, string holderId, string nodePath)
    {
        lock (nodeLock)
        {
            if (!activeNodes.TryGetValue(semaphoreName, out Dictionary<string, string>? holderNodes))
            {
                holderNodes = new Dictionary<string, string>();
                activeNodes[semaphoreName] = holderNodes;
            }
            holderNodes[holderId] = nodePath;
        }
    }

    private string? RemoveNode(string semaphoreName, string holderId)
    {
        lock (nodeLock)
        {
            if (!activeNodes.TryGetValue(semaphoreName, out Dictionary<string, string>? holderNodes))
                return null;
            if (!holderNodes.Remove(holderId, out string? nodePath))
                return null;
            if (holderNodes.Count == 0)
                activeNodes.Remove(semaphoreName);
            return nodePath;
        }
    }

    private void HandleSessionExpired(KeeperException.SessionExpiredException ex, string operation, string holderId, string semaphoreName)
    {
        logger.LogWarning(ex, "ZooKeeper session expired while {Operation} for holder {HolderId} in semaphore {SemaphoreName}",
            operation, holderId, semaphoreName);
        sessionExpired = true;
        isInitialized = false;
        lock (nodeLock)
        {
            // Session expiry invalidates ALL ephemeral nodes, not just those for the given semaphore.
            activeNodes.Clear();
        }
    }

    /// <summary>
    /// Internal class to represent holder data stored in ZooKeeper.
    /// </summary>
    private sealed class HolderData
    {
        public string HolderId { get; init; } = string.Empty;
        public DateTimeOffset AcquiredAt { get; init; }
        public DateTimeOffset LastHeartbeat { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}


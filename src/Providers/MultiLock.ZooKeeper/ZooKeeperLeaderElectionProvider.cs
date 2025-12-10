using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;
using org.apache.zookeeper;
using org.apache.zookeeper.data;

namespace MultiLock.ZooKeeper;

/// <summary>
/// Data structure for storing leader information in ZooKeeper.
/// </summary>
internal sealed class ZooKeeperLeaderData
{
    public string LeaderId { get; set; } = string.Empty;
    public DateTimeOffset LeadershipAcquiredAt { get; set; }
    public DateTimeOffset LastHeartbeat { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// ZooKeeper implementation of the leader election provider.
/// Uses ZooKeeper ephemeral sequential nodes for distributed coordination.
/// </summary>
public sealed class ZooKeeperLeaderElectionProvider : Watcher, ILeaderElectionProvider, IAsyncDisposable
{
    private readonly ZooKeeperLeaderElectionOptions options;
    private readonly ILogger<ZooKeeperLeaderElectionProvider> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly Dictionary<string, string> activeNodes = new();
    private readonly object nodeLock = new();
    private volatile bool isInitialized;
    private volatile bool isDisposed;
    private volatile bool sessionExpired;
    private org.apache.zookeeper.ZooKeeper? zooKeeper;
    private TaskCompletionSource? connectionCompletionSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZooKeeperLeaderElectionProvider"/> class.
    /// </summary>
    /// <param name="options">The ZooKeeper options.</param>
    /// <param name="logger">The logger.</param>
    public ZooKeeperLeaderElectionProvider(
        IOptions<ZooKeeperLeaderElectionOptions> options,
        ILogger<ZooKeeperLeaderElectionProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireLeadershipAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        ParameterValidation.ValidateMetadata(metadata);
        ParameterValidation.ValidateLockTimeout(lockTimeout);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string electionPath = GetElectionPath(electionGroup);
            await EnsurePathExistsAsync(electionPath);

            string createdPath;
            string? existingPath;

            // Check if we already have a node for this election group
            lock (nodeLock)
            {
                activeNodes.TryGetValue(electionGroup, out existingPath);
            }

            if (existingPath != null)
            {
                // Check if the existing node still exists
                Stat? stat = await zooKeeper!.existsAsync(existingPath);
                if (stat != null)
                {
                    createdPath = existingPath;
                }
                else
                {
                    // Node doesn't exist anymore, remove from tracking and create new one
                    lock (nodeLock)
                    {
                        activeNodes.Remove(electionGroup);
                    }
                    createdPath = await CreateElectionNodeAsync(electionPath, participantId, metadata);
                    lock (nodeLock)
                    {
                        activeNodes[electionGroup] = createdPath;
                    }
                }
            }
            else
            {
                // Create new node
                createdPath = await CreateElectionNodeAsync(electionPath, participantId, metadata);
                lock (nodeLock)
                {
                    activeNodes[electionGroup] = createdPath;
                }
            }

            // Get all children and check if we're the leader (lowest sequence number)
            ChildrenResult? children = await zooKeeper!.getChildrenAsync(electionPath);
            var sortedChildren = children.Children.OrderBy(c => c).ToList();

            // Extract the node name from the created path
            string createdNodeName = createdPath.Substring(createdPath.LastIndexOf('/') + 1);
            bool isLeader = sortedChildren.Count > 0 && createdNodeName == sortedChildren[0];

            if (isLeader)
            {
                logger.LogInformation("Successfully acquired leadership for participant {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);

                return true;
            }
            else
            {
                // Not the leader, but keep the node for potential future leadership
                logger.LogDebug("Failed to acquire leadership for participant {ParticipantId} in group {ElectionGroup}. Node: {NodeName}, Leader: {LeaderNode}",
                    participantId, electionGroup, createdNodeName, sortedChildren.FirstOrDefault());

                return false;
            }
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            logger.LogWarning(ex, "ZooKeeper session expired while acquiring leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            sessionExpired = true;
            isInitialized = false;
            throw new LeaderElectionProviderException("ZooKeeper session expired", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error acquiring leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to acquire leadership", ex);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseLeadershipAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string? nodePath;
            lock (nodeLock)
            {
                if (!activeNodes.Remove(electionGroup, out nodePath))
                {
                    logger.LogWarning("No active node found for participant {ParticipantId} in group {ElectionGroup}",
                        participantId, electionGroup);
                    return;
                }
            }

            // Delete the ephemeral node
            await zooKeeper!.deleteAsync(nodePath);

            logger.LogInformation("Released leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            logger.LogWarning(ex, "ZooKeeper session expired while releasing leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            sessionExpired = true;
            isInitialized = false;
            // Clear the active node since session expired
            lock (nodeLock)
            {
                activeNodes.Remove(electionGroup);
            }
            throw new LeaderElectionProviderException("ZooKeeper session expired", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error releasing leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to release leadership", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateHeartbeatAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        ParameterValidation.ValidateMetadata(metadata);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string? nodePath;
            lock (nodeLock)
            {
                if (!activeNodes.TryGetValue(electionGroup, out nodePath))
                {
                    return false;
                }
            }

            // Get current data and update heartbeat
            Stat? stat = await zooKeeper!.existsAsync(nodePath);
            if (stat == null)
            {
                return false;
            }

            DataResult? result = await zooKeeper.getDataAsync(nodePath);
            ZooKeeperLeaderData? existingData = JsonSerializer.Deserialize<ZooKeeperLeaderData>(Encoding.UTF8.GetString(result.Data));

            if (existingData?.LeaderId != participantId)
            {
                return false;
            }

            existingData.LastHeartbeat = DateTimeOffset.UtcNow;
            existingData.Metadata = new Dictionary<string, string>(metadata);

            string json = JsonSerializer.Serialize(existingData);
            byte[] data = Encoding.UTF8.GetBytes(json);

            await zooKeeper.setDataAsync(nodePath, data, stat.getVersion());

            logger.LogDebug("Updated heartbeat for leader {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);

            return true;
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            logger.LogWarning(ex, "ZooKeeper session expired while updating heartbeat for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            sessionExpired = true;
            isInitialized = false;
            // Clear the active node since session expired
            lock (nodeLock)
            {
                activeNodes.Remove(electionGroup);
            }
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating heartbeat for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to update heartbeat", ex);
        }
    }

    /// <inheritdoc />
    public async Task<LeaderInfo?> GetCurrentLeaderAsync(
        string electionGroup,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string electionPath = GetElectionPath(electionGroup);

            // Check if election path exists
            Stat? pathStat = await zooKeeper!.existsAsync(electionPath);
            if (pathStat == null)
            {
                return null;
            }

            ChildrenResult? children = await zooKeeper.getChildrenAsync(electionPath);
            if (children.Children.Count == 0)
            {
                return null;
            }

            // Get the leader (node with lowest sequence number)
            var sortedChildren = children.Children.OrderBy(c => c).ToList();
            string? leaderNode = sortedChildren[0];
            string leaderPath = $"{electionPath}/{leaderNode}";

            DataResult? result = await zooKeeper.getDataAsync(leaderPath);
            ZooKeeperLeaderData? leaderData = JsonSerializer.Deserialize<ZooKeeperLeaderData>(Encoding.UTF8.GetString(result.Data));

            if (leaderData == null)
            {
                return null;
            }

            return new LeaderInfo(
                leaderData.LeaderId,
                leaderData.LeadershipAcquiredAt,
                leaderData.LastHeartbeat,
                leaderData.Metadata);
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            logger.LogWarning(ex, "ZooKeeper session expired while getting current leader for group {ElectionGroup}", electionGroup);
            sessionExpired = true;
            isInitialized = false;
            throw new LeaderElectionProviderException("ZooKeeper session expired", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting current leader for group {ElectionGroup}", electionGroup);
            throw new LeaderElectionProviderException("Failed to get current leader", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsLeaderAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            LeaderInfo? currentLeader = await GetCurrentLeaderAsync(electionGroup, cancellationToken);
            return currentLeader?.LeaderId == participantId;
        }
        catch (KeeperException.SessionExpiredException ex)
        {
            logger.LogWarning(ex, "ZooKeeper session expired while checking if participant {ParticipantId} is leader in group {ElectionGroup}",
                participantId, electionGroup);
            sessionExpired = true;
            isInitialized = false;
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if participant {ParticipantId} is leader in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to check leadership status", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
        {
            return false;
        }

        try
        {
            await EnsureInitializedAsync(cancellationToken);

            // Check if ZooKeeper connection is alive
            org.apache.zookeeper.ZooKeeper.States? state = zooKeeper?.getState();
            return state == org.apache.zookeeper.ZooKeeper.States.CONNECTED;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for ZooKeeper provider");
            return false;
        }
    }

    /// <summary>
    /// Asynchronously disposes the ZooKeeper leader election provider and releases all resources.
    /// </summary>
    /// <remarks>
    /// This method performs the following cleanup operations:
    /// <list type="bullet">
    /// <item><description>Closes the ZooKeeper connection asynchronously</description></item>
    /// <item><description>Disposes the initialization lock</description></item>
    /// </list>
    /// This method is idempotent and can be called multiple times safely.
    /// After disposal, all provider methods will throw <see cref="ObjectDisposedException"/>.
    /// </remarks>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous disposal operation.</returns>
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
    /// Synchronously disposes the ZooKeeper leader election provider and releases all resources.
    /// </summary>
    /// <remarks>
    /// This method calls <see cref="DisposeAsync"/> and blocks until completion.
    /// Prefer using <see cref="DisposeAsync"/> when possible to avoid blocking.
    /// This method is idempotent and can be called multiple times safely.
    /// After disposal, all provider methods will throw <see cref="ObjectDisposedException"/>.
    /// The disposal is executed on a thread pool thread to avoid potential deadlocks in synchronization contexts.
    /// </remarks>
    public void Dispose()
    {
        Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override Task process(WatchedEvent @event)
    {
        logger.LogDebug("ZooKeeper event received: {EventType} for path {Path}", @event.get_Type(), @event.getPath());

        // Handle session expiration
        if (@event.get_Type() != Event.EventType.None)
            return Task.CompletedTask;

        Event.KeeperState state = @event.getState();
        // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
        switch (state)
        {
            case Event.KeeperState.Expired:
            {
                logger.LogWarning("ZooKeeper session expired, marking for reconnection");
                sessionExpired = true;
                isInitialized = false;

                // Clear active nodes as they are no longer valid
                lock (nodeLock)
                {
                    activeNodes.Clear();
                }

                break;
            }
            case Event.KeeperState.Disconnected:
                logger.LogWarning("ZooKeeper connection lost");
                break;
            case Event.KeeperState.SyncConnected:
                logger.LogInformation("ZooKeeper connection established/restored");
                // Signal that the connection is now established
                connectionCompletionSource?.TrySetResult();
                break;
        }

        return Task.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (!isDisposed) return;
        throw new ObjectDisposedException(nameof(ZooKeeperLeaderElectionProvider));
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

            // Close existing connection if session expired
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

            // Create ZooKeeper connection
            string connectionString = options.ConnectionString;
            int sessionTimeout = (int)options.SessionTimeout.TotalMilliseconds;

            // Create a completion source to signal when connection is established
            connectionCompletionSource = new TaskCompletionSource();

            zooKeeper = new org.apache.zookeeper.ZooKeeper(connectionString, sessionTimeout, this);

            // Check if connection is already established (handles synchronous connection case)
            if (zooKeeper.getState() == org.apache.zookeeper.ZooKeeper.States.CONNECTED)
            {
                connectionCompletionSource.TrySetResult();
            }

            // Wait for connection to be established using event-based signaling
            TimeSpan connectionTimeout = options.ConnectionTimeout;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(connectionTimeout);

            try
            {
                await connectionCompletionSource.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new LeaderElectionProviderException("Failed to connect to ZooKeeper within timeout");
            }
            finally
            {
                connectionCompletionSource = null;
            }

            if (options.AutoCreateRootPath)
            {
                await EnsurePathExistsAsync(options.RootPath);
            }

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
        {
            return;
        }

        Stat? stat = await zooKeeper!.existsAsync(path);
        if (stat != null)
        {
            return;
        }

        // Ensure parent path exists first
        string parentPath = path[..path.LastIndexOf('/')];
        if (!string.IsNullOrEmpty(parentPath) && parentPath != "/")
        {
            await EnsurePathExistsAsync(parentPath);
        }

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

    private async Task<string> CreateElectionNodeAsync(string electionPath, string participantId, IReadOnlyDictionary<string, string> metadata)
    {
        var leaderData = new ZooKeeperLeaderData
        {
            LeaderId = participantId,
            LeadershipAcquiredAt = DateTimeOffset.UtcNow,
            LastHeartbeat = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>(metadata)
        };

        string json = JsonSerializer.Serialize(leaderData);
        byte[] data = Encoding.UTF8.GetBytes(json);

        // Create ephemeral sequential node
        string nodePath = $"{electionPath}/{participantId}-";
        return await zooKeeper!.createAsync(nodePath, data, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.EPHEMERAL_SEQUENTIAL);
    }

    private string GetElectionPath(string electionGroup)
    {
        return $"{options.RootPath}/{electionGroup}";
    }
}

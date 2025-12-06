using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MultiLock;

/// <summary>
/// Implementation of the leader election service that manages the election process and leadership lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// This service implements both <see cref="IDisposable"/> and <see cref="IAsyncDisposable"/> for proper resource cleanup.
/// When disposing, the service waits for active timer callbacks to complete before releasing resources,
/// preventing race conditions and ObjectDisposedException errors.
/// </para>
/// <para>
/// <strong>Disposal Best Practices:</strong>
/// <list type="bullet">
/// <item><description>Prefer using <c>await DisposeAsync()</c> when possible for proper async disposal.</description></item>
/// <item><description>The synchronous <c>Dispose()</c> method delegates to <c>DisposeAsync()</c> and blocks until completion.</description></item>
/// <item><description>Disposal is idempotent - calling Dispose/DisposeAsync multiple times is safe.</description></item>
/// <item><description>The service waits up to 10 seconds for active callbacks to complete during disposal.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class LeaderElectionService : BackgroundService, ILeaderElectionService, IAsyncDisposable
{
    private readonly ILeaderElectionProvider provider;
    private readonly LeaderElectionOptions options;
    private readonly ILogger<LeaderElectionService> logger;
    private readonly SemaphoreSlim stateLock = new(1, 1);
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Channel<LeadershipChangedEventArgs> broadcastChannel;
    private readonly List<Channel<LeadershipChangedEventArgs>> subscriberChannels = new();
    private readonly object subscriberLock = new();

    private LeadershipStatus currentStatus = LeadershipStatus.NoLeader();
    private Timer? heartbeatTimer;
    private Timer? electionTimer;
    private readonly Task broadcastTask;
    private volatile bool isDisposed;
    private volatile int activeCallbacks;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeaderElectionService"/> class.
    /// </summary>
    /// <param name="provider">The leader election provider.</param>
    /// <param name="options">The leader election options.</param>
    /// <param name="logger">The logger.</param>
    public LeaderElectionService(
        ILeaderElectionProvider provider,
        IOptions<LeaderElectionOptions> options,
        ILogger<LeaderElectionService> logger)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        this.options.Validate();

        ParticipantId = this.options.ParticipantId ?? Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];

        // Initialize broadcast channel for leadership change notifications
        // Using unbounded channel since leadership changes are rare and critical
        var channelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,  // Only UpdateStatusAsync writes to the channel
            SingleReader = true   // Single reader in broadcast loop
        };
        broadcastChannel = Channel.CreateUnbounded<LeadershipChangedEventArgs>(channelOptions);

        // Start the broadcast task and store reference for graceful shutdown
        broadcastTask = Task.Run(BroadcastEventsAsync);

        this.logger.LogInformation("Leader election service initialized for participant {ParticipantId} in group {ElectionGroup}",
            ParticipantId, this.options.ElectionGroup);
    }

    /// <summary>
    /// Gets the current leadership status of this instance.
    /// </summary>
    public LeadershipStatus CurrentStatus => currentStatus;

    /// <summary>
    /// Gets a value indicating whether this instance is currently the leader.
    /// </summary>
    public bool IsLeader => currentStatus.IsLeader;

    /// <summary>
    /// Gets the unique identifier of this participant in the election.
    /// </summary>
    public string ParticipantId { get; }

    /// <summary>
    /// Executes the background leader election process.
    /// </summary>
    /// <param name="stoppingToken">A token that is triggered when the service should stop.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.AutoStart)
        {
            return;
        }

        try
        {
            await StartElectionProcessAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected when service is stopping
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in leader election background service");
            throw;
        }
    }

    /// <summary>
    /// Starts the leader election process.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await stateLock.WaitAsync(cancellationToken);
        try
        {
            if (heartbeatTimer != null || electionTimer != null)
            {
                return; // Already started
            }

            logger.LogInformation("Starting leader election service for participant {ParticipantId}", ParticipantId);

            await StartElectionProcessAsync(cancellationToken);
        }
        finally
        {
            stateLock.Release();
        }
    }

    /// <summary>
    /// Stops the leader election process and releases leadership if currently held.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
        {
            return;
        }

        await stateLock.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation("Stopping leader election service for participant {ParticipantId}", ParticipantId);

            // Release leadership if we have it BEFORE completing the broadcast channel
            // This ensures the leadership lost event is broadcast to subscribers
            if (currentStatus.IsLeader)
            {
                try
                {
                    await provider.ReleaseLeadershipAsync(options.ElectionGroup, ParticipantId, cancellationToken);
                    await UpdateStatusAsync(LeadershipStatus.NoLeader(), cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to release leadership during shutdown");
                }
            }

            // Complete the broadcast channel writer to signal no more events will be written
            // This allows the broadcast task to finish processing any pending events
            // Use TryComplete to avoid exception if already completed
            broadcastChannel.Writer.TryComplete();

            // Wait for the broadcast task to finish processing all pending events
            await broadcastTask.WaitAsync(cancellationToken);

            // Now cancel the token source to stop any remaining background tasks
            await cancellationTokenSource.CancelAsync();

            // Stop timers
            if (heartbeatTimer != null)
            {
                await heartbeatTimer.DisposeAsync().ConfigureAwait(false);
                heartbeatTimer = null;
            }

            if (electionTimer != null)
            {
                await electionTimer.DisposeAsync().ConfigureAwait(false);
                electionTimer = null;
            }
        }
        finally
        {
            stateLock.Release();
        }
    }

    /// <summary>
    /// Attempts to acquire leadership immediately.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a value indicating
    /// whether leadership was successfully acquired.
    /// </returns>
    public async Task<bool> TryAcquireLeadershipAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await stateLock.WaitAsync(cancellationToken);
        try
        {
            return await TryAcquireLeadershipInternalAsync(cancellationToken);
        }
        finally
        {
            stateLock.Release();
        }
    }

    /// <summary>
    /// Releases leadership if currently held by this instance.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ReleaseLeadershipAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!currentStatus.IsLeader)
            {
                return;
            }

            await provider.ReleaseLeadershipAsync(options.ElectionGroup, ParticipantId, cancellationToken);
            await UpdateStatusAsync(LeadershipStatus.NoLeader(), cancellationToken);

            // Stop heartbeat timer
            if (heartbeatTimer != null)
            {
                await heartbeatTimer.DisposeAsync().ConfigureAwait(false);
                heartbeatTimer = null;
            }

            // Restart election timer
            StartElectionTimer();
        }
        finally
        {
            stateLock.Release();
        }
    }

    /// <summary>
    /// Gets information about the current leader.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains information about
    /// the current leader, or null if no leader is currently elected.
    /// </returns>
    public async Task<LeaderInfo?> GetCurrentLeaderAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await provider.GetCurrentLeaderAsync(options.ElectionGroup, cancellationToken);
    }

    /// <summary>
    /// Waits until this instance becomes the leader or the operation is cancelled.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task WaitForLeadershipAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (!currentStatus.IsLeader && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Waits until a leader is elected (not necessarily this instance) or the operation is cancelled.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains information about
    /// the elected leader.
    /// </returns>
    public async Task<LeaderInfo> WaitForLeaderAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (!cancellationToken.IsCancellationRequested)
        {
            LeaderInfo? leader = await GetCurrentLeaderAsync(cancellationToken);
            if (leader != null)
            {
                return leader;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new OperationCanceledException(cancellationToken);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="LeaderElectionService"/>.
    /// </summary>
    /// <remarks>
    /// The disposal is executed on a thread pool thread to avoid potential deadlocks in synchronization contexts.
    /// </remarks>
    public override void Dispose()
    {
        Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
        base.Dispose();
    }

    /// <summary>
    /// Asynchronously releases all resources used by the <see cref="LeaderElectionService"/>.
    /// This method properly waits for any active timer callbacks to complete before disposing resources.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        // Set disposal flag first to prevent new operations
        isDisposed = true;

        // Cancel the token source to signal shutdown
        await cancellationTokenSource.CancelAsync();

        // Dispose timers to prevent new callbacks from starting
        // Note: Timer.Dispose() does not wait for callbacks to complete
        if (heartbeatTimer != null)
        {
            await heartbeatTimer.DisposeAsync().ConfigureAwait(false);
            heartbeatTimer = null;
        }

        if (electionTimer != null)
        {
            await electionTimer.DisposeAsync().ConfigureAwait(false);
            electionTimer = null;
        }

        // Wait for any active callbacks to complete
        // Use a timeout to prevent indefinite waiting
        var waitStart = DateTime.UtcNow;
        var maxWait = TimeSpan.FromSeconds(10);

        while (activeCallbacks > 0 && DateTime.UtcNow - waitStart < maxWait)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        if (activeCallbacks > 0)
        {
            logger.LogWarning("Disposing LeaderElectionService with {ActiveCallbacks} active callbacks still running after {MaxWait}s timeout",
                activeCallbacks, maxWait.TotalSeconds);
        }

        // Now safe to dispose the lock and other resources
        stateLock.Dispose();
        cancellationTokenSource.Dispose();

        // Complete the broadcast channel to signal no more events will be written
        // Use TryComplete to avoid exception if already completed (e.g., by StopAsync)
        broadcastChannel.Writer.TryComplete();

        // Complete all subscriber channels
        lock (subscriberLock)
        {
            foreach (Channel<LeadershipChangedEventArgs> channel in subscriberChannels)
            {
                channel.Writer.TryComplete();
            }
            subscriberChannels.Clear();
        }

        try
        {
            provider.Dispose();
        }
        // Dispose should never throw, but we catch defensively to ensure cleanup completes
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error disposing provider");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    private async Task StartElectionProcessAsync(CancellationToken cancellationToken)
    {
        // Initial attempt to acquire leadership
        await TryAcquireLeadershipInternalAsync(cancellationToken);

        // Start the appropriate timer based on current status
        if (currentStatus.IsLeader)
        {
            StartHeartbeatTimer();
        }
        else
        {
            StartElectionTimer();
        }
    }

    private async Task<bool> TryAcquireLeadershipInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            bool acquired = await provider.TryAcquireLeadershipAsync(
                options.ElectionGroup,
                ParticipantId,
                options.Metadata,
                options.LockTimeout,
                cancellationToken);

            if (acquired)
            {
                var leaderInfo = new LeaderInfo(
                    ParticipantId,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    options.Metadata);

                await UpdateStatusAsync(LeadershipStatus.Leader(leaderInfo), cancellationToken);

                // Stop election timer and start heartbeat timer
                if (electionTimer != null)
                {
                    await electionTimer.DisposeAsync().ConfigureAwait(false);
                    electionTimer = null;
                }
                StartHeartbeatTimer();

                logger.LogInformation("Successfully acquired leadership for participant {ParticipantId}", ParticipantId);
                return true;
            }
            else
            {
                // Check current leader
                LeaderInfo? currentLeader = await provider.GetCurrentLeaderAsync(options.ElectionGroup, cancellationToken);
                await UpdateStatusAsync(LeadershipStatus.Follower(currentLeader), cancellationToken);

                logger.LogDebug("Failed to acquire leadership for participant {ParticipantId}. Current leader: {CurrentLeader}",
                    ParticipantId, currentLeader?.LeaderId ?? "None");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during leadership acquisition attempt for participant {ParticipantId}", ParticipantId);
            return false;
        }
    }

    private void StartHeartbeatTimer()
    {
        heartbeatTimer?.Dispose();
        heartbeatTimer = new Timer(
            HeartbeatTimerCallback,
            null,
            options.HeartbeatInterval,
            options.HeartbeatInterval);
    }

    private void StartElectionTimer()
    {
        electionTimer?.Dispose();
        electionTimer = new Timer(
            ElectionTimerCallback,
            null,
            options.ElectionInterval,
            options.ElectionInterval);
    }

    private async void HeartbeatTimerCallback(object? state)
    {
        // Early exit if disposed
        if (isDisposed)
            return;

        // Increment active callback counter
        Interlocked.Increment(ref activeCallbacks);
        try
        {
            await HeartbeatCallbackAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in heartbeat timer callback for participant {ParticipantId}", ParticipantId);
        }
        finally
        {
            // Decrement active callback counter
            Interlocked.Decrement(ref activeCallbacks);
        }
    }

    private async void ElectionTimerCallback(object? state)
    {
        // Early exit if disposed
        if (isDisposed)
            return;

        // Increment active callback counter
        Interlocked.Increment(ref activeCallbacks);
        try
        {
            await ElectionCallbackAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in election timer callback for participant {ParticipantId}", ParticipantId);
        }
        finally
        {
            // Decrement active callback counter
            Interlocked.Decrement(ref activeCallbacks);
        }
    }

    private async Task HeartbeatCallbackAsync()
    {
        if (isDisposed || cancellationTokenSource.Token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await stateLock.WaitAsync(cancellationTokenSource.Token);
            try
            {
                // Double-check disposal state after acquiring lock
                if (isDisposed)
                    return;

                if (!currentStatus.IsLeader)
                {
                    return;
                }

                bool heartbeatSuccessful = await provider.UpdateHeartbeatAsync(
                    options.ElectionGroup,
                    ParticipantId,
                    options.Metadata,
                    cancellationTokenSource.Token);

                if (!heartbeatSuccessful)
                {
                    // We lost leadership
                    logger.LogWarning("Lost leadership for participant {ParticipantId}", ParticipantId);

                    await UpdateStatusAsync(LeadershipStatus.NoLeader(), cancellationTokenSource.Token);

                    // Stop heartbeat timer and start election timer
                    if (heartbeatTimer != null)
                    {
                        await heartbeatTimer.DisposeAsync().ConfigureAwait(false);
                        heartbeatTimer = null;
                    }
                    StartElectionTimer();
                }
                else if (options.EnableDetailedLogging)
                {
                    logger.LogDebug("Heartbeat successful for leader {ParticipantId}", ParticipantId);
                }
            }
            finally
            {
                stateLock.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected during disposal - stateLock may be disposed
            // Silently ignore as this is a normal shutdown scenario
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation - ignore
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during heartbeat for participant {ParticipantId}", ParticipantId);
        }
    }

    private async Task ElectionCallbackAsync()
    {
        if (isDisposed || cancellationTokenSource.Token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await stateLock.WaitAsync(cancellationTokenSource.Token);
            try
            {
                // Double-check disposal state after acquiring lock
                if (isDisposed)
                    return;

                if (currentStatus.IsLeader)
                {
                    return;
                }

                await TryAcquireLeadershipInternalAsync(cancellationTokenSource.Token);
            }
            finally
            {
                stateLock.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected during disposal - stateLock may be disposed
            // Silently ignore as this is a normal shutdown scenario
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation - ignore
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during election attempt for participant {ParticipantId}", ParticipantId);
        }
    }

    private async Task UpdateStatusAsync(LeadershipStatus newStatus, CancellationToken cancellationToken)
    {
        LeadershipStatus previousStatus = currentStatus;
        currentStatus = newStatus;

        var eventArgs = new LeadershipChangedEventArgs(previousStatus, newStatus);

        // Write to broadcast channel for async enumerable consumers
        // Using TryWrite since we have an unbounded channel (should always succeed)
        if (!broadcastChannel.Writer.TryWrite(eventArgs))
        {
            logger.LogWarning("Failed to write leadership change event to broadcast channel. Channel may be completed.");
        }

        await Task.CompletedTask;
    }

    private async Task BroadcastEventsAsync()
    {
        // Read until the channel is completed (not until cancelled)
        // This ensures all pending events are broadcast during graceful shutdown
        // Note: ReadAllAsync() completes gracefully when the channel writer is completed,
        // so no try-catch is needed for normal operation
        await foreach (LeadershipChangedEventArgs eventArgs in broadcastChannel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            // Broadcast to all subscriber channels
            lock (subscriberLock)
            {
                foreach (Channel<LeadershipChangedEventArgs> channel in subscriberChannels)
                {
                    // Use TryWrite to avoid blocking if a subscriber is slow
                    // With unbounded channels, this should always succeed unless the channel is completed
                    channel.Writer.TryWrite(eventArgs);
                }
            }
        }
    }

    /// <summary>
    /// Gets an async enumerable stream of all leadership change events.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>
    /// An async enumerable that yields leadership change events as they occur.
    /// The enumeration will continue until the service is disposed or the cancellation token is triggered.
    /// </returns>
    public async IAsyncEnumerable<LeadershipChangedEventArgs> GetLeadershipChangesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Create a dedicated channel for this subscriber
        var subscriberChannel = Channel.CreateUnbounded<LeadershipChangedEventArgs>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        // Register the subscriber channel
        lock (subscriberLock)
        {
            subscriberChannels.Add(subscriberChannel);
        }

        try
        {
            // Read from the subscriber channel
            await foreach (LeadershipChangedEventArgs eventArgs in subscriberChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return eventArgs;
            }
        }
        finally
        {
            // Unregister and complete the subscriber channel
            lock (subscriberLock)
            {
                subscriberChannels.Remove(subscriberChannel);
            }
            subscriberChannel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Gets an async enumerable stream of filtered leadership change events.
    /// </summary>
    /// <param name="eventTypes">The types of events to include in the stream.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>
    /// An async enumerable that yields filtered leadership change events as they occur.
    /// The enumeration will continue until the service is disposed or the cancellation token is triggered.
    /// </returns>
    public async IAsyncEnumerable<LeadershipChangedEventArgs> GetLeadershipChangesAsync(
        LeadershipEventType eventTypes,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Create a dedicated channel for this subscriber
        var subscriberChannel = Channel.CreateUnbounded<LeadershipChangedEventArgs>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        // Register the subscriber channel
        lock (subscriberLock)
        {
            subscriberChannels.Add(subscriberChannel);
        }

        try
        {
            // Read from the subscriber channel and filter
            await foreach (LeadershipChangedEventArgs eventArgs in subscriberChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // Check if this event matches the requested event types
                bool shouldYield = ((eventTypes & LeadershipEventType.Acquired) != 0 && eventArgs.BecameLeader) ||
                                   ((eventTypes & LeadershipEventType.Lost) != 0 && eventArgs.LostLeadership) ||
                                   ((eventTypes & LeadershipEventType.Changed) != 0);

                if (shouldYield)
                {
                    yield return eventArgs;
                }
            }
        }
        finally
        {
            // Unregister and complete the subscriber channel
            lock (subscriberLock)
            {
                subscriberChannels.Remove(subscriberChannel);
            }
            subscriberChannel.Writer.TryComplete();
        }
    }
}

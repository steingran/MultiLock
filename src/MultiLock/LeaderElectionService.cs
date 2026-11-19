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
/// When disposing, the service waits for in-progress operations (timer callbacks, <see cref="StopAsync"/>,
/// <see cref="StartAsync"/>, <see cref="TryAcquireLeadershipAsync"/> and <see cref="ReleaseLeadershipAsync"/>)
/// to complete before releasing resources, preventing race conditions and ObjectDisposedException errors.
/// </para>
/// <para>
/// <strong>Disposal Best Practices:</strong>
/// <list type="bullet">
/// <item><description>Prefer using <c>await DisposeAsync()</c> when possible for proper async disposal.</description></item>
/// <item><description>The synchronous <c>Dispose()</c> method delegates to <c>DisposeAsync()</c> and blocks until completion.</description></item>
/// <item><description>Disposal is idempotent - calling Dispose/DisposeAsync multiple times is safe.</description></item>
/// <item><description>The service waits up to 10 seconds for in-progress operations to complete during disposal.</description></item>
/// <item><description><see cref="StopAsync"/> never throws <see cref="ObjectDisposedException"/>, even when it runs at the same time as disposal.</description></item>
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
    // 0 = not disposed, 1 = disposed. Set once with Interlocked so that only one caller runs the disposal body.
    private int disposeState;
    // Number of in-progress operations that use stateLock or other disposable resources.
    // DisposeAsync waits for this to reach zero before it disposes those resources.
    private int activeOperations;

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
        EnterOperationOrThrow();
        try
        {
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
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Stops the leader election process and releases leadership if currently held.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Register as an in-progress operation so that DisposeAsync waits for this method
        // before it disposes stateLock. If disposal has already started, the service is stopped.
        if (!TryEnterOperation())
        {
            return;
        }

        try
        {
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
                await DisposeTimerAsync(ref heartbeatTimer).ConfigureAwait(false);
                await DisposeTimerAsync(ref electionTimer).ConfigureAwait(false);
            }
            finally
            {
                stateLock.Release();
            }
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            // DisposeAsync stopped waiting for this method (after its timeout) and disposed
            // stateLock or the cancellation token source. The service is stopped either way,
            // and shutdown must not throw.
        }
        finally
        {
            ExitOperation();
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
        EnterOperationOrThrow();
        try
        {
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
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Releases leadership if currently held by this instance.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ReleaseLeadershipAsync(CancellationToken cancellationToken = default)
    {
        EnterOperationOrThrow();
        try
        {
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
                await DisposeTimerAsync(ref heartbeatTimer).ConfigureAwait(false);

                // Restart election timer
                StartElectionTimer();
            }
            finally
            {
                stateLock.Release();
            }
        }
        finally
        {
            ExitOperation();
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

        // If already leader, return immediately
        if (currentStatus.IsLeader)
            return;

        // Wait for leadership acquisition event using the async enumerable API
        await foreach (LeadershipChangedEventArgs e in GetLeadershipChangesAsync(cancellationToken))
        {
            if (e.BecameLeader)
                return;
        }
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

        // Check if a leader is already elected
        LeaderInfo? currentLeader = await GetCurrentLeaderAsync(cancellationToken);
        if (currentLeader != null)
            return currentLeader;

        // Wait for any leadership change event that results in a leader being elected
        await foreach (LeadershipChangedEventArgs _ in GetLeadershipChangesAsync(cancellationToken))
        {
            // After each event, check if a leader is now elected
            LeaderInfo? leader = await GetCurrentLeaderAsync(cancellationToken);
            if (leader != null)
                return leader;
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
    /// This method waits for in-progress operations (including timer callbacks and <see cref="StopAsync"/>)
    /// to complete before disposing resources.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        // Set disposal flag first to prevent new operations. Only the first caller continues.
        // Interlocked.Exchange is a full fence, so the reads of activeOperations below cannot move
        // before this write. Together with TryEnterOperation this guarantees that every operation
        // either sees the flag and backs out, or is counted and waited for.
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
            return;

        // Cancel the token source to signal shutdown
        await cancellationTokenSource.CancelAsync();

        // Dispose timers to prevent new callbacks from starting
        // Note: Timer.Dispose() does not wait for callbacks to complete
        await DisposeTimerAsync(ref heartbeatTimer).ConfigureAwait(false);
        await DisposeTimerAsync(ref electionTimer).ConfigureAwait(false);

        // Wait for in-progress operations (timer callbacks, StopAsync and the public methods
        // that take stateLock) to complete. Use a timeout to prevent indefinite waiting.
        var waitStart = DateTime.UtcNow;
        TimeSpan maxWait = DisposeWaitTimeout;

        while (Volatile.Read(ref activeOperations) > 0 && DateTime.UtcNow - waitStart < maxWait)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        int remainingOperations = Volatile.Read(ref activeOperations);
        if (remainingOperations > 0)
        {
            logger.LogWarning("Disposing LeaderElectionService with {ActiveOperations} active operations still running after {MaxWait}s timeout",
                remainingOperations, maxWait.TotalSeconds);
        }

        // An operation we waited for may have started a new timer (for example ReleaseLeadershipAsync
        // restarts the election timer). No new operation can enter now, so dispose any such timer.
        await DisposeTimerAsync(ref heartbeatTimer).ConfigureAwait(false);
        await DisposeTimerAsync(ref electionTimer).ConfigureAwait(false);

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

    /// <summary>
    /// How long <see cref="DisposeAsync"/> waits for in-progress operations before it disposes resources anyway.
    /// Internal so that tests can use a short timeout.
    /// </summary>
    internal TimeSpan DisposeWaitTimeout { get; set; } = TimeSpan.FromSeconds(10);

    private bool IsDisposed => Volatile.Read(ref disposeState) != 0;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    /// <summary>
    /// Registers an in-progress operation so that <see cref="DisposeAsync"/> waits for it
    /// before it disposes <see cref="stateLock"/> and other resources.
    /// </summary>
    /// <returns><c>true</c> if the operation may continue; <c>false</c> if the service is disposed.</returns>
    /// <remarks>
    /// The counter is incremented before the disposed flag is checked. Interlocked.Increment is a full fence,
    /// so either this method sees the flag set by <see cref="DisposeAsync"/>, or <see cref="DisposeAsync"/>
    /// sees the incremented counter and waits. Every successful call must be paired with <see cref="ExitOperation"/>.
    /// </remarks>
    private bool TryEnterOperation()
    {
        Interlocked.Increment(ref activeOperations);
        if (IsDisposed)
        {
            Interlocked.Decrement(ref activeOperations);
            return false;
        }

        return true;
    }

    private void EnterOperationOrThrow()
    {
        ObjectDisposedException.ThrowIf(!TryEnterOperation(), this);
    }

    private void ExitOperation()
    {
        Interlocked.Decrement(ref activeOperations);
    }

    /// <summary>
    /// Atomically takes the timer out of the field and disposes it, so that concurrent callers
    /// (for example <see cref="StopAsync"/> and <see cref="DisposeAsync"/>) never dispose a timer
    /// that another caller has already set to null.
    /// </summary>
    private static ValueTask DisposeTimerAsync(ref Timer? timer)
    {
        Timer? toDispose = Interlocked.Exchange(ref timer, null);
        return toDispose?.DisposeAsync() ?? ValueTask.CompletedTask;
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
                await DisposeTimerAsync(ref electionTimer).ConfigureAwait(false);
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
        // Register as an in-progress operation; exit early if disposed
        if (!TryEnterOperation())
            return;

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
            ExitOperation();
        }
    }

    private async void ElectionTimerCallback(object? state)
    {
        // Register as an in-progress operation; exit early if disposed
        if (!TryEnterOperation())
            return;

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
            ExitOperation();
        }
    }

    private async Task HeartbeatCallbackAsync()
    {
        if (IsDisposed || cancellationTokenSource.Token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await stateLock.WaitAsync(cancellationTokenSource.Token);
            try
            {
                // Double-check disposal state after acquiring lock
                if (IsDisposed)
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
                    await DisposeTimerAsync(ref heartbeatTimer).ConfigureAwait(false);
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
        if (IsDisposed || cancellationTokenSource.Token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await stateLock.WaitAsync(cancellationTokenSource.Token);
            try
            {
                // Double-check disposal state after acquiring lock
                if (IsDisposed)
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
        // Note: ReadAllAsync() completes gracefully when the channel writer is completed
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
    public IAsyncEnumerable<LeadershipChangedEventArgs> GetLeadershipChangesAsync(
        CancellationToken cancellationToken = default)
        => GetLeadershipChangesAsyncCore(eventTypes: null, cancellationToken, subscriberRegistered: null);

    /// <summary>
    /// Internal overload that signals when the subscriber is registered.
    /// Used by tests to avoid race conditions.
    /// </summary>
    internal IAsyncEnumerable<LeadershipChangedEventArgs> GetLeadershipChangesAsyncCore(
        CancellationToken cancellationToken,
        TaskCompletionSource? subscriberRegistered)
        => GetLeadershipChangesAsyncCore(eventTypes: null, cancellationToken, subscriberRegistered);

    /// <summary>
    /// Gets an async enumerable stream of filtered leadership change events.
    /// </summary>
    /// <param name="eventTypes">The types of events to include in the stream.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>
    /// An async enumerable that yields filtered leadership change events as they occur.
    /// The enumeration will continue until the service is disposed or the cancellation token is triggered.
    /// </returns>
    public IAsyncEnumerable<LeadershipChangedEventArgs> GetLeadershipChangesAsync(
        LeadershipEventType eventTypes,
        CancellationToken cancellationToken = default)
        => GetLeadershipChangesAsyncCore(eventTypes, cancellationToken, subscriberRegistered: null);

    /// <summary>
    /// Internal overload for filtered events that signals when the subscriber is registered.
    /// Used by tests to avoid race conditions.
    /// </summary>
    internal IAsyncEnumerable<LeadershipChangedEventArgs> GetLeadershipChangesAsyncCore(
        LeadershipEventType eventTypes,
        CancellationToken cancellationToken,
        TaskCompletionSource? subscriberRegistered)
        => GetLeadershipChangesAsyncCore((LeadershipEventType?)eventTypes, cancellationToken, subscriberRegistered);

    /// <summary>
    /// Core implementation that handles both filtered and unfiltered event streams.
    /// </summary>
    private async IAsyncEnumerable<LeadershipChangedEventArgs> GetLeadershipChangesAsyncCore(
        LeadershipEventType? eventTypes,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        TaskCompletionSource? subscriberRegistered)
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

        // Signal that the subscriber is now registered
        subscriberRegistered?.TrySetResult();

        try
        {
            // Read from the subscriber channel
            await foreach (LeadershipChangedEventArgs eventArgs in subscriberChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // If no filter specified, yield all events
                if (eventTypes is null)
                {
                    yield return eventArgs;
                    continue;
                }

                // Check if this event matches the requested event types
                bool shouldYield = ((eventTypes.Value & LeadershipEventType.Acquired) != 0 && eventArgs.BecameLeader) ||
                                   ((eventTypes.Value & LeadershipEventType.Lost) != 0 && eventArgs.LostLeadership) ||
                                   ((eventTypes.Value & LeadershipEventType.Changed) != 0);

                if (shouldYield)
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
}

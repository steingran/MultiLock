using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MultiLock;

/// <summary>
/// Implementation of the distributed semaphore service that manages slot acquisition and lifecycle.
/// </summary>
public sealed class SemaphoreService : BackgroundService, ISemaphoreService
{
    private readonly ISemaphoreProvider provider;
    private readonly SemaphoreOptions options;
    private readonly ILogger<SemaphoreService> logger;
    private readonly SemaphoreSlim stateLock = new(1, 1);
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Channel<SemaphoreChangedEventArgs> broadcastChannel;
    private readonly List<Channel<SemaphoreChangedEventArgs>> subscriberChannels = [];
    private readonly object subscriberLock = new();

    private SemaphoreStatus currentStatus;
    private Timer? heartbeatTimer;
    private Timer? acquisitionTimer;
    private readonly Task broadcastTask;
    private volatile bool isDisposed;
    private volatile int activeCallbacks;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreService"/> class.
    /// </summary>
    /// <param name="provider">The semaphore provider.</param>
    /// <param name="options">The semaphore options.</param>
    /// <param name="logger">The logger.</param>
    public SemaphoreService(
        ISemaphoreProvider provider,
        IOptions<SemaphoreOptions> options,
        ILogger<SemaphoreService> logger)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        this.options.Validate();

        HolderId = this.options.HolderId ?? Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];
        currentStatus = SemaphoreStatus.Unknown(this.options.MaxCount);

        var channelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        };
        broadcastChannel = Channel.CreateUnbounded<SemaphoreChangedEventArgs>(channelOptions);
        broadcastTask = Task.Run(BroadcastEventsAsync);

        this.logger.LogInformation(
            "Semaphore service initialized for holder {HolderId} on semaphore {SemaphoreName} (max: {MaxCount})",
            HolderId, this.options.SemaphoreName, this.options.MaxCount);
    }

    /// <inheritdoc />
    public SemaphoreStatus CurrentStatus => currentStatus;

    /// <inheritdoc />
    public bool IsHolding => currentStatus.IsHolding;

    /// <inheritdoc />
    public string HolderId { get; }

    /// <inheritdoc />
    public string SemaphoreName => options.SemaphoreName;

    /// <inheritdoc />
    public int MaxCount => options.MaxCount;

    // BackgroundService.ExecuteAsync is abstract and must be implemented, but acquisition is driven
    // entirely through StartAsync so this method is never reached via the normal hosted-service path.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // When AutoStart is false the caller drives acquisition manually via TryAcquireAsync().
        if (!options.AutoStart)
            return;

        await stateLock.WaitAsync(cancellationToken);
        try
        {
            if (heartbeatTimer != null || acquisitionTimer != null)
                return;

            logger.LogInformation("Starting semaphore service for holder {HolderId}", HolderId);
            await StartAcquisitionProcessAsync(cancellationToken);
        }
        finally
        {
            stateLock.Release();
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
            return;

        await stateLock.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation("Stopping semaphore service for holder {HolderId}", HolderId);

            if (currentStatus.IsHolding)
            {
                try
                {
                    await provider.ReleaseAsync(options.SemaphoreName, HolderId, cancellationToken);
                    UpdateStatus(SemaphoreStatus.Waiting(Math.Max(0, currentStatus.CurrentCount - 1), options.MaxCount, null));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to release slot during shutdown");
                }
            }

            broadcastChannel.Writer.TryComplete();
            await broadcastTask.WaitAsync(cancellationToken);
            await cancellationTokenSource.CancelAsync();

            await DisposeTimersAsync();
        }
        finally
        {
            stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await stateLock.WaitAsync(cancellationToken);
        try
        {
            return await TryAcquireInternalAsync(cancellationToken);
        }
        finally
        {
            stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!currentStatus.IsHolding)
                return;

            await ExecuteWithRetryAsync(
                async ct => await provider.ReleaseAsync(options.SemaphoreName, HolderId, ct),
                "release slot",
                cancellationToken);

            UpdateStatus(SemaphoreStatus.Waiting(Math.Max(0, currentStatus.CurrentCount - 1), options.MaxCount, null));

            await DisposeHeartbeatTimerAsync();
            StartAcquisitionTimer();
        }
        finally
        {
            stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task WaitForSlotAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Subscribe BEFORE checking IsHolding to close a TOCTOU window: IsHolding could
        // transition true between our check and the subscription registration, causing us
        // to miss the broadcast and wait indefinitely. GetStatusChangesAsyncCoreImpl
        // registers the subscriber channel synchronously (inside a lock, before any yield),
        // so by the time MoveNextAsync() suspends the subscription is already in place.
        //
        // A localCts is used so that when IsHolding is already true we can cancel the
        // pending MoveNextAsync before the await-using disposes the enumerator. Without
        // cancellation, DisposeAsync throws NotSupportedException because it cannot
        // interrupt a ReadAllAsync awaiting an uncancellable token.
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var subscriptionRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IAsyncEnumerator<SemaphoreChangedEventArgs> enumerator =
            GetStatusChangesAsyncCore(localCts.Token, subscriptionRegistered)
                .GetAsyncEnumerator(CancellationToken.None);

        await using (enumerator)
        {
            // Drive the state machine synchronously to the first suspension point, which
            // registers the subscription and sets subscriptionRegistered before any await.
            ValueTask<bool> pending = enumerator.MoveNextAsync();

            // subscriptionRegistered is already complete — check IsHolding with the guarantee
            // that any concurrent transition will be delivered to our subscriber channel.
            if (currentStatus.IsHolding)
            {
                // Cancel the pending MoveNextAsync so DisposeAsync can interrupt ReadAllAsync.
                localCts.Cancel();
                try { await pending; } catch (OperationCanceledException) { }
                return;
            }

            while (await pending)
            {
                if (enumerator.Current.AcquiredSlot)
                    return;
                pending = enumerator.MoveNextAsync();
            }
        }

        // The enumerator completes normally only when the broadcast channel is closed (disposal).
        // A cancelled token throws OperationCanceledException and never reaches here.
        throw new ObjectDisposedException(nameof(SemaphoreService));
    }

    /// <inheritdoc />
    public async Task<bool> WaitForSlotAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using var timeoutCts = new CancellationTokenSource(timeout);
        // localCts combines the caller's token, the timeout, and the "already holding" early-exit.
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var subscriptionRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe BEFORE checking IsHolding — see WaitForSlotAsync overload for rationale.
        IAsyncEnumerator<SemaphoreChangedEventArgs> enumerator =
            GetStatusChangesAsyncCore(localCts.Token, subscriptionRegistered)
                .GetAsyncEnumerator(CancellationToken.None);

        await using (enumerator)
        {
            ValueTask<bool> pending = enumerator.MoveNextAsync();

            if (currentStatus.IsHolding)
            {
                localCts.Cancel();
                try { await pending; } catch (OperationCanceledException) { }
                return true;
            }

            try
            {
                while (await pending)
                {
                    if (enumerator.Current.AcquiredSlot)
                        return true;
                    pending = enumerator.MoveNextAsync();
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        // The enumerator completes normally only when the broadcast channel is closed (disposal).
        throw new ObjectDisposedException(nameof(SemaphoreService));
    }

    /// <inheritdoc />
    public async Task<SemaphoreInfo?> GetSemaphoreInfoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        int currentCount = await provider.GetCurrentCountAsync(options.SemaphoreName, options.HeartbeatTimeout, cancellationToken);
        IReadOnlyList<SemaphoreHolder> holders = await provider.GetHoldersAsync(options.SemaphoreName, cancellationToken);

        return new SemaphoreInfo(options.SemaphoreName, options.MaxCount, currentCount, holders);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SemaphoreChangedEventArgs> GetStatusChangesAsync(CancellationToken cancellationToken = default)
        => GetStatusChangesAsyncCore(cancellationToken, subscriberRegistered: null);

    internal IAsyncEnumerable<SemaphoreChangedEventArgs> GetStatusChangesAsyncCore(
        CancellationToken cancellationToken,
        TaskCompletionSource? subscriberRegistered)
    {
        return GetStatusChangesAsyncCoreImpl(cancellationToken, subscriberRegistered);
    }

    private async IAsyncEnumerable<SemaphoreChangedEventArgs> GetStatusChangesAsyncCoreImpl(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        TaskCompletionSource? subscriberRegistered)
    {
        ThrowIfDisposed();

        var subscriberChannel = Channel.CreateUnbounded<SemaphoreChangedEventArgs>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        lock (subscriberLock)
        {
            subscriberChannels.Add(subscriberChannel);
        }

        subscriberRegistered?.TrySetResult();

        try
        {
            await foreach (SemaphoreChangedEventArgs eventArgs in subscriberChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return eventArgs;
            }
        }
        finally
        {
            lock (subscriberLock)
            {
                subscriberChannels.Remove(subscriberChannel);
            }
            subscriberChannel.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Prefer <see cref="DisposeAsync"/> in async contexts. This synchronous path blocks the
    /// calling thread and should not be used when a synchronization context is active.
    /// </remarks>
    public override void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        await cancellationTokenSource.CancelAsync();
        await DisposeTimersAsync();

        var waitStart = DateTime.UtcNow;
        var maxWait = TimeSpan.FromSeconds(10);

        while (activeCallbacks > 0 && DateTime.UtcNow - waitStart < maxWait)
            await Task.Delay(10).ConfigureAwait(false);

        if (activeCallbacks > 0)
        {
            logger.LogWarning(
                "Disposing SemaphoreService with {ActiveCallbacks} active callbacks still running after {MaxWait}s timeout",
                activeCallbacks, maxWait.TotalSeconds);
        }

        stateLock.Dispose();
        cancellationTokenSource.Dispose();
        broadcastChannel.Writer.TryComplete();

        try
        {
            await broadcastTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the service is cancelled during disposal.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error draining broadcast task during disposal");
        }

        lock (subscriberLock)
        {
            foreach (Channel<SemaphoreChangedEventArgs> channel in subscriberChannels)
                channel.Writer.TryComplete();
            subscriberChannels.Clear();
        }

        try
        {
            provider.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error disposing provider");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    private async Task StartAcquisitionProcessAsync(CancellationToken cancellationToken)
    {
        await TryAcquireInternalAsync(cancellationToken);

        if (currentStatus.IsHolding)
            StartHeartbeatTimer();
        else
            StartAcquisitionTimer();
    }

    private async Task<bool> TryAcquireInternalAsync(CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(
            async ct =>
            {
                bool acquired = await provider.TryAcquireAsync(
                    options.SemaphoreName,
                    HolderId,
                    options.MaxCount,
                    options.Metadata,
                    options.HeartbeatTimeout,
                    ct);

                int currentCount = await provider.GetCurrentCountAsync(options.SemaphoreName, options.HeartbeatTimeout, ct);

                if (acquired)
                {
                    UpdateStatus(SemaphoreStatus.Holding(currentCount, options.MaxCount));

                    await DisposeAcquisitionTimerAsync();
                    StartHeartbeatTimer();

                    logger.LogInformation("Successfully acquired slot for holder {HolderId}", HolderId);
                    return true;
                }
                else
                {
                    UpdateStatus(SemaphoreStatus.Waiting(currentCount, options.MaxCount, DateTimeOffset.UtcNow.Add(options.AcquisitionInterval)));

                    logger.LogDebug("Failed to acquire slot for holder {HolderId}. Semaphore is full.", HolderId);
                    return false;
                }
            },
            "acquire slot",
            cancellationToken);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        Exception? lastException = null;

        while (attempt <= options.MaxRetryAttempts)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;

                if (attempt > options.MaxRetryAttempts)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to {OperationName} for holder {HolderId} after {Attempts} attempts",
                        operationName, HolderId, attempt);
                    break;
                }

                TimeSpan delay = CalculateBackoffDelay(attempt);

                if (options.EnableDetailedLogging)
                    logger.LogDebug(
                        ex,
                        "Attempt {Attempt} to {OperationName} for holder {HolderId} failed. Retrying in {Delay}ms",
                        attempt, operationName, HolderId, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        // All attempts exhausted — re-throw so callers can distinguish a provider error
        // from a legitimate "not acquired / not holding" false return.
        throw lastException!;
    }

    private async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        await ExecuteWithRetryAsync(
            async ct =>
            {
                await operation(ct);
                return true;
            },
            operationName,
            cancellationToken);
    }

    private TimeSpan CalculateBackoffDelay(int attempt)
    {
        // Exponential backoff with jitter: baseDelay * 2^(attempt-1) + random jitter
        double exponentialMs = options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        double jitterMs = Random.Shared.NextDouble() * options.RetryBaseDelay.TotalMilliseconds;
        double totalMs = exponentialMs + jitterMs;

        // Cap at max delay
        double cappedMs = Math.Min(totalMs, options.RetryMaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(cappedMs);
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

    private void StartAcquisitionTimer()
    {
        acquisitionTimer?.Dispose();
        acquisitionTimer = new Timer(
            AcquisitionTimerCallback,
            null,
            options.AcquisitionInterval,
            options.AcquisitionInterval);
    }

    private async void HeartbeatTimerCallback(object? state)
    {
        if (isDisposed)
            return;

        Interlocked.Increment(ref activeCallbacks);
        try
        {
            await HeartbeatCallbackAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in heartbeat timer callback for holder {HolderId}", HolderId);
        }
        finally
        {
            Interlocked.Decrement(ref activeCallbacks);
        }
    }

    private async void AcquisitionTimerCallback(object? state)
    {
        if (isDisposed)
            return;

        Interlocked.Increment(ref activeCallbacks);
        try
        {
            await AcquisitionCallbackAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in acquisition timer callback for holder {HolderId}", HolderId);
        }
        finally
        {
            Interlocked.Decrement(ref activeCallbacks);
        }
    }

    private async Task HeartbeatCallbackAsync()
    {
        if (isDisposed || cancellationTokenSource.Token.IsCancellationRequested)
            return;

        try
        {
            await stateLock.WaitAsync(cancellationTokenSource.Token);
            try
            {
                if (isDisposed)
                    return;

                if (!currentStatus.IsHolding)
                    return;

                bool heartbeatSuccessful = await ExecuteWithRetryAsync(
                    async ct => await provider.UpdateHeartbeatAsync(
                        options.SemaphoreName,
                        HolderId,
                        options.Metadata,
                        ct),
                    "update heartbeat",
                    cancellationTokenSource.Token);

                if (!heartbeatSuccessful)
                {
                    logger.LogWarning("Lost slot for holder {HolderId}", HolderId);

                    int currentCount = await provider.GetCurrentCountAsync(options.SemaphoreName, options.HeartbeatTimeout, cancellationTokenSource.Token);
                    UpdateStatus(SemaphoreStatus.Waiting(currentCount, options.MaxCount, DateTimeOffset.UtcNow.Add(options.AcquisitionInterval)));

                    await DisposeHeartbeatTimerAsync();
                    StartAcquisitionTimer();
                }
                else if (options.EnableDetailedLogging)
                {
                    logger.LogDebug("Heartbeat successful for holder {HolderId}", HolderId);
                }
            }
            finally
            {
                stateLock.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected during disposal
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during heartbeat for holder {HolderId}", HolderId);
        }
    }

    private async Task AcquisitionCallbackAsync()
    {
        if (isDisposed || cancellationTokenSource.Token.IsCancellationRequested)
            return;

        try
        {
            await stateLock.WaitAsync(cancellationTokenSource.Token);
            try
            {
                if (isDisposed)
                    return;

                if (currentStatus.IsHolding)
                    return;

                await TryAcquireInternalAsync(cancellationTokenSource.Token);
            }
            finally
            {
                stateLock.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected during disposal
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during acquisition attempt for holder {HolderId}", HolderId);
        }
    }

    private void UpdateStatus(SemaphoreStatus newStatus)
    {
        SemaphoreStatus previousStatus = currentStatus;
        currentStatus = newStatus;

        var eventArgs = new SemaphoreChangedEventArgs(previousStatus, newStatus);

        if (!broadcastChannel.Writer.TryWrite(eventArgs))
            logger.LogWarning("Failed to write semaphore change event to broadcast channel. Channel may be completed.");
    }

    private async Task BroadcastEventsAsync()
    {
        await foreach (SemaphoreChangedEventArgs eventArgs in broadcastChannel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (subscriberLock)
            {
                foreach (Channel<SemaphoreChangedEventArgs> channel in subscriberChannels)
                    channel.Writer.TryWrite(eventArgs);
            }
        }
    }

    private async Task DisposeTimersAsync()
    {
        await DisposeHeartbeatTimerAsync();
        await DisposeAcquisitionTimerAsync();
    }

    private async Task DisposeHeartbeatTimerAsync()
    {
        if (heartbeatTimer != null)
        {
            await heartbeatTimer.DisposeAsync().ConfigureAwait(false);
            heartbeatTimer = null;
        }
    }

    private async Task DisposeAcquisitionTimerAsync()
    {
        if (acquisitionTimer != null)
        {
            await acquisitionTimer.DisposeAsync().ConfigureAwait(false);
            acquisitionTimer = null;
        }
    }
}

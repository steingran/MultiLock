namespace MultiLock;

/// <summary>
/// Defines the contract for a high-level distributed semaphore service.
/// Provides automatic heartbeat renewal, retry logic, and lifecycle management.
/// </summary>
public interface ISemaphoreService : IAsyncDisposable
{
    /// <summary>
    /// Gets the current status of this participant in the semaphore.
    /// </summary>
    SemaphoreStatus CurrentStatus { get; }

    /// <summary>
    /// Gets a value indicating whether this participant currently holds a slot.
    /// </summary>
    bool IsHolding { get; }

    /// <summary>
    /// Gets the unique identifier of this participant.
    /// </summary>
    string HolderId { get; }

    /// <summary>
    /// Gets the name of the semaphore.
    /// </summary>
    string SemaphoreName { get; }

    /// <summary>
    /// Gets the maximum number of concurrent holders allowed.
    /// </summary>
    int MaxCount { get; }

    /// <summary>
    /// Starts the semaphore service.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the semaphore service and releases any held slot.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire a slot in the semaphore.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if a slot was acquired; otherwise, false.</returns>
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the currently held slot, if any.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task ReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until a slot is acquired or the operation is cancelled.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for a slot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if a slot was acquired; false if the timeout expired.</returns>
    Task<bool> WaitForSlotAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until a slot is acquired or the operation is cancelled.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when a slot is acquired.</returns>
    Task WaitForSlotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about the current state of the semaphore.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Information about the semaphore, or null if unavailable.</returns>
    Task<SemaphoreInfo?> GetSemaphoreInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an async enumerable of semaphore status changes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable of status change events.</returns>
    IAsyncEnumerable<SemaphoreChangedEventArgs> GetStatusChangesAsync(CancellationToken cancellationToken = default);
}


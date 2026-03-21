namespace MultiLock;

/// <summary>
/// Defines the contract for distributed semaphore providers.
/// A distributed semaphore allows a limited number of concurrent holders across multiple processes or machines.
/// </summary>
public interface ISemaphoreProvider : IDisposable
{
    /// <summary>
    /// Attempts to acquire a slot in the semaphore.
    /// </summary>
    /// <param name="semaphoreName">The name of the semaphore.</param>
    /// <param name="holderId">The unique identifier of the holder attempting to acquire.</param>
    /// <param name="maxCount">The maximum number of concurrent holders allowed.</param>
    /// <param name="metadata">Optional metadata associated with this holder.</param>
    /// <param name="slotTimeout">The duration after which the slot expires if not renewed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the slot was acquired; otherwise, false.</returns>
    Task<bool> TryAcquireAsync(
        string semaphoreName,
        string holderId,
        int maxCount,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan slotTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a slot held by the specified holder.
    /// </summary>
    /// <param name="semaphoreName">The name of the semaphore.</param>
    /// <param name="holderId">The unique identifier of the holder releasing the slot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task ReleaseAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the heartbeat for a held slot to prevent expiration.
    /// </summary>
    /// <param name="semaphoreName">The name of the semaphore.</param>
    /// <param name="holderId">The unique identifier of the holder.</param>
    /// <param name="metadata">Updated metadata for the holder.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the heartbeat was updated; false if the holder does not hold a slot.</returns>
    Task<bool> UpdateHeartbeatAsync(
        string semaphoreName,
        string holderId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current number of active (non-expired) holders for the semaphore.
    /// </summary>
    /// <param name="semaphoreName">The name of the semaphore.</param>
    /// <param name="slotTimeout">The duration after which a holder is considered expired if not renewed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The current number of active holders.</returns>
    Task<int> GetCurrentCountAsync(
        string semaphoreName,
        TimeSpan slotTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about all current holders of the semaphore.
    /// </summary>
    /// <param name="semaphoreName">The name of the semaphore.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of holder information, or an empty collection if no holders exist.</returns>
    Task<IReadOnlyList<SemaphoreHolder>> GetHoldersAsync(
        string semaphoreName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the specified holder currently holds a slot in the semaphore.
    /// </summary>
    /// <param name="semaphoreName">The name of the semaphore.</param>
    /// <param name="holderId">The unique identifier of the holder to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the holder currently holds a slot; otherwise, false.</returns>
    Task<bool> IsHoldingAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the provider.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the provider is healthy; otherwise, false.</returns>
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}


namespace MultiLock;

/// <summary>
/// Defines the contract for a leader election service that manages the election process and leadership lifecycle.
/// </summary>
public interface ILeaderElectionService : IDisposable
{
    /// <summary>
    /// Gets the current leadership status of this instance.
    /// </summary>
    LeadershipStatus CurrentStatus { get; }

    /// <summary>
    /// Gets a value indicating whether this instance is currently the leader.
    /// </summary>
    bool IsLeader { get; }

    /// <summary>
    /// Gets the unique identifier of this participant in the election.
    /// </summary>
    string ParticipantId { get; }

    /// <summary>
    /// Starts the leader election process.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the leader election process and releases leadership if currently held.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire leadership immediately.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a value indicating
    /// whether leadership was successfully acquired.
    /// </returns>
    Task<bool> TryAcquireLeadershipAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases leadership if currently held by this instance.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ReleaseLeadershipAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about the current leader.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains information about
    /// the current leader, or null if no leader is currently elected.
    /// </returns>
    Task<LeaderInfo?> GetCurrentLeaderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until this instance becomes the leader or the operation is cancelled.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task WaitForLeadershipAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until a leader is elected (not necessarily this instance) or the operation is cancelled.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains information about
    /// the elected leader.
    /// </returns>
    Task<LeaderInfo> WaitForLeaderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an async enumerable stream of all leadership change events.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>
    /// An async enumerable that yields leadership change events as they occur.
    /// The enumeration will continue until the service is disposed or the cancellation token is triggered.
    /// </returns>
    IAsyncEnumerable<LeadershipChangedEventArgs> GetLeadershipChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an async enumerable stream of filtered leadership change events.
    /// </summary>
    /// <param name="eventTypes">The types of events to include in the stream.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>
    /// An async enumerable that yields filtered leadership change events as they occur.
    /// The enumeration will continue until the service is disposed or the cancellation token is triggered.
    /// </returns>
    IAsyncEnumerable<LeadershipChangedEventArgs> GetLeadershipChangesAsync(
        LeadershipEventType eventTypes,
        CancellationToken cancellationToken = default);
}

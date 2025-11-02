namespace MultiLock;

/// <summary>
/// Defines the contract for a leader election provider that handles the underlying storage and coordination mechanism.
/// </summary>
public interface ILeaderElectionProvider : IDisposable
{
    /// <summary>
    /// Attempts to acquire leadership for the specified participant.
    /// </summary>
    /// <param name="electionGroup">The name of the election group.</param>
    /// <param name="participantId">The unique identifier of the participant attempting to become leader.</param>
    /// <param name="metadata">Optional metadata to associate with the leadership.</param>
    /// <param name="lockTimeout">The maximum time to wait for lock acquisition.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a value indicating
    /// whether leadership was successfully acquired.
    /// </returns>
    Task<bool> TryAcquireLeadershipAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases leadership for the specified participant.
    /// </summary>
    /// <param name="electionGroup">The name of the election group.</param>
    /// <param name="participantId">The unique identifier of the participant releasing leadership.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ReleaseLeadershipAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the heartbeat for the current leader.
    /// </summary>
    /// <param name="electionGroup">The name of the election group.</param>
    /// <param name="participantId">The unique identifier of the leader.</param>
    /// <param name="metadata">Optional metadata to update with the heartbeat.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a value indicating
    /// whether the heartbeat was successfully updated (i.e., the participant is still the leader).
    /// </returns>
    Task<bool> UpdateHeartbeatAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about the current leader for the specified election group.
    /// </summary>
    /// <param name="electionGroup">The name of the election group.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains information about
    /// the current leader, or null if no leader is currently elected or the leader has expired.
    /// </returns>
    Task<LeaderInfo?> GetCurrentLeaderAsync(
        string electionGroup,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the specified participant is currently the leader.
    /// </summary>
    /// <param name="electionGroup">The name of the election group.</param>
    /// <param name="participantId">The unique identifier of the participant to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a value indicating
    /// whether the specified participant is currently the leader.
    /// </returns>
    Task<bool> IsLeaderAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs health check on the provider to ensure it's operational.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a value indicating
    /// whether the provider is healthy and operational.
    /// </returns>
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}

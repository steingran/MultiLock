namespace MultiLock;

/// <summary>
/// Defines the contract for a leader election provider that handles the underlying storage and coordination mechanism.
/// </summary>
public interface ILeaderElectionProvider : IDisposable
{
    /// <summary>
    /// Attempts to acquire leadership for the specified participant.
    /// </summary>
    /// <param name="electionGroup">The name of the election group. Must be non-null, non-empty, non-whitespace,
    /// contain only alphanumeric characters, underscores, hyphens, and periods, and not exceed 255 characters.</param>
    /// <param name="participantId">The unique identifier of the participant attempting to become leader.
    /// Must be non-null, non-empty, non-whitespace, contain only alphanumeric characters, underscores, hyphens,
    /// and periods, and not exceed 255 characters.</param>
    /// <param name="metadata">Optional metadata to associate with the leadership. Must not be null.
    /// Cannot contain more than 100 entries. Keys must be non-null, non-empty, non-whitespace, and not exceed 255 characters.
    /// Values must not exceed 4000 characters.</param>
    /// <param name="lockTimeout">The maximum time to wait for lock acquisition. Must be positive and not exceed 1 day.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a value indicating
    /// whether leadership was successfully acquired.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="electionGroup"/>, <paramref name="participantId"/>,
    /// <paramref name="metadata"/>, or <paramref name="lockTimeout"/> do not meet validation requirements.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata"/> is null.</exception>
    Task<bool> TryAcquireLeadershipAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases leadership for the specified participant.
    /// </summary>
    /// <param name="electionGroup">The name of the election group. Must be non-null, non-empty, non-whitespace,
    /// contain only alphanumeric characters, underscores, hyphens, and periods, and not exceed 255 characters.</param>
    /// <param name="participantId">The unique identifier of the participant releasing leadership.
    /// Must be non-null, non-empty, non-whitespace, contain only alphanumeric characters, underscores, hyphens,
    /// and periods, and not exceed 255 characters.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="electionGroup"/> or
    /// <paramref name="participantId"/> do not meet validation requirements.</exception>
    Task ReleaseLeadershipAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the heartbeat for the current leader.
    /// </summary>
    /// <param name="electionGroup">The name of the election group. Must be non-null, non-empty, non-whitespace,
    /// contain only alphanumeric characters, underscores, hyphens, and periods, and not exceed 255 characters.</param>
    /// <param name="participantId">The unique identifier of the leader.
    /// Must be non-null, non-empty, non-whitespace, contain only alphanumeric characters, underscores, hyphens,
    /// and periods, and not exceed 255 characters.</param>
    /// <param name="metadata">Optional metadata to update with the heartbeat. Must not be null.
    /// Cannot contain more than 100 entries. Keys must be non-null, non-empty, non-whitespace, and not exceed 255 characters.
    /// Values must not exceed 4000 characters.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a value indicating
    /// whether the heartbeat was successfully updated (i.e., the participant is still the leader).
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="electionGroup"/>, <paramref name="participantId"/>,
    /// or <paramref name="metadata"/> do not meet validation requirements.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata"/> is null.</exception>
    Task<bool> UpdateHeartbeatAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about the current leader for the specified election group.
    /// </summary>
    /// <param name="electionGroup">The name of the election group. Must be non-null, non-empty, non-whitespace,
    /// contain only alphanumeric characters, underscores, hyphens, and periods, and not exceed 255 characters.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains information about
    /// the current leader, or null if no leader is currently elected or the leader has expired.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="electionGroup"/> does not meet validation requirements.</exception>
    Task<LeaderInfo?> GetCurrentLeaderAsync(
        string electionGroup,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the specified participant is currently the leader.
    /// </summary>
    /// <param name="electionGroup">The name of the election group. Must be non-null, non-empty, non-whitespace,
    /// contain only alphanumeric characters, underscores, hyphens, and periods, and not exceed 255 characters.</param>
    /// <param name="participantId">The unique identifier of the participant to check.
    /// Must be non-null, non-empty, non-whitespace, contain only alphanumeric characters, underscores, hyphens,
    /// and periods, and not exceed 255 characters.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a value indicating
    /// whether the specified participant is currently the leader.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="electionGroup"/> or
    /// <paramref name="participantId"/> do not meet validation requirements.</exception>
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

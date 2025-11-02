namespace MultiLock;

/// <summary>
/// Represents information about the current leader in a leader election.
/// </summary>
public sealed class LeaderInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LeaderInfo"/> class.
    /// </summary>
    /// <param name="leaderId">The unique identifier of the leader.</param>
    /// <param name="leadershipAcquiredAt">The timestamp when leadership was acquired.</param>
    /// <param name="lastHeartbeat">The timestamp of the last heartbeat from the leader.</param>
    /// <param name="metadata">Optional metadata associated with the leader.</param>
    public LeaderInfo(
        string leaderId,
        DateTimeOffset leadershipAcquiredAt,
        DateTimeOffset lastHeartbeat,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        LeaderId = leaderId ?? throw new ArgumentNullException(nameof(leaderId));
        LeadershipAcquiredAt = leadershipAcquiredAt;
        LastHeartbeat = lastHeartbeat;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Gets the unique identifier of the leader.
    /// </summary>
    public string LeaderId { get; }

    /// <summary>
    /// Gets the timestamp when leadership was acquired.
    /// </summary>
    public DateTimeOffset LeadershipAcquiredAt { get; }

    /// <summary>
    /// Gets the timestamp of the last heartbeat from the leader.
    /// </summary>
    public DateTimeOffset LastHeartbeat { get; }

    /// <summary>
    /// Gets the metadata associated with the leader.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Determines whether the leader is considered healthy based on the heartbeat timeout.
    /// </summary>
    /// <param name="heartbeatTimeout">The maximum allowed time since the last heartbeat.</param>
    /// <returns>True if the leader is healthy; otherwise, false.</returns>
    public bool IsHealthy(TimeSpan heartbeatTimeout)
    {
        return DateTimeOffset.UtcNow - LastHeartbeat <= heartbeatTimeout;
    }
}
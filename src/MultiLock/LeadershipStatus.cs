namespace MultiLock;

/// <summary>
/// Represents the current leadership status of a participant in leader election.
/// </summary>
public sealed class LeadershipStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LeadershipStatus"/> class.
    /// </summary>
    /// <param name="isLeader">Indicates whether this instance is the current leader.</param>
    /// <param name="currentLeader">Information about the current leader, if known.</param>
    /// <param name="lastElectionAttempt">The timestamp of the last election attempt.</param>
    /// <param name="nextElectionAttempt">The timestamp of the next scheduled election attempt.</param>
    public LeadershipStatus(
        bool isLeader,
        LeaderInfo? currentLeader = null,
        DateTimeOffset? lastElectionAttempt = null,
        DateTimeOffset? nextElectionAttempt = null)
    {
        IsLeader = isLeader;
        CurrentLeader = currentLeader;
        LastElectionAttempt = lastElectionAttempt;
        NextElectionAttempt = nextElectionAttempt;
    }

    /// <summary>
    /// Gets a value indicating whether this instance is the current leader.
    /// </summary>
    public bool IsLeader { get; }

    /// <summary>
    /// Gets information about the current leader, if known.
    /// </summary>
    public LeaderInfo? CurrentLeader { get; }

    /// <summary>
    /// Gets the timestamp of the last election attempt.
    /// </summary>
    public DateTimeOffset? LastElectionAttempt { get; }

    /// <summary>
    /// Gets the timestamp of the next scheduled election attempt.
    /// </summary>
    public DateTimeOffset? NextElectionAttempt { get; }

    /// <summary>
    /// Creates a leadership status indicating this instance is the leader.
    /// </summary>
    /// <param name="leaderInfo">Information about the leader.</param>
    /// <returns>A <see cref="LeadershipStatus"/> indicating leadership.</returns>
    public static LeadershipStatus Leader(LeaderInfo leaderInfo)
    {
        return new LeadershipStatus(true, leaderInfo);
    }

    /// <summary>
    /// Creates a leadership status indicating this instance is not the leader.
    /// </summary>
    /// <param name="currentLeader">Information about the current leader, if known.</param>
    /// <returns>A <see cref="LeadershipStatus"/> indicating non-leadership.</returns>
    public static LeadershipStatus Follower(LeaderInfo? currentLeader = null)
    {
        return new LeadershipStatus(false, currentLeader);
    }

    /// <summary>
    /// Creates a leadership status indicating no current leader is known.
    /// </summary>
    /// <param name="lastElectionAttempt">The timestamp of the last election attempt.</param>
    /// <param name="nextElectionAttempt">The timestamp of the next scheduled election attempt.</param>
    /// <returns>A <see cref="LeadershipStatus"/> indicating no known leader.</returns>
    public static LeadershipStatus NoLeader(
        DateTimeOffset? lastElectionAttempt = null,
        DateTimeOffset? nextElectionAttempt = null)
    {
        return new LeadershipStatus(false, null, lastElectionAttempt, nextElectionAttempt);
    }
}

namespace MultiLock;

/// <summary>
/// Provides data for leadership change events.
/// </summary>
public sealed class LeadershipChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LeadershipChangedEventArgs"/> class.
    /// </summary>
    /// <param name="previousStatus">The previous leadership status.</param>
    /// <param name="currentStatus">The current leadership status.</param>
    public LeadershipChangedEventArgs(LeadershipStatus previousStatus, LeadershipStatus currentStatus)
    {
        PreviousStatus = previousStatus ?? throw new ArgumentNullException(nameof(previousStatus));
        CurrentStatus = currentStatus ?? throw new ArgumentNullException(nameof(currentStatus));
    }

    /// <summary>
    /// Gets the previous leadership status.
    /// </summary>
    public LeadershipStatus PreviousStatus { get; }

    /// <summary>
    /// Gets the current leadership status.
    /// </summary>
    public LeadershipStatus CurrentStatus { get; }

    /// <summary>
    /// Gets a value indicating whether this instance became the leader.
    /// </summary>
    public bool BecameLeader => !PreviousStatus.IsLeader && CurrentStatus.IsLeader;

    /// <summary>
    /// Gets a value indicating whether this instance lost leadership.
    /// </summary>
    public bool LostLeadership => PreviousStatus.IsLeader && !CurrentStatus.IsLeader;
}

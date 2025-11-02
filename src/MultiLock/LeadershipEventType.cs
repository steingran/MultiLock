namespace MultiLock;

/// <summary>
/// Specifies the types of leadership events that can be observed.
/// This enumeration supports bitwise combination of its member values.
/// </summary>
[Flags]
public enum LeadershipEventType
{
    /// <summary>
    /// No events.
    /// </summary>
    None = 0,

    /// <summary>
    /// Event fired when this instance acquires leadership.
    /// </summary>
    Acquired = 1,

    /// <summary>
    /// Event fired when this instance loses leadership.
    /// </summary>
    Lost = 2,

    /// <summary>
    /// Event fired when leadership status changes (including when another participant becomes leader).
    /// </summary>
    Changed = 4,

    /// <summary>
    /// All leadership events.
    /// </summary>
    All = Acquired | Lost | Changed
}


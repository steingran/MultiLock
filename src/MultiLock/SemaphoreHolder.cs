namespace MultiLock;

/// <summary>
/// Represents information about a holder of a semaphore slot.
/// </summary>
/// <param name="HolderId">The unique identifier of the holder.</param>
/// <param name="AcquiredAt">The time when the slot was acquired.</param>
/// <param name="LastHeartbeat">The time of the last heartbeat update.</param>
/// <param name="Metadata">Optional metadata associated with the holder.</param>
public sealed record SemaphoreHolder(
    string HolderId,
    DateTimeOffset AcquiredAt,
    DateTimeOffset LastHeartbeat,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>
    /// Determines whether the holder's slot is still healthy based on the specified timeout.
    /// </summary>
    /// <param name="timeout">The timeout duration after which a slot is considered unhealthy.</param>
    /// <returns>True if the slot is healthy; otherwise, false.</returns>
    public bool IsHealthy(TimeSpan timeout)
    {
        return DateTimeOffset.UtcNow - LastHeartbeat < timeout;
    }
}


namespace MultiLock;

/// <summary>
/// Represents information about a distributed semaphore.
/// </summary>
/// <param name="SemaphoreName">The name of the semaphore.</param>
/// <param name="MaxCount">The maximum number of concurrent holders allowed.</param>
/// <param name="CurrentCount">The current number of active holders.</param>
/// <param name="Holders">The list of current holders.</param>
public sealed record SemaphoreInfo(
    string SemaphoreName,
    int MaxCount,
    int CurrentCount,
    IReadOnlyList<SemaphoreHolder> Holders)
{
    /// <summary>
    /// Gets the number of available slots in the semaphore.
    /// </summary>
    public int AvailableSlots => Math.Max(0, MaxCount - CurrentCount);

    /// <summary>
    /// Gets a value indicating whether the semaphore has available slots.
    /// </summary>
    public bool HasAvailableSlots => AvailableSlots > 0;

    /// <summary>
    /// Gets a value indicating whether the semaphore is at full capacity.
    /// </summary>
    public bool IsFull => CurrentCount >= MaxCount;
}


namespace MultiLock;

/// <summary>
/// Represents the current status of a semaphore participant.
/// </summary>
/// <param name="IsHolding">Whether this participant currently holds a slot.</param>
/// <param name="CurrentCount">The current number of holders in the semaphore.</param>
/// <param name="MaxCount">The maximum number of concurrent holders allowed.</param>
/// <param name="LastAcquisitionAttempt">The time of the last acquisition attempt.</param>
/// <param name="NextAcquisitionAttempt">The scheduled time for the next acquisition attempt, if any.</param>
public sealed record SemaphoreStatus(
    bool IsHolding,
    int CurrentCount,
    int MaxCount,
    DateTimeOffset? LastAcquisitionAttempt,
    DateTimeOffset? NextAcquisitionAttempt)
{
    /// <summary>
    /// Gets a value indicating whether this status reflects an actual observation from the provider.
    /// This is <c>false</c> only for the initial <see cref="Unknown"/> state, before the first
    /// provider read. While unknown, availability is reported conservatively as none.
    /// </summary>
    public bool HasInformation { get; init; } = true;

    /// <summary>
    /// Gets the number of available slots in the semaphore. Returns 0 while the status is
    /// <see cref="Unknown"/> so callers don't act on unobserved capacity.
    /// </summary>
    public int AvailableSlots => HasInformation ? Math.Max(0, MaxCount - CurrentCount) : 0;

    /// <summary>
    /// Gets a value indicating whether the semaphore has available slots.
    /// </summary>
    public bool HasAvailableSlots => AvailableSlots > 0;

    /// <summary>
    /// Creates a status indicating the participant is holding a slot.
    /// </summary>
    /// <param name="currentCount">The current number of holders.</param>
    /// <param name="maxCount">The maximum number of holders.</param>
    /// <returns>A new <see cref="SemaphoreStatus"/> instance.</returns>
    public static SemaphoreStatus Holding(int currentCount, int maxCount) =>
        new(true, currentCount, maxCount, DateTimeOffset.UtcNow, null);

    /// <summary>
    /// Creates a status indicating the participant is waiting for a slot.
    /// </summary>
    /// <param name="currentCount">The current number of holders.</param>
    /// <param name="maxCount">The maximum number of holders.</param>
    /// <param name="nextAttempt">The scheduled time for the next acquisition attempt.</param>
    /// <returns>A new <see cref="SemaphoreStatus"/> instance.</returns>
    public static SemaphoreStatus Waiting(int currentCount, int maxCount, DateTimeOffset? nextAttempt) =>
        new(false, currentCount, maxCount, DateTimeOffset.UtcNow, nextAttempt);

    /// <summary>
    /// Creates a status indicating no semaphore information is available.
    /// </summary>
    /// <param name="maxCount">The maximum number of holders.</param>
    /// <returns>A new <see cref="SemaphoreStatus"/> instance.</returns>
    public static SemaphoreStatus Unknown(int maxCount) =>
        new(false, 0, maxCount, null, null) { HasInformation = false };
}


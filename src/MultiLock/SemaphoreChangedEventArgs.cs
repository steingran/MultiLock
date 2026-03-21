namespace MultiLock;

/// <summary>
/// Provides data for semaphore status change events.
/// </summary>
/// <param name="PreviousStatus">The previous semaphore status.</param>
/// <param name="CurrentStatus">The current semaphore status.</param>
public sealed record SemaphoreChangedEventArgs(
    SemaphoreStatus PreviousStatus,
    SemaphoreStatus CurrentStatus)
{
    /// <summary>
    /// Gets a value indicating whether the participant acquired a slot in this change.
    /// </summary>
    public bool AcquiredSlot => !PreviousStatus.IsHolding && CurrentStatus.IsHolding;

    /// <summary>
    /// Gets a value indicating whether the participant lost their slot in this change.
    /// </summary>
    public bool LostSlot => PreviousStatus.IsHolding && !CurrentStatus.IsHolding;

    /// <summary>
    /// Gets a value indicating whether the holding status changed.
    /// </summary>
    public bool HoldingStatusChanged => PreviousStatus.IsHolding != CurrentStatus.IsHolding;
}


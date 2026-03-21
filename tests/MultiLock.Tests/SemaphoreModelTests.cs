using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Tests for semaphore model types: SemaphoreStatus, SemaphoreInfo, SemaphoreHolder, SemaphoreChangedEventArgs.
/// </summary>
public class SemaphoreModelTests
{
    // SemaphoreStatus

    [Fact]
    public void SemaphoreStatus_Holding_ShouldSetIsHoldingTrue()
    {
        var status = SemaphoreStatus.Holding(currentCount: 2, maxCount: 5);
        status.IsHolding.ShouldBeTrue();
        status.CurrentCount.ShouldBe(2);
        status.MaxCount.ShouldBe(5);
        status.LastAcquisitionAttempt.ShouldNotBeNull();
        status.NextAcquisitionAttempt.ShouldBeNull();
    }

    [Fact]
    public void SemaphoreStatus_Waiting_ShouldSetIsHoldingFalse()
    {
        var next = DateTimeOffset.UtcNow.AddSeconds(5);
        var status = SemaphoreStatus.Waiting(currentCount: 3, maxCount: 3, nextAttempt: next);
        status.IsHolding.ShouldBeFalse();
        status.CurrentCount.ShouldBe(3);
        status.MaxCount.ShouldBe(3);
        status.NextAcquisitionAttempt.ShouldBe(next);
    }

    [Fact]
    public void SemaphoreStatus_Unknown_ShouldHaveZeroCount()
    {
        var status = SemaphoreStatus.Unknown(maxCount: 10);
        status.IsHolding.ShouldBeFalse();
        status.CurrentCount.ShouldBe(0);
        status.MaxCount.ShouldBe(10);
        status.LastAcquisitionAttempt.ShouldBeNull();
        status.NextAcquisitionAttempt.ShouldBeNull();
    }

    [Fact]
    public void SemaphoreStatus_AvailableSlots_ShouldReturnDifference()
    {
        var status = SemaphoreStatus.Holding(currentCount: 2, maxCount: 5);
        status.AvailableSlots.ShouldBe(3);
    }

    [Fact]
    public void SemaphoreStatus_AvailableSlots_WhenOverAdmitted_ShouldReturnZero()
    {
        // Simulate transient over-admission where currentCount exceeds maxCount
        var status = new SemaphoreStatus(false, CurrentCount: 6, MaxCount: 5, null, null);
        status.AvailableSlots.ShouldBe(0);
    }

    [Fact]
    public void SemaphoreStatus_HasAvailableSlots_WhenSlotsAvailable_ShouldReturnTrue()
    {
        var status = SemaphoreStatus.Holding(currentCount: 1, maxCount: 5);
        status.HasAvailableSlots.ShouldBeTrue();
    }

    [Fact]
    public void SemaphoreStatus_HasAvailableSlots_WhenFull_ShouldReturnFalse()
    {
        var status = SemaphoreStatus.Holding(currentCount: 5, maxCount: 5);
        status.HasAvailableSlots.ShouldBeFalse();
    }

    // SemaphoreInfo

    [Fact]
    public void SemaphoreInfo_AvailableSlots_ShouldReturnDifference()
    {
        var info = new SemaphoreInfo("test", MaxCount: 5, CurrentCount: 2, Holders: []);
        info.AvailableSlots.ShouldBe(3);
    }

    [Fact]
    public void SemaphoreInfo_AvailableSlots_WhenOverAdmitted_ShouldReturnZero()
    {
        var info = new SemaphoreInfo("test", MaxCount: 3, CurrentCount: 5, Holders: []);
        info.AvailableSlots.ShouldBe(0);
    }

    [Fact]
    public void SemaphoreInfo_HasAvailableSlots_WhenNotFull_ShouldReturnTrue()
    {
        var info = new SemaphoreInfo("test", MaxCount: 5, CurrentCount: 2, Holders: []);
        info.HasAvailableSlots.ShouldBeTrue();
    }

    [Fact]
    public void SemaphoreInfo_IsFull_WhenAtCapacity_ShouldReturnTrue()
    {
        var info = new SemaphoreInfo("test", MaxCount: 3, CurrentCount: 3, Holders: []);
        info.IsFull.ShouldBeTrue();
    }

    [Fact]
    public void SemaphoreInfo_IsFull_WhenBelowCapacity_ShouldReturnFalse()
    {
        var info = new SemaphoreInfo("test", MaxCount: 3, CurrentCount: 2, Holders: []);
        info.IsFull.ShouldBeFalse();
    }

    // SemaphoreHolder

    [Fact]
    public void SemaphoreHolder_IsHealthy_WhenRecentHeartbeat_ShouldReturnTrue()
    {
        var holder = new SemaphoreHolder("h1", DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddSeconds(-5), new Dictionary<string, string>());
        holder.IsHealthy(TimeSpan.FromSeconds(30)).ShouldBeTrue();
    }

    [Fact]
    public void SemaphoreHolder_IsHealthy_WhenStaleHeartbeat_ShouldReturnFalse()
    {
        var holder = new SemaphoreHolder("h1", DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddSeconds(-60), new Dictionary<string, string>());
        holder.IsHealthy(TimeSpan.FromSeconds(30)).ShouldBeFalse();
    }

    // SemaphoreChangedEventArgs

    [Fact]
    public void SemaphoreChangedEventArgs_AcquiredSlot_WhenTransitioningToHolding_ShouldReturnTrue()
    {
        var prev = SemaphoreStatus.Waiting(0, 5, null);
        var curr = SemaphoreStatus.Holding(1, 5);
        var args = new SemaphoreChangedEventArgs(prev, curr);
        args.AcquiredSlot.ShouldBeTrue();
        args.LostSlot.ShouldBeFalse();
        args.HoldingStatusChanged.ShouldBeTrue();
    }

    [Fact]
    public void SemaphoreChangedEventArgs_LostSlot_WhenTransitioningFromHolding_ShouldReturnTrue()
    {
        var prev = SemaphoreStatus.Holding(1, 5);
        var curr = SemaphoreStatus.Waiting(0, 5, null);
        var args = new SemaphoreChangedEventArgs(prev, curr);
        args.LostSlot.ShouldBeTrue();
        args.AcquiredSlot.ShouldBeFalse();
        args.HoldingStatusChanged.ShouldBeTrue();
    }

    [Fact]
    public void SemaphoreChangedEventArgs_WhenStatusUnchanged_ShouldReturnFalse()
    {
        var prev = SemaphoreStatus.Waiting(1, 5, null);
        var curr = SemaphoreStatus.Waiting(2, 5, null);
        var args = new SemaphoreChangedEventArgs(prev, curr);
        args.AcquiredSlot.ShouldBeFalse();
        args.LostSlot.ShouldBeFalse();
        args.HoldingStatusChanged.ShouldBeFalse();
    }
}


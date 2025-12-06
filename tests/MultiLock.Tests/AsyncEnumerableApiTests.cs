using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Tests for the AsyncEnumerable API of the leader election service.
/// </summary>
public class AsyncEnumerableApiTests
{
    [Fact]
    public async Task GetLeadershipChangesAsync_ShouldEmitAcquiredEvent_WhenBecomingLeader()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        var cts = new CancellationTokenSource();

        // Act - Start listening for events BEFORE starting the service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
                if (e.BecameLeader)
                {
                    break; // Stop after first acquired event
                }
            }
        }, cancellationToken);

        // Give the event listener task a chance to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        // Start the service and wait for leadership
        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Any(e => e.BecameLeader), TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        events.ShouldHaveSingleItem();
        events[0].BecameLeader.ShouldBeTrue();
        events[0].CurrentStatus.IsLeader.ShouldBeTrue();

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { } // Wait for the background task to complete
        // ReSharper disable once MethodSupportsCancellation
        await service.StopAsync();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task GetLeadershipChangesAsync_ShouldEmitLostEvent_WhenLosingLeadership()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        var cts = new CancellationTokenSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the event listener task a chance to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token); // This should trigger lost leadership event
        await TestHelpers.WaitForConditionAsync(() => events.Any(e => e.LostLeadership), TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        events.ShouldContain(e => e.BecameLeader);
        events.ShouldContain(e => e.LostLeadership);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { } // Wait for the background task to complete
        await services.DisposeAsync();
    }

    [Fact]
    public async Task GetLeadershipChangesAsync_WithFilter_ShouldOnlyEmitAcquiredEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        var cts = new CancellationTokenSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(LeadershipEventType.Acquired, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
                if (e.BecameLeader)
                {
                    break; // Stop after first acquired event
                }
            }
        }, cancellationToken);

        // Give the event listener task a chance to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Any(e => e.BecameLeader), TimeSpan.FromSeconds(5), cts.Token, eventsLock);
        await service.StopAsync(cts.Token); // This should trigger lost leadership event (but filtered out)
        await TestHelpers.WaitForConditionAsync(() => !service.IsLeader, TimeSpan.FromSeconds(5), cts.Token);

        // Assert
        events.ShouldHaveSingleItem();
        events[0].BecameLeader.ShouldBeTrue();
        events.ShouldNotContain(e => e.LostLeadership);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { } // Wait for the background task to complete
        await services.DisposeAsync();
    }

    [Fact]
    public async Task GetLeadershipChangesAsync_WithFilter_ShouldOnlyEmitLostEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        var cts = new CancellationTokenSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(LeadershipEventType.Lost, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
                if (e.LostLeadership)
                {
                    break; // Stop after first lost event
                }
            }
        }, cancellationToken);

        // Give the event listener task a chance to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token); // This should trigger lost leadership event
        await TestHelpers.WaitForConditionAsync(() => events.Any(e => e.LostLeadership), TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        events.ShouldHaveSingleItem();
        events[0].LostLeadership.ShouldBeTrue();
        events.ShouldNotContain(e => e.BecameLeader);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { } // Wait for the background task to complete
        await services.DisposeAsync();
    }

    [Fact]
    public async Task GetLeadershipChangesAsync_WithChangedFilter_ShouldEmitAllStatusChanges()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        var cts = new CancellationTokenSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(LeadershipEventType.Changed, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the event listener task a chance to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token); // This should trigger lost leadership event
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        events.Count.ShouldBeGreaterThanOrEqualTo(1);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { } // Wait for the background task to complete
        await services.DisposeAsync();
    }

    [Fact]
    public async Task GetLeadershipChangesAsync_MultipleSubscribers_ShouldAllReceiveEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events1 = new List<LeadershipChangedEventArgs>();
        var events2 = new List<LeadershipChangedEventArgs>();
        var events3 = new List<LeadershipChangedEventArgs>();
        object events1Lock = new();
        object events2Lock = new();
        object events3Lock = new();
        var cts = new CancellationTokenSource();

        // Act - Create three subscribers BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask1 = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken))
            {
                lock (events1Lock)
                {
                    events1.Add(e);
                }
            }
        }, cancellationToken);

        var eventTask2 = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken))
            {
                lock (events2Lock)
                {
                    events2.Add(e);
                }
            }
        }, cancellationToken);

        var eventTask3 = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken))
            {
                lock (events3Lock)
                {
                    events3.Add(e);
                }
            }
        }, cancellationToken);

        // Give the event listener tasks a chance to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token); // This should trigger lost leadership event
        await TestHelpers.WaitForConditionAsync(() =>
        {
            bool result;
            lock (events1Lock)
            {
                lock (events2Lock)
                {
                    lock (events3Lock)
                    {
                        result = events1.Any(e => e.LostLeadership) &&
                                 events2.Any(e => e.LostLeadership) &&
                                 events3.Any(e => e.LostLeadership);
                    }
                }
            }
            return result;
        }, TimeSpan.FromSeconds(5), cts.Token);

        // Assert - All subscribers should receive the same events
        events1.ShouldContain(e => e.BecameLeader);
        events2.ShouldContain(e => e.BecameLeader);
        events3.ShouldContain(e => e.BecameLeader);

        events1.ShouldContain(e => e.LostLeadership);
        events2.ShouldContain(e => e.LostLeadership);
        events3.ShouldContain(e => e.LostLeadership);

        // Cleanup
        await cts.CancelAsync();
        try { await Task.WhenAll(eventTask1, eventTask2, eventTask3); } catch (OperationCanceledException) { } // Wait for all background tasks to complete
        await services.DisposeAsync();
    }

    [Fact]
    public async Task GetLeadershipChangesAsync_CancellationToken_ShouldStopEnumeration()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        var cts = new CancellationTokenSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the event listener task a chance to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Any(e => e.BecameLeader), TimeSpan.FromSeconds(5), cts.Token, eventsLock);
        await cts.CancelAsync(); // Cancel the enumeration

        // Assert
        events.ShouldContain(e => e.BecameLeader);

        // Cleanup
        try { await eventTask; } catch (OperationCanceledException) { } // Wait for the background task to complete
        // ReSharper disable once MethodSupportsCancellation
        await service.StopAsync();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task GetLeadershipChangesAsync_EventOrder_ShouldBeCorrect()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        var cts = new CancellationTokenSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the event listener task a chance to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token); // This should trigger lost leadership event
        await TestHelpers.WaitForConditionAsync(() => events.Any(e => e.LostLeadership), TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        LeadershipChangedEventArgs? acquiredEvent = events.FirstOrDefault(e => e.BecameLeader);
        LeadershipChangedEventArgs? lostEvent = events.FirstOrDefault(e => e.LostLeadership);

        acquiredEvent.ShouldNotBeNull();
        lostEvent.ShouldNotBeNull();

        int acquiredIndex = events.IndexOf(acquiredEvent);
        int lostIndex = events.IndexOf(lostEvent);

        acquiredIndex.ShouldBeLessThan(lostIndex);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { } // Wait for the background task to complete
        await services.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_WithoutStarting_ShouldNotThrow()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();

        // Act & Assert - StopAsync should not throw even if the service was never started
        await Should.NotThrowAsync(async () => await service.StopAsync(CancellationToken.None));

        // Cleanup
        await services.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ShouldCompleteChannelWithTryComplete()
    {
        // Arrange - This test verifies that DisposeAsync uses TryComplete to avoid exceptions
        // when the channel has already been completed by StopAsync
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);

        // Stop the service first (this completes the channel)
        await service.StopAsync(cts.Token);

        // Act & Assert - DisposeAsync should not throw even though the channel is already completed
        await Should.NotThrowAsync(async () => await services.DisposeAsync());
    }
}

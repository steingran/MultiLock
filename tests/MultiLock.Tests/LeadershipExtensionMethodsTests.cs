using Microsoft.Extensions.DependencyInjection;
using MultiLock.Extensions;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Tests for the AsyncEnumerable extension methods for leadership events.
/// </summary>
public class LeadershipExtensionMethodsTests
{
    [Fact]
    public async Task Where_ShouldFilterEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken)
                .Where(e => e.BecameLeader, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
                if (events.Count >= 1)
                {
                    break;
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Now safe to access events collection without lock since eventTask has completed
        events.ShouldHaveSingleItem();
        events.ShouldAllBe(e => e.BecameLeader);

        // ReSharper disable once MethodSupportsCancellation
        await service.StopAsync();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Select_ShouldTransformEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var participantIds = new List<string>();
        object participantIdsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (string participantId in service.GetLeadershipChangesAsync(cancellationToken)
                .Select(e => e.CurrentStatus.CurrentLeader?.LeaderId ?? "none", cancellationToken))
            {
                lock (participantIdsLock)
                {
                    participantIds.Add(participantId);
                }
                if (participantIds.Count >= 2)
                {
                    break;
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => participantIds.Count >= 2, TimeSpan.FromSeconds(5), cts.Token, participantIdsLock);

        // Assert
        participantIds.ShouldContain("test-participant");

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Take_ShouldLimitNumberOfEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken).Take(2, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 2, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        events.Count.ShouldBe(2);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Skip_ShouldSkipEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken)
                               .Skip(1, cancellationToken).Take(1, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        events.ShouldHaveSingleItem();
        events.First().LostLeadership.ShouldBeTrue();

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task DistinctUntilChanged_ShouldFilterConsecutiveDuplicates()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken)
                .DistinctUntilChanged(cancellationToken)
                .Take(2, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        events.Count.ShouldBeGreaterThanOrEqualTo(1);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task ForEachAsync_ShouldExecuteActionForEachEvent()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        int eventCount = 0;
        object eventCountLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await service.GetLeadershipChangesAsync(cancellationToken)
                .Take(2, cancellationToken)
                .ForEachAsync(_ =>
                {
                    lock (eventCountLock)
                    {
                        eventCount++;
                    }
                }, cancellationToken);
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => eventCount >= 2, TimeSpan.FromSeconds(5), cts.Token, eventCountLock);

        // Assert
        eventCount.ShouldBe(2);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task ForEachAsync_WithAsyncAction_ShouldExecuteAsyncActionForEachEvent()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        int eventCount = 0;
        object eventCountLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await service.GetLeadershipChangesAsync(cancellationToken)
                .Take(2, cancellationToken)
                .ForEachAsync(async _ =>
                {
                    await Task.Delay(10, cancellationToken);
                    lock (eventCountLock)
                    {
                        eventCount++;
                    }
                }, cancellationToken);
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => eventCount >= 2, TimeSpan.FromSeconds(5), cts.Token, eventCountLock);

        // Assert
        eventCount.ShouldBe(2);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task BufferByCount_ShouldGroupEventsByCount()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var buffers = new List<IReadOnlyList<LeadershipChangedEventArgs>>();
        object buffersLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (IReadOnlyList<LeadershipChangedEventArgs> buffer in service
                               .GetLeadershipChangesAsync(cancellationToken).Buffer(2, cancellationToken))
            {
                lock (buffersLock)
                {
                    buffers.Add(buffer);
                }
                if (buffers.Count >= 1)
                {
                    break;
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => buffers.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, buffersLock);

        // Assert
        buffers.ShouldHaveSingleItem();
        buffers.First().Count.ShouldBe(2);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task BufferByTime_ShouldGroupEventsByTimeWindow()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var buffers = new List<IReadOnlyList<LeadershipChangedEventArgs>>();
        object buffersLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (IReadOnlyList<LeadershipChangedEventArgs> buffer in service.GetLeadershipChangesAsync(cancellationToken)
                .Buffer(TimeSpan.FromMilliseconds(200), cancellationToken))
            {
                lock (buffersLock)
                {
                    buffers.Add(buffer);
                }

                // Break after we get at least one buffer with events
                bool shouldBreak;
                lock (buffersLock)
                {
                    shouldBreak = buffers.Any(b => b.Count > 0);
                }
                if (shouldBreak)
                {
                    break;
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);

        // Wait for the buffer time window to complete (200ms)
        await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token);

        // Stop the service to trigger a "leadership lost" event, which will cause the buffer to flush
        // ReSharper disable once MethodSupportsCancellation
        await service.StopAsync();

        // Wait for the buffer to be emitted
        await TestHelpers.WaitForConditionAsync(() => buffers.Any(b => b.Count > 0), TimeSpan.FromSeconds(5), cts.Token, buffersLock);

        // Assert - time-based buffering should have captured at least one buffer with events
        buffers.Count.ShouldBeGreaterThanOrEqualTo(1);
        buffers.SelectMany(b => b).Count().ShouldBeGreaterThanOrEqualTo(1);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Throttle_ShouldLimitEventRate()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken)
                .Throttle(TimeSpan.FromMilliseconds(200), cancellationToken)
                .Take(2, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);

        // Wait for throttle delay and event processing
        await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        events.Count.ShouldBeGreaterThanOrEqualTo(1);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Debounce_ShouldDelayEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken)
                .Debounce(TimeSpan.FromMilliseconds(100), cancellationToken)
                .Take(2, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);

        // Wait for debounce delay and event processing
        await Task.Delay(TimeSpan.FromMilliseconds(150), cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Assert
        events.Count.ShouldBeGreaterThanOrEqualTo(1);

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task TakeUntilLeader_ShouldStopWhenBecomingLeader()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken).TakeUntilLeader(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Any(e => e.BecameLeader), TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Take snapshot of events while holding lock to ensure thread-safety
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.ShouldContain(e => e.BecameLeader);
        eventSnapshot.Last().BecameLeader.ShouldBeTrue();

        // ReSharper disable once MethodSupportsCancellation
        await service.StopAsync();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task WhileLeader_ShouldContinueWhileLeader()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken).WhileLeader(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);

        // Wait for at least one event to be captured
        await TestHelpers.WaitForConditionAsync(() => events.Any(e => e.BecameLeader), TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => !service.IsLeader, TimeSpan.FromSeconds(5), cts.Token);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Take snapshot of events while holding lock to ensure thread-safety
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.ShouldContain(e => e.BecameLeader);
        eventSnapshot.ShouldAllBe(e => e.CurrentStatus.IsLeader || e.LostLeadership);

        await services.DisposeAsync();
    }

    [Fact]
    public async Task OnLeadershipTransition_ShouldExecuteCallbacks()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        bool acquiredCalled = false;
        bool lostCalled = false;
        object callbackLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await service.GetLeadershipChangesAsync(cancellationToken)
                .Take(2, cancellationToken)
                .OnLeadershipTransition(
                    onAcquired: _ =>
                    {
                        lock (callbackLock)
                        {
                            acquiredCalled = true;
                        }
                    },
                    onLost: _ =>
                    {
                        lock (callbackLock)
                        {
                            lostCalled = true;
                        }
                    },
                    cancellationToken);
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => acquiredCalled && lostCalled, TimeSpan.FromSeconds(5), cts.Token, callbackLock);

        // Assert
        acquiredCalled.ShouldBeTrue();
        lostCalled.ShouldBeTrue();

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task OnLeadershipTransition_WithAsyncCallbacks_ShouldExecuteAsyncCallbacks()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        bool acquiredCalled = false;
        bool lostCalled = false;
        object callbackLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await service.GetLeadershipChangesAsync(cancellationToken)
                .Take(2, cancellationToken)
                .OnLeadershipTransition(
                    onAcquired: async _ =>
                    {
                        await Task.Delay(10, cancellationToken);
                        lock (callbackLock)
                        {
                            acquiredCalled = true;
                        }
                    },
                    onLost: async _ =>
                    {
                        await Task.Delay(10, cancellationToken);
                        lock (callbackLock)
                        {
                            lostCalled = true;
                        }
                    },
                    cancellationToken);
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => acquiredCalled && lostCalled, TimeSpan.FromSeconds(5), cts.Token, callbackLock);

        // Assert
        acquiredCalled.ShouldBeTrue();
        lostCalled.ShouldBeTrue();

        // Cleanup
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { }
        await services.DisposeAsync();
    }

    [Fact]
    public async Task CombinedExtensions_ShouldWorkTogether()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        ILeaderElectionService service = services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken)
                .Where(e => e.BecameLeader || e.LostLeadership, cancellationToken)
                .Take(2, cancellationToken)
                .DistinctUntilChanged(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 2, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Take snapshot of events while holding lock to ensure thread-safety
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.Length.ShouldBe(2);
        eventSnapshot.ShouldContain(e => e.BecameLeader);
        eventSnapshot.ShouldContain(e => e.LostLeadership);

        await services.DisposeAsync();
    }
}

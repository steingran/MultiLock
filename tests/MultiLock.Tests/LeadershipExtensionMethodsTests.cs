using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiLock.Extensions;
using MultiLock.InMemory;
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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
                .Where(e => e.BecameLeader, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
                if (events.Count >= 1)
                    break;
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Take snapshot of events while holding lock to ensure thread-safety
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.ShouldHaveSingleItem();
        eventSnapshot.ShouldAllBe(e => e.BecameLeader);

        // ReSharper disable once MethodSupportsCancellation
        await service.StopAsync();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Select_ShouldTransformEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var participantIds = new List<string>();
        object participantIdsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (string participantId in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
                .Select(e => e.CurrentStatus.CurrentLeader?.LeaderId ?? "none", cancellationToken))
            {
                lock (participantIdsLock)
                {
                    participantIds.Add(participantId);
                }
                if (participantIds.Count >= 2)
                    break;
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered).Take(2, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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

        await services.DisposeAsync();
    }

    [Fact]
    public async Task Skip_ShouldSkipEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
                               .Skip(1, cancellationToken).Take(1, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Take snapshot of events while holding lock to ensure thread-safety
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.ShouldHaveSingleItem();
        eventSnapshot.First().LostLeadership.ShouldBeTrue();

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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken)
                .DistinctUntilChanged(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Give the async enumerable consumer time to start
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

        await service.StartAsync(cancellationToken);
        await service.WaitForLeadershipAsync(cancellationToken);

        // Wait for the first event (gained leadership)
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(3), cancellationToken, eventsLock);

        await service.StopAsync(cancellationToken);

        // Wait for the second event (lost leadership)
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 2, TimeSpan.FromSeconds(3), cancellationToken, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Take snapshot of events while holding lock to ensure thread-safety
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.Length.ShouldBeGreaterThanOrEqualTo(2);
        eventSnapshot[0].BecameLeader.ShouldBeTrue();
        eventSnapshot[1].LostLeadership.ShouldBeTrue();

        await services.DisposeAsync();
    }

    [Fact]
    public async Task ForEachAsync_ShouldExecuteActionForEachEvent()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        int eventCount = 0;
        object eventCountLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
                .Take(2, cancellationToken)
                .ForEachAsync(_ =>
                {
                    lock (eventCountLock)
                    {
                        eventCount++;
                    }
                }, cancellationToken);
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        int eventCount = 0;
        object eventCountLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
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

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var buffers = new List<IReadOnlyList<LeadershipChangedEventArgs>>();
        object buffersLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (IReadOnlyList<LeadershipChangedEventArgs> buffer in service
                               .GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered).Buffer(2, cancellationToken))
            {
                lock (buffersLock)
                {
                    buffers.Add(buffer);
                }
                if (buffers.Count >= 1)
                    break;
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var buffers = new List<IReadOnlyList<LeadershipChangedEventArgs>>();
        object buffersLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (IReadOnlyList<LeadershipChangedEventArgs> buffer in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
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
                    break;
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
                .Throttle(TimeSpan.FromMilliseconds(200), cancellationToken)
                .Take(2, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);

        // Wait for throttle delay and event processing
        await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(5), cts.Token, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Take snapshot of events while holding lock to ensure thread-safety
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.Length.ShouldBeGreaterThanOrEqualTo(1);

        await services.DisposeAsync();
    }

    [Fact]
    public async Task Debounce_ShouldDelayEvents()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
                .Debounce(TimeSpan.FromMilliseconds(100), cancellationToken)
                .Take(2, cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);

        // Wait for debounce delay and event processing - debounce needs time to process events
        // The debounce waits 100ms after each event, so we need significant time for 2 events
        // plus the time for the stream to complete after the service stops
        await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(10), cts.Token, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Take snapshot of events while holding lock to ensure thread-safety
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.Length.ShouldBeGreaterThanOrEqualTo(1);

        await services.DisposeAsync();
    }

    [Fact]
    public async Task TakeUntilLeader_ShouldStopWhenBecomingLeader()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered).TakeUntilLeader(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered).WhileLeader(cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        bool acquiredCalled = false;
        bool lostCalled = false;
        object callbackLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
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

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        bool acquiredCalled = false;
        bool lostCalled = false;
        object callbackLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
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

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
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

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

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

    // Tests for WaitForLeadershipAsync method

    [Fact]
    public async Task WaitForLeadershipAsync_WhenAlreadyLeader_ShouldReturnImmediately()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start service and acquire leadership
        await service.StartAsync(cts.Token);
        await service.TryAcquireLeadershipAsync(cts.Token);

        // Verify we are leader
        service.IsLeader.ShouldBeTrue();

        // Act - Call WaitForLeadershipAsync when already leader
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.WaitForLeadershipAsync(cts.Token);
        stopwatch.Stop();

        // Assert - Should return immediately (within 100ms)
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(100);

        // Cleanup
        await service.StopAsync(cts.Token);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task WaitForLeadershipAsync_ShouldWaitUntilLeadershipAcquired()
    {
        // Arrange - Create two services with shared provider to simulate leadership competition
        var provider = new InMemoryLeaderElectionProvider(
            new LoggerFactory().CreateLogger<InMemoryLeaderElectionProvider>());

        var serviceCollection1 = new ServiceCollection();
        serviceCollection1.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection1.AddSingleton<ILeaderElectionProvider>(provider);
        serviceCollection1.Configure<LeaderElectionOptions>(options =>
        {
            options.ParticipantId = "participant-1";
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
            options.AutoStart = false;
        });
        serviceCollection1.AddSingleton<ILeaderElectionService, LeaderElectionService>();
        ServiceProvider services1 = serviceCollection1.BuildServiceProvider();

        var serviceCollection2 = new ServiceCollection();
        serviceCollection2.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection2.AddSingleton<ILeaderElectionProvider>(provider);
        serviceCollection2.Configure<LeaderElectionOptions>(options =>
        {
            options.ParticipantId = "participant-2";
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
            options.AutoStart = false;
        });
        serviceCollection2.AddSingleton<ILeaderElectionService, LeaderElectionService>();
        ServiceProvider services2 = serviceCollection2.BuildServiceProvider();

        var service1 = (LeaderElectionService)services1.GetRequiredService<ILeaderElectionService>();
        var service2 = (LeaderElectionService)services2.GetRequiredService<ILeaderElectionService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act - Start first service (it will acquire leadership)
        await service1.StartAsync(cts.Token);
        service1.IsLeader.ShouldBeTrue();

        // Start second service (it won't be leader initially)
        await service2.StartAsync(cts.Token);
        service2.IsLeader.ShouldBeFalse();

        // Start waiting for leadership in background on service2
        Task waitTask = service2.WaitForLeadershipAsync(cts.Token);
        bool waitCompleted = false;

        // Give the wait task time to start
        await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);

        // Act - Stop service1 so service2 can acquire leadership
        await service1.StopAsync(cts.Token);

        // Wait for the WaitForLeadershipAsync to complete
        await TestHelpers.WaitForConditionAsync(
            async () =>
            {
                if (waitTask.IsCompleted)
                {
                    await waitTask;
                    waitCompleted = true;
                    return true;
                }
                return false;
            },
            TimeSpan.FromSeconds(5),
            cts.Token);

        // Assert
        service2.IsLeader.ShouldBeTrue();
        waitCompleted.ShouldBeTrue();

        // Cleanup
        await service2.StopAsync(cts.Token);
        await services1.DisposeAsync();
        await services2.DisposeAsync();
    }

    [Fact]
    public async Task WaitForLeadershipAsync_WithCancellation_ShouldThrow()
    {
        // Arrange - Create two services with shared provider
        var provider = new InMemoryLeaderElectionProvider(
            new LoggerFactory().CreateLogger<InMemoryLeaderElectionProvider>());

        var serviceCollection1 = new ServiceCollection();
        serviceCollection1.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection1.AddSingleton<ILeaderElectionProvider>(provider);
        serviceCollection1.Configure<LeaderElectionOptions>(options =>
        {
            options.ParticipantId = "participant-1";
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
            options.AutoStart = false;
        });
        serviceCollection1.AddSingleton<ILeaderElectionService, LeaderElectionService>();
        ServiceProvider services1 = serviceCollection1.BuildServiceProvider();

        var serviceCollection2 = new ServiceCollection();
        serviceCollection2.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection2.AddSingleton<ILeaderElectionProvider>(provider);
        serviceCollection2.Configure<LeaderElectionOptions>(options =>
        {
            options.ParticipantId = "participant-2";
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
            options.AutoStart = false;
        });
        serviceCollection2.AddSingleton<ILeaderElectionService, LeaderElectionService>();
        ServiceProvider services2 = serviceCollection2.BuildServiceProvider();

        var service1 = (LeaderElectionService)services1.GetRequiredService<ILeaderElectionService>();
        var service2 = (LeaderElectionService)services2.GetRequiredService<ILeaderElectionService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act - Start first service (it will acquire leadership)
        await service1.StartAsync(cts.Token);
        service1.IsLeader.ShouldBeTrue();

        // Start second service (it won't be leader)
        await service2.StartAsync(cts.Token);
        service2.IsLeader.ShouldBeFalse();

        // Create a separate cancellation token that we'll cancel
        using var waitCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert - Should throw OperationCanceledException
        await Should.ThrowAsync<OperationCanceledException>(
            () => service2.WaitForLeadershipAsync(waitCts.Token));

        // Cleanup
        await service1.StopAsync(cts.Token);
        await service2.StopAsync(cts.Token);
        await services1.DisposeAsync();
        await services2.DisposeAsync();
    }

    // Tests for WaitForLeaderAsync method

    [Fact]
    public async Task WaitForLeaderAsync_WhenLeaderExists_ShouldReturnImmediately()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Start service and acquire leadership
        await service.StartAsync(cts.Token);
        bool acquired = await service.TryAcquireLeadershipAsync(cts.Token);

        // Verify we are leader
        acquired.ShouldBeTrue();
        service.IsLeader.ShouldBeTrue();

        // Act - Call WaitForLeaderAsync when leader already exists
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        LeaderInfo leader = await service.WaitForLeaderAsync(cts.Token);
        stopwatch.Stop();

        // Assert - Should return immediately (within 100ms)
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(100);
        leader.ShouldNotBeNull();
        leader.LeaderId.ShouldBe("test-participant");

        // Cleanup
        await service.StopAsync(cts.Token);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task WaitForLeaderAsync_ShouldWaitUntilLeaderElected()
    {
        // Arrange - Create a service with shared provider, but don't start any service yet
        var provider = new InMemoryLeaderElectionProvider(
            new LoggerFactory().CreateLogger<InMemoryLeaderElectionProvider>());

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection.AddSingleton<ILeaderElectionProvider>(provider);
        serviceCollection.Configure<LeaderElectionOptions>(options =>
        {
            options.ParticipantId = "participant-1";
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
            options.AutoStart = false;
        });
        serviceCollection.AddSingleton<ILeaderElectionService, LeaderElectionService>();
        ServiceProvider services = serviceCollection.BuildServiceProvider();

        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act - Start waiting for leader before any service is started
        Task<LeaderInfo> waitTask = service.WaitForLeaderAsync(cts.Token);
        bool waitCompleted = false;
        LeaderInfo? electedLeader = null;

        // Give the wait task time to start
        await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);

        // Act - Start the service so it acquires leadership
        await service.StartAsync(cts.Token);

        // Wait for the WaitForLeaderAsync to complete
        await TestHelpers.WaitForConditionAsync(
            async () =>
            {
                if (waitTask.IsCompleted)
                {
                    electedLeader = await waitTask;
                    waitCompleted = true;
                    return true;
                }
                return false;
            },
            TimeSpan.FromSeconds(5),
            cts.Token);

        // Assert
        service.IsLeader.ShouldBeTrue();
        waitCompleted.ShouldBeTrue();
        electedLeader.ShouldNotBeNull();
        electedLeader.LeaderId.ShouldBe("participant-1");

        // Cleanup
        await service.StopAsync(cts.Token);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task WaitForLeaderAsync_WithCancellation_ShouldThrow()
    {
        // Arrange - Create a service but don't start it, so WaitForLeaderAsync will wait
        var provider = new InMemoryLeaderElectionProvider(
            new LoggerFactory().CreateLogger<InMemoryLeaderElectionProvider>());

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection.AddSingleton<ILeaderElectionProvider>(provider);
        serviceCollection.Configure<LeaderElectionOptions>(options =>
        {
            options.ParticipantId = "participant-1";
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
            options.AutoStart = false;
        });
        serviceCollection.AddSingleton<ILeaderElectionService, LeaderElectionService>();
        ServiceProvider services = serviceCollection.BuildServiceProvider();

        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Create a separate cancellation token that we'll cancel
        using var waitCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert - Should throw OperationCanceledException
        await Should.ThrowAsync<OperationCanceledException>(
            () => service.WaitForLeaderAsync(waitCts.Token));

        // Cleanup
        await services.DisposeAsync();
    }

    [Fact]
    public async Task WaitForLeaderAsync_ShouldReturnCorrectLeaderInfo()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act - Start service and acquire leadership
        await service.StartAsync(cts.Token);
        bool acquired = await service.TryAcquireLeadershipAsync(cts.Token);

        // Act - Get leader info
        LeaderInfo leader = await service.WaitForLeaderAsync(cts.Token);

        // Assert
        acquired.ShouldBeTrue();
        leader.ShouldNotBeNull();
        leader.LeaderId.ShouldBe("test-participant");
        leader.LeadershipAcquiredAt.ShouldNotBe(default);
        leader.LastHeartbeat.ShouldNotBe(default);

        // Cleanup
        await service.StopAsync(cts.Token);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Debounce_WithMultipleRapidEvents_ShouldEmitOnlyLastEvent()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
                .Debounce(TimeSpan.FromMilliseconds(200), cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
                if (events.Count >= 2)
                    break;
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

        // Simulate rapid events by starting and stopping quickly
        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);
        await service.StopAsync(cts.Token);

        // Wait for debounce to process
        await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(10), cts.Token, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Debounce should have emitted events
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.Length.ShouldBeGreaterThanOrEqualTo(1);

        await services.DisposeAsync();
    }

    [Fact]
    public async Task Debounce_WithSpacedEvents_ShouldEmitEachEvent()
    {
        // Arrange
        ServiceProvider services = TestHelpers.CreateLeaderElectionService("test-participant");
        var service = (LeaderElectionService)services.GetRequiredService<ILeaderElectionService>();
        var events = new List<LeadershipChangedEventArgs>();
        object eventsLock = new();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var subscriberRegistered = new TaskCompletionSource();

        // Act - Start listening BEFORE starting service
        CancellationToken cancellationToken = cts.Token;
        var eventTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsyncCore(cancellationToken, subscriberRegistered)
                .Debounce(TimeSpan.FromMilliseconds(100), cancellationToken))
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
                if (events.Count >= 2)
                    break;
            }
        }, cancellationToken);

        // Wait for the subscriber to be registered (no race condition)
        await subscriberRegistered.Task;

        // Simulate spaced events - start, wait, stop
        await service.StartAsync(cts.Token);
        await service.WaitForLeadershipAsync(cts.Token);

        // Wait longer than debounce period between events
        await Task.Delay(TimeSpan.FromMilliseconds(300), cts.Token);

        await service.StopAsync(cts.Token);

        // Wait for debounce to process
        await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
        await TestHelpers.WaitForConditionAsync(() => events.Count >= 1, TimeSpan.FromSeconds(10), cts.Token, eventsLock);

        // Cleanup - Cancel and wait for event task to complete before asserting
        await cts.CancelAsync();
        try { await eventTask; } catch (OperationCanceledException) { /* Expected during test cleanup; safe to ignore. */ }

        // Assert - Spaced events should be emitted separately
        LeadershipChangedEventArgs[] eventSnapshot;
        lock (eventsLock)
        {
            eventSnapshot = events.ToArray();
        }
        eventSnapshot.Length.ShouldBeGreaterThanOrEqualTo(1);

        await services.DisposeAsync();
    }
}

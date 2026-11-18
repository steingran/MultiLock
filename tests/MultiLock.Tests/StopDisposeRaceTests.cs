using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Tests that <see cref="LeaderElectionService.StopAsync"/> and disposal can run at the same time
/// without throwing. This is what happens at host shutdown, for example when a
/// <c>WebApplicationFactory</c> is disposed in an integration test (issue #171).
/// </summary>
/// <remarks>
/// These stress tests run many iterations and put load on the thread pool. They run in a
/// non-parallel collection so they do not slow down timing-sensitive tests in other classes.
/// </remarks>
[Collection(nameof(StopDisposeRaceTests))]
public class StopDisposeRaceTests
{
    // The unfixed code failed on the first iteration; 50 keeps the tests fast while still alternating order.
    private const int Iterations = 50;
    private static readonly TimeSpan IterationTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task StopAsync_AndDisposeAsync_Concurrently_ShouldNotThrow()
    {
        for (int i = 0; i < Iterations; i++)
        {
            LeaderElectionService service = await CreateLeaderServiceAsync();
            bool disposeFirst = i % 2 == 0;

            await RunConcurrentlyAsync(
                () => service.StopAsync(CancellationToken.None),
                () => service.DisposeAsync().AsTask(),
                disposeFirst);
        }
    }

    [Fact]
    public async Task StopAsync_AndSyncDispose_Concurrently_ShouldNotThrow()
    {
        for (int i = 0; i < Iterations; i++)
        {
            LeaderElectionService service = await CreateLeaderServiceAsync();
            bool disposeFirst = i % 2 == 0;

            await RunConcurrentlyAsync(
                () => service.StopAsync(CancellationToken.None),
                () =>
                {
                    service.Dispose();
                    return Task.CompletedTask;
                },
                disposeFirst);
        }
    }

    [Fact]
    public async Task StopAsync_AfterDispose_ShouldNotThrow()
    {
        LeaderElectionService service = await CreateLeaderServiceAsync();

        await service.DisposeAsync();

        await Should.NotThrowAsync(() => service.StopAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_ShouldWaitForInProgressStopAsync()
    {
        var provider = new SlowReleaseProvider(releaseDelay: TimeSpan.FromMilliseconds(200));
        LeaderElectionService service = CreateService(provider);
        (await service.TryAcquireLeadershipAsync()).ShouldBeTrue();

        Task stopTask = service.StopAsync(CancellationToken.None);
        await provider.ReleaseStarted.Task.WaitAsync(IterationTimeout);

        // StopAsync now holds the state lock and is inside ReleaseLeadershipAsync.
        await service.DisposeAsync().AsTask().WaitAsync(IterationTimeout);

        // DisposeAsync must not finish before StopAsync has finished releasing leadership.
        provider.ReleaseCount.ShouldBe(1);
        await stopTask.WaitAsync(IterationTimeout);
    }

    [Fact]
    public async Task PublicMethods_RacingDisposeAsync_ShouldOnlyThrowServiceObjectDisposedException()
    {
        for (int i = 0; i < Iterations; i++)
        {
            LeaderElectionService service = await CreateLeaderServiceAsync();
            bool disposeFirst = i % 2 == 0;

            Func<Task> operation = (i % 3) switch
            {
                0 => () => service.ReleaseLeadershipAsync(),
                1 => () => service.TryAcquireLeadershipAsync(),
                _ => () => service.StartAsync()
            };

            await RunConcurrentlyAsync(
                async () =>
                {
                    try
                    {
                        await operation();
                    }
                    catch (ObjectDisposedException ex)
                    {
                        // Calling a public method on a disposed service may throw, but the exception
                        // must be about the service, never about its internal SemaphoreSlim.
                        ex.ObjectName.ShouldBe(typeof(LeaderElectionService).FullName);
                    }
                },
                () => service.DisposeAsync().AsTask(),
                disposeFirst);
        }
    }

    private static async Task RunConcurrentlyAsync(Func<Task> stop, Func<Task> dispose, bool disposeFirst)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task first = Task.Run(async () =>
        {
            await gate.Task;
            await (disposeFirst ? dispose() : stop());
        });
        Task second = Task.Run(async () =>
        {
            await gate.Task;
            await (disposeFirst ? stop() : dispose());
        });

        gate.SetResult();

        // Awaiting WhenAll rethrows the first exception from either task.
        await Task.WhenAll(first, second).WaitAsync(IterationTimeout);
    }

    private static async Task<LeaderElectionService> CreateLeaderServiceAsync()
    {
        // A short delay in ReleaseLeadershipAsync keeps StopAsync inside the state lock
        // long enough for disposal to overlap with it.
        LeaderElectionService service = CreateService(new SlowReleaseProvider(TimeSpan.FromMilliseconds(1)));
        (await service.TryAcquireLeadershipAsync()).ShouldBeTrue();
        return service;
    }

    private static LeaderElectionService CreateService(ILeaderElectionProvider provider)
    {
        var options = new LeaderElectionOptions
        {
            ElectionGroup = "stop-dispose-race",
            // Long intervals so timer callbacks do not run during the test.
            HeartbeatInterval = TimeSpan.FromMinutes(5),
            HeartbeatTimeout = TimeSpan.FromMinutes(10),
            ElectionInterval = TimeSpan.FromMinutes(5),
            AutoStart = false
        };

        return new LeaderElectionService(
            provider,
            Options.Create(options),
            NullLogger<LeaderElectionService>.Instance);
    }

    /// <summary>
    /// A provider that always grants leadership and waits a short time in <see cref="ReleaseLeadershipAsync"/>.
    /// </summary>
    private sealed class SlowReleaseProvider(TimeSpan releaseDelay) : ILeaderElectionProvider
    {
        private int releaseCount;

        public TaskCompletionSource ReleaseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReleaseCount => Volatile.Read(ref releaseCount);

        public Task<bool> TryAcquireLeadershipAsync(
            string electionGroup,
            string participantId,
            IReadOnlyDictionary<string, string> metadata,
            TimeSpan lockTimeout,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public async Task ReleaseLeadershipAsync(
            string electionGroup,
            string participantId,
            CancellationToken cancellationToken = default)
        {
            ReleaseStarted.TrySetResult();
            await Task.Delay(releaseDelay, cancellationToken);
            Interlocked.Increment(ref releaseCount);
        }

        public Task<bool> UpdateHeartbeatAsync(
            string electionGroup,
            string participantId,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<LeaderInfo?> GetCurrentLeaderAsync(
            string electionGroup,
            CancellationToken cancellationToken = default) => Task.FromResult<LeaderInfo?>(null);

        public Task<bool> IsLeaderAsync(
            string electionGroup,
            string participantId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Runs <see cref="StopDisposeRaceTests"/> on its own, with no other test collections in parallel.
/// </summary>
[CollectionDefinition(nameof(StopDisposeRaceTests), DisableParallelization = true)]
public class StopDisposeRaceTestsCollection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Comprehensive tests for concurrent scenarios in leader election.
/// Tests multiple participants competing, rapid operations, and race conditions.
/// </summary>
public class ConcurrentScenarioTests
{
    [Fact]
    public async Task HighConcurrency_10Participants_ShouldElectExactlyOneLeader()
    {
        // Arrange
        const int participantCount = 10;
        var services = new List<ILeaderElectionService>();
        var serviceProviders = new List<ServiceProvider>();
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        for (int i = 0; i < participantCount; i++)
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            serviceCollection.AddSingleton<ILeaderElectionProvider>(provider);

            int index = i;
            serviceCollection.Configure<LeaderElectionOptions>(options =>
            {
                options.ParticipantId = $"participant-{index}";
                options.ElectionGroup = "high-concurrency-test";
                options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
                options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
                options.ElectionInterval = TimeSpan.FromMilliseconds(50);
                options.AutoStart = false;
            });
            serviceCollection.AddSingleton<ILeaderElectionService, LeaderElectionService>();

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
            serviceProviders.Add(serviceProvider);
            ILeaderElectionService service = serviceProvider.GetRequiredService<ILeaderElectionService>();
            services.Add(service);
        }

        try
        {
            // Act - Start all participants simultaneously
            await Task.WhenAll(services.Select(s => s.StartAsync()));

            // Wait for election to stabilize
            await TestHelpers.WaitForConditionAsync(
                () => services.Count(s => s.IsLeader) == 1,
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            // Assert - Exactly one leader
            var leaders = services.Where(s => s.IsLeader).ToList();
            leaders.Count.ShouldBe(1, "exactly one participant should be elected as leader");

            // Verify all participants agree on the same leader
            LeaderInfo? leaderInfo = await services[0].GetCurrentLeaderAsync();
            leaderInfo.ShouldNotBeNull();

            await Task.WhenAll(services.Select(async service =>
            {
                LeaderInfo? info = await service.GetCurrentLeaderAsync();
                info.ShouldNotBeNull();
                info.LeaderId.ShouldBe(leaderInfo.LeaderId, "all participants should agree on the same leader");
            }));
        }
        finally
        {
            await Task.WhenAll(services.Select(s => s.StopAsync()));

            foreach (ILeaderElectionService service in services)
                service.Dispose();

            foreach (ServiceProvider serviceProvider in serviceProviders)
                await serviceProvider.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentAcquireAttempts_20Participants_OnlyOneSucceeds()
    {
        // Arrange
        const int participantCount = 20;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        var metadata = new Dictionary<string, string> { { "test", "concurrent" } };
        bool[] acquireResults = new bool[participantCount];

        // Act - All participants try to acquire leadership simultaneously
        Task[] acquireTasks = Enumerable.Range(0, participantCount).Select(async i =>
        {
            bool result = await provider.TryAcquireLeadershipAsync(
                "concurrent-acquire-test",
                $"participant-{i}",
                metadata,
                TimeSpan.FromMinutes(1));
            acquireResults[i] = result;
        }).ToArray();

        await Task.WhenAll(acquireTasks);

        // Assert - Exactly one should succeed
        int successCount = acquireResults.Count(r => r);
        successCount.ShouldBe(1, "exactly one participant should successfully acquire leadership");

        // Verify the leader is set
        LeaderInfo? leaderInfo = await provider.GetCurrentLeaderAsync("concurrent-acquire-test");
        leaderInfo.ShouldNotBeNull();
        leaderInfo.LeaderId.ShouldStartWith("participant-");
    }

    [Fact]
    public async Task ConcurrentAcquireAndRelease_ShouldMaintainConsistency()
    {
        // Arrange
        const int iterations = 50;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        var metadata = new Dictionary<string, string> { { "test", "acquire-release" } };
        int successfulAcquisitions = 0;
        int failedAcquisitions = 0;
        object lockObj = new object();

        // Act - Multiple participants rapidly try to acquire and release
        Task[] tasks = Enumerable.Range(0, 5).Select(async participantIndex =>
        {
            for (int i = 0; i < iterations; i++)
            {
                bool acquired = await provider.TryAcquireLeadershipAsync(
                    "acquire-release-test",
                    $"participant-{participantIndex}",
                    metadata,
                    TimeSpan.FromMinutes(1));

                if (acquired)
                {
                    lock (lockObj)
                    {
                        successfulAcquisitions++;
                    }

                    // Hold leadership briefly
                    await Task.Delay(10);

                    await provider.ReleaseLeadershipAsync(
                        "acquire-release-test",
                        $"participant-{participantIndex}");
                }
                else
                {
                    lock (lockObj)
                    {
                        failedAcquisitions++;
                    }
                }

                // Small delay between attempts
                await Task.Delay(5);
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        // Assert - Should have many successful acquisitions and releases
        successfulAcquisitions.ShouldBeGreaterThan(0, "at least some acquisitions should succeed");
        failedAcquisitions.ShouldBeGreaterThan(0, "at least some acquisitions should fail due to contention");

        // Final state should have no leader (all released)
        LeaderInfo? finalLeader = await provider.GetCurrentLeaderAsync("acquire-release-test");
        finalLeader.ShouldBeNull("no leader should remain after all releases");
    }

    [Fact]
    public async Task ConcurrentIsLeaderChecks_ShouldBeThreadSafe()
    {
        // Arrange
        const int checkCount = 100;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        var metadata = new Dictionary<string, string> { { "test", "is-leader" } };

        // Acquire leadership with one participant
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "is-leader-test",
            "leader-participant",
            metadata,
            TimeSpan.FromMinutes(1));
        acquired.ShouldBeTrue();

        // Act - Multiple threads check IsLeader concurrently
        Task[] checkTasks = Enumerable.Range(0, checkCount).Select(async i =>
        {
            bool isLeader = await provider.IsLeaderAsync("is-leader-test", "leader-participant");
            isLeader.ShouldBeTrue("leader should always be identified as leader");

            bool isNotLeader = await provider.IsLeaderAsync("is-leader-test", $"non-leader-{i}");
            isNotLeader.ShouldBeFalse("non-leaders should not be identified as leaders");
        }).ToArray();

        await Task.WhenAll(checkTasks);

        // Assert - All checks completed successfully (verified in the tasks above)
    }

    [Fact]
    public async Task ConcurrentGetCurrentLeader_ShouldReturnConsistentResults()
    {
        // Arrange
        const int queryCount = 100;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        var metadata = new Dictionary<string, string> { { "test", "get-leader" } };

        // Acquire leadership
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "get-leader-test",
            "the-leader",
            metadata,
            TimeSpan.FromMinutes(1));
        acquired.ShouldBeTrue();

        // Act - Multiple threads query current leader concurrently
        Task<LeaderInfo?>[] queryTasks = Enumerable.Range(0, queryCount)
            .Select(_ => provider.GetCurrentLeaderAsync("get-leader-test"))
            .ToArray();

        LeaderInfo?[] results = await Task.WhenAll(queryTasks);

        // Assert - All queries should return the same leader
        results.ShouldAllBe(info => info != null && info.LeaderId == "the-leader",
            "all concurrent queries should return the same leader");
    }

    [Fact]
    public async Task RapidAcquireReleaseCycles_100Iterations_ShouldNotCauseRaceConditions()
    {
        // Arrange
        const int iterations = 100;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        var metadata = new Dictionary<string, string> { { "iteration", "0" } };

        // Act - Rapidly acquire and release in a tight loop
        for (int i = 0; i < iterations; i++)
        {
            metadata["iteration"] = i.ToString();

            bool acquired = await provider.TryAcquireLeadershipAsync(
                "rapid-cycle-test",
                "rapid-participant",
                metadata,
                TimeSpan.FromMinutes(1));

            acquired.ShouldBeTrue($"acquisition {i} should succeed");

            // Verify we're the leader
            bool isLeader = await provider.IsLeaderAsync("rapid-cycle-test", "rapid-participant");
            isLeader.ShouldBeTrue($"should be leader after acquisition {i}");

            // Release immediately
            await provider.ReleaseLeadershipAsync("rapid-cycle-test", "rapid-participant");

            // Verify we're no longer the leader
            LeaderInfo? leaderInfo = await provider.GetCurrentLeaderAsync("rapid-cycle-test");
            leaderInfo.ShouldBeNull($"no leader should exist after release {i}");
        }

        // Assert - Final state should be clean
        LeaderInfo? finalLeader = await provider.GetCurrentLeaderAsync("rapid-cycle-test");
        finalLeader.ShouldBeNull("no leader should remain after all cycles");
    }

    [Fact]
    public async Task ConcurrentHeartbeatUpdates_ShouldBeThreadSafe()
    {
        // Arrange
        const int updateCount = 50;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        var metadata = new Dictionary<string, string> { { "heartbeat", "initial" } };

        // Acquire leadership
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "heartbeat-test",
            "heartbeat-leader",
            metadata,
            TimeSpan.FromMinutes(1));
        acquired.ShouldBeTrue();

        // Act - Multiple threads update heartbeat concurrently
        Task[] updateTasks = Enumerable.Range(0, updateCount).Select(async i =>
        {
            var updateMetadata = new Dictionary<string, string> { { "heartbeat", $"update-{i}" } };
            await provider.UpdateHeartbeatAsync("heartbeat-test", "heartbeat-leader", updateMetadata);
        }).ToArray();

        await Task.WhenAll(updateTasks);

        // Assert - Leader should still exist and be valid
        bool isLeader = await provider.IsLeaderAsync("heartbeat-test", "heartbeat-leader");
        isLeader.ShouldBeTrue("leader should still be valid after concurrent heartbeat updates");

        LeaderInfo? leaderInfo = await provider.GetCurrentLeaderAsync("heartbeat-test");
        leaderInfo.ShouldNotBeNull();
        leaderInfo.LeaderId.ShouldBe("heartbeat-leader");
    }

    [Fact]
    public async Task ConcurrentMetadataUpdates_ShouldNotCorruptData()
    {
        // Arrange
        const int updateCount = 100;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        var metadata = new Dictionary<string, string> { { "counter", "0" } };

        // Acquire leadership
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "metadata-test",
            "metadata-leader",
            metadata,
            TimeSpan.FromMinutes(1));
        acquired.ShouldBeTrue();

        // Act - Multiple threads update metadata concurrently
        Task[] updateTasks = Enumerable.Range(0, updateCount).Select(async i =>
        {
            var updateMetadata = new Dictionary<string, string>
            {
                { "counter", i.ToString() },
                { "timestamp", DateTime.UtcNow.Ticks.ToString() },
                { $"key-{i}", $"value-{i}" }
            };
            await provider.UpdateHeartbeatAsync("metadata-test", "metadata-leader", updateMetadata);
        }).ToArray();

        await Task.WhenAll(updateTasks);

        // Assert - Leader should still exist with valid metadata
        LeaderInfo? leaderInfo = await provider.GetCurrentLeaderAsync("metadata-test");
        leaderInfo.ShouldNotBeNull();
        leaderInfo.LeaderId.ShouldBe("metadata-leader");
        leaderInfo.Metadata.ShouldNotBeNull();
        leaderInfo.Metadata.ShouldNotBeEmpty();
    }

    // Leader Failover Race Condition Tests

    [Fact]
    public async Task LeaderFailover_MultipleWaitingParticipants_ShouldElectExactlyOneNewLeader()
    {
        // Arrange
        const int waitingParticipantCount = 5;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        // Leader acquires leadership
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "failover-test",
            "leader",
            new Dictionary<string, string>(),
            TimeSpan.FromSeconds(1));
        acquired.ShouldBeTrue();

        // Act - Leader releases (simulating failure), multiple participants try to take over
        await provider.ReleaseLeadershipAsync("failover-test", "leader");

        var acquireTasks = Enumerable.Range(0, waitingParticipantCount).Select(async i =>
        {
            string participantId = $"participant{i}";
            bool participantAcquired = await provider.TryAcquireLeadershipAsync(
                "failover-test",
                participantId,
                new Dictionary<string, string>(),
                TimeSpan.FromSeconds(1));
            return new { ParticipantId = participantId, Acquired = participantAcquired };
        }).ToArray();

        var results = await Task.WhenAll(acquireTasks);

        // Assert - Exactly one participant should become leader
        int successCount = results.Count(r => r.Acquired);
        successCount.ShouldBe(1, "exactly one participant should acquire leadership");

        LeaderInfo? newLeader = await provider.GetCurrentLeaderAsync("failover-test");
        newLeader.ShouldNotBeNull();
        results.Single(r => r.Acquired).ParticipantId.ShouldBe(newLeader.LeaderId);
    }

    [Fact]
    public async Task LeaderFailover_SimultaneousFailureAndAcquire_ShouldNotCauseSplitBrain()
    {
        // Arrange
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        // Leader acquires leadership
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "split-brain-test",
            "leader",
            new Dictionary<string, string>(),
            TimeSpan.FromSeconds(2));
        acquired.ShouldBeTrue();

        // Act - Simulate race: leader releases while others try to acquire
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(10); // Small delay to create race condition
            await provider.ReleaseLeadershipAsync("split-brain-test", "leader");
        });

        var acquireTasks = Enumerable.Range(0, 10).Select(async i =>
        {
            string participantId = $"participant{i}";
            await Task.Delay(Random.Shared.Next(0, 50)); // Random delays
            bool participantAcquired = await provider.TryAcquireLeadershipAsync(
                "split-brain-test",
                participantId,
                new Dictionary<string, string>(),
                TimeSpan.FromSeconds(2));
            return new { ParticipantId = participantId, Acquired = participantAcquired };
        }).ToArray();

        await Task.WhenAll(acquireTasks.Concat(new[] { releaseTask }));
        var results = await Task.WhenAll(acquireTasks);

        // Assert - At most one participant should be leader (no split-brain)
        int successCount = results.Count(r => r.Acquired);
        successCount.ShouldBeLessThanOrEqualTo(1, "at most one participant should acquire leadership");

        // Verify current leader state is consistent
        LeaderInfo? currentLeader = await provider.GetCurrentLeaderAsync("split-brain-test");
        if (successCount == 1)
        {
            currentLeader.ShouldNotBeNull();
            results.Single(r => r.Acquired).ParticipantId.ShouldBe(currentLeader.LeaderId);
        }
        else
        {
            currentLeader.ShouldBeNull();
        }
    }

    [Fact]
    public async Task LeaderFailover_RapidLeaderChanges_ShouldMaintainConsistency()
    {
        // Arrange
        const int iterationCount = 20;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        // Act - Simulate rapid leader changes
        for (int i = 0; i < iterationCount; i++)
        {
            string leaderId = $"leader{i}";

            // Acquire leadership
            bool acquired = await provider.TryAcquireLeadershipAsync(
                "rapid-failover-test",
                leaderId,
                new Dictionary<string, string>(),
                TimeSpan.FromSeconds(5));
            acquired.ShouldBeTrue($"leader{i} should acquire leadership");

            // Verify leadership
            bool isLeader = await provider.IsLeaderAsync("rapid-failover-test", leaderId);
            isLeader.ShouldBeTrue($"leader{i} should be the leader");

            // Release immediately
            await provider.ReleaseLeadershipAsync("rapid-failover-test", leaderId);

            // Verify release
            LeaderInfo? currentLeader = await provider.GetCurrentLeaderAsync("rapid-failover-test");
            currentLeader.ShouldBeNull($"no leader should exist after leader{i} releases");
        }

        // Assert - Final state should be consistent (no leader)
        LeaderInfo? finalLeader = await provider.GetCurrentLeaderAsync("rapid-failover-test");
        finalLeader.ShouldBeNull("no leader should remain after all rapid changes");
    }

    [Fact]
    public async Task LeaderFailover_ConcurrentFailoverAttempts_ShouldBeThreadSafe()
    {
        // Arrange
        const int cycleCount = 10;
        using var loggerFactory = new LoggerFactory();
        var provider = new InMemoryLeaderElectionProvider(
            loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>());

        // Act - Multiple cycles of leader acquisition and release
        Task<bool>[] tasks = Enumerable.Range(0, cycleCount).Select(async cycle =>
        {
            string leaderId = $"leader{cycle}";

            // Try to acquire
            bool acquired = await provider.TryAcquireLeadershipAsync(
                "concurrent-failover-test",
                leaderId,
                new Dictionary<string, string>(),
                TimeSpan.FromSeconds(2));

            if (acquired)
            {
                // Hold leadership briefly
                await Task.Delay(Random.Shared.Next(10, 50));

                // Release
                await provider.ReleaseLeadershipAsync("concurrent-failover-test", leaderId);
            }

            return acquired;
        }).ToArray();

        bool[] results = await Task.WhenAll(tasks);

        // Assert - At least one participant should have acquired leadership
        int successCount = results.Count(r => r);
        successCount.ShouldBeGreaterThan(0, "at least one participant should acquire leadership");

        // Final state should be consistent (no leader after all releases)
        LeaderInfo? finalLeader = await provider.GetCurrentLeaderAsync("concurrent-failover-test");
        finalLeader.ShouldBeNull("no leader should remain after all releases");
    }
}


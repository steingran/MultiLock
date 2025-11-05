using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiLock.FileSystem;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Integration tests that simulate multiple instances competing for leadership.
/// </summary>
public class MultiInstanceLeaderElectionTests
{
    [Fact]
    public async Task MultipleInstances_WithInMemoryProvider_ShouldElectOneLeader()
    {
        // Arrange
        const int instanceCount = 3;
        var services = new List<ILeaderElectionService>();
        var leadershipEvents = new List<(int InstanceId, bool IsLeader, DateTime Timestamp)>();
        var eventTasks = new List<Task>();
        var cts = new CancellationTokenSource();

        try
        {
            // Create multiple instances with the same in-memory provider
            var provider = new InMemoryLeaderElectionProvider(
                new LoggerFactory().CreateLogger<InMemoryLeaderElectionProvider>());

            for (int i = 0; i < instanceCount; i++)
            {
                var serviceCollection = new ServiceCollection();
                serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
                serviceCollection.AddSingleton<ILeaderElectionProvider>(provider);
                int index = i;
                serviceCollection.Configure<LeaderElectionOptions>(options =>
                {
                    options.ParticipantId = $"instance-{index}";
                    options.ElectionGroup = "test-group";
                    options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
                    options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
                    options.ElectionInterval = TimeSpan.FromMilliseconds(50);
                    options.AutoStart = false;
                });
                serviceCollection.AddSingleton<ILeaderElectionService, LeaderElectionService>();

                ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
                ILeaderElectionService service = serviceProvider.GetRequiredService<ILeaderElectionService>();

                int instanceId = i;
                // Subscribe to leadership changes using AsyncEnumerable
                CancellationToken cancellationToken = cts.Token;
                var eventTask = Task.Run(async () =>
                {
                    await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken))
                    {
                        lock (leadershipEvents)
                        {
                            leadershipEvents.Add((instanceId, e.CurrentStatus.IsLeader, DateTime.UtcNow));
                        }
                    }
                }, cancellationToken);
                eventTasks.Add(eventTask);

                services.Add(service);
            }

            // Give event subscriptions time to start
            await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);

            // Act - Start all instances
            Task[] startTasks = services.Select(s => s.StartAsync(cts.Token)).ToArray();
            await Task.WhenAll(startTasks);

            // Wait for election to stabilize - wait for exactly one leader
            await TestHelpers.WaitForConditionAsync(
                () => services.Count(s => s.IsLeader) == 1,
                TimeSpan.FromSeconds(5),
                cts.Token);

            // Wait for leadership events to be received
            await TestHelpers.WaitForConditionAsync(
                () =>
                {
                    lock (leadershipEvents)
                    {
                        return leadershipEvents.Any(e => e.IsLeader);
                    }
                },
                TimeSpan.FromSeconds(3),
                cts.Token,
                leadershipEvents);

            // Assert - Exactly one leader should be elected
            var currentLeaders = services.Where(s => s.IsLeader).ToList();
            currentLeaders.Count.ShouldBe(1, "exactly one instance should be the leader");

            // Verify all instances know about the same leader
            LeaderInfo? leaderInfo = await services[0].GetCurrentLeaderAsync(cts.Token);
            leaderInfo.ShouldNotBeNull();

            foreach (ILeaderElectionService service in services)
            {
                LeaderInfo? info = await service.GetCurrentLeaderAsync(cts.Token);
                info.ShouldNotBeNull();
                info.LeaderId.ShouldBe(leaderInfo.LeaderId);
            }

            // Verify leadership events were fired
            lock (leadershipEvents)
            {
                leadershipEvents.ShouldNotBeEmpty();
                leadershipEvents.Count(e => e.IsLeader).ShouldBe(1, "only one instance should have become leader");
            }
        }
        finally
        {
            // Cancel event subscriptions
            await cts.CancelAsync();

            // Wait for event tasks to complete
            foreach (Task eventTask in eventTasks)
            {
                try
                {
                    await eventTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation token is canceled
                }
            }

            // Cleanup
            // ReSharper disable once MethodSupportsCancellation
            Task[] stopTasks = services.Select(s => s.StopAsync()).ToArray();
            await Task.WhenAll(stopTasks);

            foreach (ILeaderElectionService service in services)
            {
                service.Dispose();
            }

            cts.Dispose();
        }
    }

    [Fact]
    public async Task LeaderFailover_ShouldElectNewLeader()
    {
        // Arrange
        var services = new List<ILeaderElectionService>();
        var leadershipChanges = new List<string>();
        var eventTasks = new List<Task>();
        var cts = new CancellationTokenSource();
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            // Create multiple instances with file system provider
            Directory.CreateDirectory(tempDir);

            for (int i = 0; i < 3; i++)
            {
                var serviceCollection = new ServiceCollection();
                serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
                serviceCollection.Configure<FileSystemLeaderElectionOptions>(options =>
                {
                    options.DirectoryPath = tempDir;
                });
                int index = i;
                serviceCollection.Configure<LeaderElectionOptions>(options =>
                {
                    options.ParticipantId = $"instance-{index}";
                    options.ElectionGroup = "failover-test";
                    options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
                    options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
                    options.ElectionInterval = TimeSpan.FromMilliseconds(50);
                    options.AutoStart = false;
                });
                serviceCollection.AddSingleton<ILeaderElectionProvider, FileSystemLeaderElectionProvider>();
                serviceCollection.AddSingleton<ILeaderElectionService, LeaderElectionService>();

                ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
                ILeaderElectionService service = serviceProvider.GetRequiredService<ILeaderElectionService>();

                // Subscribe to leadership changes using AsyncEnumerable
                CancellationToken cancellationToken = cts.Token;
                var eventTask = Task.Run(async () =>
                {
                    await foreach (LeadershipChangedEventArgs e in service.GetLeadershipChangesAsync(cancellationToken))
                    {
                        if (e.BecameLeader)
                        {
                            lock (leadershipChanges)
                            {
                                leadershipChanges.Add($"{service.ParticipantId}-acquired");
                            }
                        }
                        else if (e.LostLeadership)
                        {
                            lock (leadershipChanges)
                            {
                                leadershipChanges.Add($"{service.ParticipantId}-lost");
                            }
                        }
                    }
                }, cancellationToken);
                eventTasks.Add(eventTask);

                services.Add(service);
            }

            // Act - Start all instances
            Task[] startTasks = services.Select(s => s.StartAsync(cts.Token)).ToArray();
            await Task.WhenAll(startTasks);

            // Wait for initial election - wait for exactly one leader
            await TestHelpers.WaitForConditionAsync(
                () => services.Count(s => s.IsLeader) == 1,
                TimeSpan.FromSeconds(5),
                cts.Token);

            // Find the current leader
            ILeaderElectionService? initialLeader = services.FirstOrDefault(s => s.IsLeader);
            initialLeader.ShouldNotBeNull("a leader should be elected");

            // Stop the leader to simulate failure
            await initialLeader.StopAsync(cts.Token);

            // Wait for failover - wait for a new leader to be elected (excluding the stopped one)
            await TestHelpers.WaitForConditionAsync(
                () => services.Count(s => s.IsLeader && s != initialLeader) == 1,
                TimeSpan.FromSeconds(5),
                cts.Token);

            // Wait for leadership change events to be processed
            await TestHelpers.WaitForConditionAsync(
                () =>
                {
                    lock (leadershipChanges)
                    {
                        return leadershipChanges.Any(c => c.StartsWith(initialLeader.ParticipantId) && c.EndsWith("-lost"));
                    }
                },
                TimeSpan.FromSeconds(5),
                cts.Token,
                leadershipChanges);

            // Assert - A new leader should be elected
            var newLeaders = services.Where(s => s.IsLeader && s != initialLeader).ToList();
            newLeaders.Count.ShouldBe(1, "exactly one new leader should be elected after failover");

            // Verify leadership changes were recorded
            lock (leadershipChanges)
            {
                leadershipChanges.ShouldContain(c => c.EndsWith("-acquired"), "at least one instance should have acquired leadership");
                leadershipChanges.ShouldContain(c => c.StartsWith(initialLeader.ParticipantId) && c.EndsWith("-lost"),
                    "the original leader should have lost leadership");
            }
        }
        finally
        {
            // Cancel event subscriptions
            await cts.CancelAsync();

            // Wait for event tasks to complete
            foreach (Task eventTask in eventTasks)
            {
                try
                {
                    await eventTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation token is canceled
                }
            }

            // Cleanup
            // ReSharper disable once MethodSupportsCancellation
            Task[] stopTasks = services.Select(s => s.StopAsync()).ToArray();
            await Task.WhenAll(stopTasks);

            foreach (ILeaderElectionService service in services)
            {
                service.Dispose();
            }

            cts.Dispose();

            // Cleanup temp directory after all services are disposed
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (IOException ex)
                {
                    // Log but don't fail the test if cleanup fails
                    Console.WriteLine($"Warning: Failed to delete temp directory {tempDir}: {ex.Message}");
                }
            }
        }
    }

    [Fact]
    public async Task ConcurrentElectionAttempts_ShouldResultInSingleLeader()
    {
        // Arrange
        const int instanceCount = 5;
        var services = new List<ILeaderElectionService>();

        try
        {
            // Create multiple instances
            var provider = new InMemoryLeaderElectionProvider(
                new LoggerFactory().CreateLogger<InMemoryLeaderElectionProvider>());

            for (int i = 0; i < instanceCount; i++)
            {
                var serviceCollection = new ServiceCollection();
                serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
                serviceCollection.AddSingleton<ILeaderElectionProvider>(provider);

                int index = i;
                serviceCollection.Configure<LeaderElectionOptions>(options =>
                {
                    options.ParticipantId = $"concurrent-instance-{index}";
                    options.ElectionGroup = "concurrent-test";
                    options.HeartbeatInterval = TimeSpan.FromMilliseconds(200);
                    options.HeartbeatTimeout = TimeSpan.FromMilliseconds(600);
                    options.ElectionInterval = TimeSpan.FromMilliseconds(100);
                    options.AutoStart = false;
                });
                serviceCollection.AddSingleton<ILeaderElectionService, LeaderElectionService>();

                ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
                ILeaderElectionService service = serviceProvider.GetRequiredService<ILeaderElectionService>();
                services.Add(service);
            }

            // Act - Start all instances simultaneously
            Task<bool>[] startTasks = services.Select(async service =>
            {
                await service.StartAsync();
                // Immediately try to acquire leadership
                return await service.TryAcquireLeadershipAsync();
            }).ToArray();

            bool[] results = await Task.WhenAll(startTasks);

            // Wait for stabilization - wait for exactly one leader
            await TestHelpers.WaitForConditionAsync(
                () => services.Count(s => s.IsLeader) == 1,
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            // Assert - Only one instance should have successfully acquired leadership
            int successfulAcquisitions = results.Count(r => r);
            successfulAcquisitions.ShouldBeLessThanOrEqualTo(1, "at most one instance should successfully acquire leadership initially");

            // Verify final state has exactly one leader
            var currentLeaders = services.Where(s => s.IsLeader).ToList();
            currentLeaders.Count.ShouldBe(1, "exactly one instance should be the leader after stabilization");
        }
        finally
        {
            // Cleanup
            Task[] stopTasks = services.Select(s => s.StopAsync()).ToArray();
            await Task.WhenAll(stopTasks);

            foreach (ILeaderElectionService service in services)
            {
                service.Dispose();
            }
        }
    }
}

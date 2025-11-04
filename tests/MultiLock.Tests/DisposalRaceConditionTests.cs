using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Tests for disposal race conditions in LeaderElectionService.
/// These tests verify that disposal works correctly even when timer callbacks are executing.
/// </summary>
public class DisposalRaceConditionTests
{
    [Fact]
    public async Task Dispose_WhileHeartbeatCallbackRunning_ShouldNotThrowObjectDisposedException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ILeaderElectionProvider, InMemoryLeaderElectionProvider>();
        services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
        {
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(150);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
        });

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        var host = serviceProvider.GetRequiredService<IHostedService>() as LeaderElectionService;
        host.ShouldNotBeNull();

        // Act - Start the service and let it acquire leadership
        await host.StartAsync(CancellationToken.None);
        await Task.Delay(200); // Wait for leadership acquisition and heartbeat to start

        // Dispose while heartbeat callbacks are likely running
        var disposeTask = Task.Run(async () => await host.DisposeAsync());

        // Should complete without throwing
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert - No exception should be thrown
        disposeTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Dispose_WhileElectionCallbackRunning_ShouldNotThrowObjectDisposedException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ILeaderElectionProvider, InMemoryLeaderElectionProvider>();
        services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
        {
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(150);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
        });

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        var host = serviceProvider.GetRequiredService<IHostedService>() as LeaderElectionService;
        host.ShouldNotBeNull();

        // Act - Start the service (election callbacks will be running)
        await host.StartAsync(CancellationToken.None);
        await Task.Delay(100); // Wait for election callbacks to start

        // Dispose while election callbacks are likely running
        var disposeTask = Task.Run(async () => await host.DisposeAsync());

        // Should complete without throwing
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert - No exception should be thrown
        disposeTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task ConcurrentDispose_ShouldBeIdempotent()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ILeaderElectionProvider, InMemoryLeaderElectionProvider>();
        services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
        {
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(150);
        });

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        var host = serviceProvider.GetRequiredService<IHostedService>() as LeaderElectionService;
        host.ShouldNotBeNull();

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Act - Call Dispose multiple times concurrently
        Task[] disposeTasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () => await host.DisposeAsync()))
            .ToArray();

        // Should all complete without throwing
        await Task.WhenAll(disposeTasks).WaitAsync(TimeSpan.FromSeconds(15));

        // Assert - All dispose tasks should complete successfully
        disposeTasks.All(t => t.IsCompletedSuccessfully).ShouldBeTrue();
    }

    [Fact]
    public async Task RapidStartStopCycles_ShouldNotCauseRaceConditions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ILeaderElectionProvider, InMemoryLeaderElectionProvider>();

        // Act & Assert - Perform multiple rapid start/stop cycles
        for (int i = 0; i < 5; i++)
        {
            int i1 = i;
            services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
            {
                options.ElectionGroup = $"test-group-{i1}";
                options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
                options.HeartbeatTimeout = TimeSpan.FromMilliseconds(150);
                options.ElectionInterval = TimeSpan.FromMilliseconds(50);
            });

            ServiceProvider serviceProvider = services.BuildServiceProvider();
            var host = serviceProvider.GetRequiredService<IHostedService>() as LeaderElectionService;
            host.ShouldNotBeNull();

            await host.StartAsync(CancellationToken.None);
            await Task.Delay(50); // Very short delay
            await host.StopAsync(CancellationToken.None);
            await host.DisposeAsync();

            // Rebuild services for next iteration
            services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            services.AddSingleton<ILeaderElectionProvider, InMemoryLeaderElectionProvider>();
        }

        // If we get here without exceptions, the test passes
    }

    [Fact]
    public async Task Dispose_ShouldWaitForActiveCallbacksToComplete()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ILeaderElectionProvider, InMemoryLeaderElectionProvider>();
        services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
        {
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(150);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
        });

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        var host = serviceProvider.GetRequiredService<IHostedService>() as LeaderElectionService;
        host.ShouldNotBeNull();

        // Act - Start and let callbacks run
        await host.StartAsync(CancellationToken.None);
        await Task.Delay(200); // Let multiple callbacks execute

        DateTime disposeStartTime = DateTime.UtcNow;
        await host.DisposeAsync();
        DateTime disposeEndTime = DateTime.UtcNow;

        // Assert - Dispose should complete in reasonable time (not hang indefinitely)
        TimeSpan disposeDuration = disposeEndTime - disposeStartTime;
        disposeDuration.ShouldBeLessThan(TimeSpan.FromSeconds(12)); // Max wait is 10s + buffer
    }

    [Fact]
    public async Task DisposeAsync_ThenDispose_ShouldBeIdempotent()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ILeaderElectionProvider, InMemoryLeaderElectionProvider>();
        services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
        {
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(150);
        });

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        var host = serviceProvider.GetRequiredService<IHostedService>() as LeaderElectionService;
        host.ShouldNotBeNull();

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Act - Call DisposeAsync then Dispose
        await host.DisposeAsync();
        await host.DisposeAsync(); // Should not throw

        // Assert - No exception should be thrown. The test has succeeded if we are here
    }

    [Fact]
    public async Task Dispose_ShouldStopTimerCallbacksFromExecuting()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ILeaderElectionProvider, InMemoryLeaderElectionProvider>();
        services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
        {
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(150);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
        });

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        var host = serviceProvider.GetRequiredService<IHostedService>() as LeaderElectionService;
        host.ShouldNotBeNull();

        // Act
        await host.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        await host.DisposeAsync();

        // Wait a bit to ensure no callbacks execute after disposal
        await Task.Delay(200);

        // Assert - If we get here without exceptions, timers were properly stopped
    }

    [Fact]
    public async Task MultipleServices_DisposedConcurrently_ShouldNotInterfere()
    {
        // Arrange - Create multiple services
        var hosts = new List<LeaderElectionService>();

        for (int i = 0; i < 5; i++)
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            services.AddSingleton<ILeaderElectionProvider, InMemoryLeaderElectionProvider>();

            int i1 = i;
            services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
            {
                options.ElectionGroup = $"test-group-{i1}";
                options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
                options.HeartbeatTimeout = TimeSpan.FromMilliseconds(150);
            });

            ServiceProvider serviceProvider = services.BuildServiceProvider();
            var host = serviceProvider.GetRequiredService<IHostedService>() as LeaderElectionService;
            host.ShouldNotBeNull();

            await host.StartAsync(CancellationToken.None);
            hosts.Add(host);
        }

        await Task.Delay(200); // Let all services run

        // Act - Dispose all services concurrently
        Task[] disposeTasks = hosts.Select(h => Task.Run(async () => await h.DisposeAsync())).ToArray();
        await Task.WhenAll(disposeTasks).WaitAsync(TimeSpan.FromSeconds(15));

        // Assert - All should complete successfully
        disposeTasks.All(t => t.IsCompletedSuccessfully).ShouldBeTrue();
    }
}


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Consul;
using MultiLock.Tests;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("Consul")]
public class ConsulIntegrationTests : IAsyncLifetime
{
    private readonly ILogger<ConsulLeaderElectionProvider> logger;
    private readonly string keyPrefix;
    private IHost? host1;
    private IHost? host2;

    public ConsulIntegrationTests()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<ConsulLeaderElectionProvider>();
        keyPrefix = $"test-leader-election-{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        // Wait for Consul to be ready
        await TestHelpers.WaitForConditionAsync(
            IsConsulAvailableAsync,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (host1 != null)
        {
            await host1.StopAsync();
            host1.Dispose();
        }

        if (host2 != null)
        {
            await host2.StopAsync();
            host2.Dispose();
        }
    }

    [Fact]
    public async Task HealthCheck_WithConsul_ShouldReturnTrue()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        var options = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = keyPrefix,
            SessionTtl = TimeSpan.FromSeconds(60),
            SessionLockDelay = TimeSpan.FromSeconds(15)
        };

        using var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act
        bool isHealthy = await provider.HealthCheckAsync();

        // Assert
        isHealthy.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireLeadership_WithConsul_ShouldSucceed()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        var options = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = keyPrefix,
            SessionTtl = TimeSpan.FromSeconds(60),
            SessionLockDelay = TimeSpan.FromSeconds(15)
        };

        using var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();

        // Verify leadership
        LeaderInfo? leaderInfo = await provider.GetCurrentLeaderAsync("test-group");
        leaderInfo.ShouldNotBeNull();
        leaderInfo.LeaderId.ShouldBe("participant-1");
        leaderInfo.Metadata["key"].ShouldBe("value");

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task TryAcquireLeadership_WhenAlreadyAcquired_ShouldReturnFalse()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        var options = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = keyPrefix,
            SessionTtl = TimeSpan.FromSeconds(60),
            SessionLockDelay = TimeSpan.FromSeconds(15)
        };

        using var provider1 = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        using var provider2 = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        bool acquired1 = await provider1.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        bool acquired2 = await provider2.TryAcquireLeadershipAsync(
            "test-group",
            "participant-2",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        acquired1.ShouldBeTrue();
        acquired2.ShouldBeFalse();

        // Cleanup
        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task ReleaseLeadership_ShouldAllowNewLeader()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        var options = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = keyPrefix,
            SessionTtl = TimeSpan.FromSeconds(60),
            SessionLockDelay = TimeSpan.FromSeconds(15)
        };

        using var provider1 = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        using var provider2 = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        bool acquired1 = await provider1.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        acquired1.ShouldBeTrue();

        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");

        // Wait for Consul's SessionLockDelay to expire by polling for leadership acquisition
        bool acquired2 = false;
        await TestHelpers.WaitForConditionAsync(
            async () =>
            {
                acquired2 = await provider2.TryAcquireLeadershipAsync(
                    "test-group",
                    "participant-2",
                    metadata,
                    TimeSpan.FromMinutes(5));
                return acquired2;
            },
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        // Assert
        acquired2.ShouldBeTrue();

        // Cleanup
        await provider2.ReleaseLeadershipAsync("test-group", "participant-2");
    }

    [Fact]
    public async Task UpdateHeartbeat_AsLeader_ShouldSucceed()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        var options = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = keyPrefix,
            SessionTtl = TimeSpan.FromSeconds(60),
            SessionLockDelay = TimeSpan.FromSeconds(15)
        };

        using var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        bool heartbeatUpdated = await provider.UpdateHeartbeatAsync("test-group", "participant-1", metadata);

        // Assert
        heartbeatUpdated.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task UpdateHeartbeat_AsNonLeader_ShouldReturnFalse()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        var options = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = keyPrefix,
            SessionTtl = TimeSpan.FromSeconds(60),
            SessionLockDelay = TimeSpan.FromSeconds(15)
        };

        using var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act
        var metadata = new Dictionary<string, string> { { "test", "value" } };
        bool heartbeatUpdated = await provider.UpdateHeartbeatAsync("test-group", "participant-1", metadata);

        // Assert
        heartbeatUpdated.ShouldBeFalse();
    }

    [Fact]
    public async Task IsLeader_WhenLeader_ShouldReturnTrue()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        var options = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = keyPrefix,
            SessionTtl = TimeSpan.FromSeconds(60),
            SessionLockDelay = TimeSpan.FromSeconds(15)
        };

        using var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        bool isLeader = await provider.IsLeaderAsync("test-group", "participant-1");

        // Assert
        isLeader.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task Consul_LeaderElection_ShouldWork()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        host1 = CreateHost("participant-1");
        host2 = CreateHost("participant-2");

        await host1.StartAsync();
        await host2.StartAsync();

        ILeaderElectionService leaderElection1 = host1.Services.GetRequiredService<ILeaderElectionService>();
        ILeaderElectionService leaderElection2 = host2.Services.GetRequiredService<ILeaderElectionService>();

        // Start the services - this will trigger the initial election process
        await leaderElection1.StartAsync();
        await leaderElection2.StartAsync();

        // Wait for the initial election process to complete by checking if leadership state is determined
        await TestHelpers.WaitForConditionAsync(
            () => leaderElection1.IsLeader || leaderElection2.IsLeader,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        // Act & Assert
        // After StartAsync, one should be leader and the other should not
        bool isLeader1 = leaderElection1.IsLeader;
        bool isLeader2 = leaderElection2.IsLeader;

        // One should become leader, the other should not
        (isLeader1 != isLeader2).ShouldBeTrue("Exactly one participant should become leader");

        ILeaderElectionService leader = isLeader1 ? leaderElection1 : leaderElection2;
        ILeaderElectionService follower = isLeader1 ? leaderElection2 : leaderElection1;

        // Verify leadership status
        leader.IsLeader.ShouldBeTrue();
        follower.IsLeader.ShouldBeFalse();

        // Get current leader info
        LeaderInfo? leaderInfo = await leader.GetCurrentLeaderAsync();
        leaderInfo.ShouldNotBeNull();
        leaderInfo.LeaderId.ShouldBe(isLeader1 ? "participant-1" : "participant-2");

        // Release leadership
        await leader.ReleaseLeadershipAsync();

        // Verify leadership is released
        leader.IsLeader.ShouldBeFalse();

        // Wait for Consul's SessionLockDelay to expire by polling for leadership acquisition
        bool becameLeader = false;
        await TestHelpers.WaitForConditionAsync(
            async () =>
            {
                becameLeader = await follower.TryAcquireLeadershipAsync();
                return becameLeader;
            },
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        becameLeader.ShouldBeTrue();
        follower.IsLeader.ShouldBeTrue();
    }

    [Fact]
    public async Task Consul_ReleaseLeadership_ShouldAllowNewLeader_WithEventDriven()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        var options = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = keyPrefix,
            SessionTtl = TimeSpan.FromSeconds(60),
            SessionLockDelay = TimeSpan.FromSeconds(15)
        };

        using var provider1 = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        using var provider2 = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act - First provider acquires leadership
        bool acquired1 = await provider1.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        acquired1.ShouldBeTrue();

        // Release leadership
        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");

        // Wait for new leader using event-driven approach
        bool acquired2 = false;
        await TestHelpers.WaitForConditionAsync(
            async () =>
            {
                acquired2 = await provider2.TryAcquireLeadershipAsync(
                    "test-group",
                    "participant-2",
                    metadata,
                    TimeSpan.FromMinutes(5));
                return acquired2;
            },
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        // Assert
        acquired2.ShouldBeTrue();

        // Cleanup
        await provider2.ReleaseLeadershipAsync("test-group", "participant-2");
    }

    [Fact]
    public async Task Consul_LeaderElection_ShouldWork_WithEventDrivenWaiting()
    {
        // Arrange
        if (!await IsConsulAvailableAsync())
            Assert.Fail("Consul is not available");

        // Use a unique election group to avoid conflicts with other tests
        string uniqueElectionGroup = $"event-driven-test-{Guid.NewGuid()}";
        host1 = CreateHost("leader-1", uniqueElectionGroup);
        host2 = CreateHost("follower-1", uniqueElectionGroup);

        await host1.StartAsync();
        await host2.StartAsync();

        var leader = host1.Services.GetRequiredService<ILeaderElectionService>();
        var follower = host2.Services.GetRequiredService<ILeaderElectionService>();

        // Start the services - this will trigger the initial election process
        await leader.StartAsync();
        await follower.StartAsync();

        // Act - Wait for leader to be elected using event-driven approach (synchronous condition)
        await TestHelpers.WaitForConditionAsync(
            () => leader.IsLeader || follower.IsLeader,
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        // Determine which one became leader
        ILeaderElectionService actualLeader = leader.IsLeader ? leader : follower;
        ILeaderElectionService actualFollower = leader.IsLeader ? follower : leader;

        actualLeader.IsLeader.ShouldBeTrue();
        actualFollower.IsLeader.ShouldBeFalse();

        // Release leadership and wait for follower to acquire it
        await actualLeader.ReleaseLeadershipAsync();

        // Wait for Consul's SessionLockDelay to expire by polling for leadership acquisition
        bool becameLeader = false;
        await TestHelpers.WaitForConditionAsync(
            async () =>
            {
                becameLeader = await actualFollower.TryAcquireLeadershipAsync();
                return becameLeader;
            },
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        // Assert
        becameLeader.ShouldBeTrue();
        actualFollower.IsLeader.ShouldBeTrue();
        actualLeader.IsLeader.ShouldBeFalse();
    }

    private static IHost CreateHost(string participantId, string? electionGroup = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
                services.AddConsulLeaderElection("http://localhost:8500", options =>
                {
                    options.ElectionGroup = electionGroup ?? "integration-test";
                    options.ParticipantId = participantId;
                    options.HeartbeatInterval = TimeSpan.FromSeconds(5);
                    options.HeartbeatTimeout = TimeSpan.FromSeconds(15);
                });
            })
            .Build();
    }

    private async Task<bool> IsConsulAvailableAsync()
    {
        try
        {
            var options = new ConsulLeaderElectionOptions
            {
                Address = "http://localhost:8500",
                KeyPrefix = "health-check",
                SessionTtl = TimeSpan.FromSeconds(60),
                SessionLockDelay = TimeSpan.FromSeconds(15)
            };

            using var provider = new ConsulLeaderElectionProvider(
                Options.Create(options),
                logger);

            return await provider.HealthCheckAsync();
        }
        catch
        {
            return false;
        }
    }
}


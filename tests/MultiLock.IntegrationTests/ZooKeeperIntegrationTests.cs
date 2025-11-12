using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Tests;
using MultiLock.ZooKeeper;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("ZooKeeper")]
public class ZooKeeperIntegrationTests : IAsyncLifetime
{
    private const string connectionString = "localhost:2181";
    private readonly ILogger<ZooKeeperLeaderElectionProvider> logger;
    private readonly string rootPath;
    private IHost? host1;
    private IHost? host2;

    public ZooKeeperIntegrationTests()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<ZooKeeperLeaderElectionProvider>();
        rootPath = $"/test-leader-election-{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        // Wait for ZooKeeper to be ready
        await TestHelpers.WaitForConditionAsync(
            IsZooKeeperAvailableAsync,
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
    public async Task HealthCheck_WithZooKeeper_ShouldReturnTrue()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act
        bool result = await provider.HealthCheckAsync();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireLeadership_WithZooKeeper_ShouldSucceed()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        bool result = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        result.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task TryAcquireLeadership_WhenAlreadyAcquired_ShouldReturnFalse()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options1 = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        var options2 = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider1 = new ZooKeeperLeaderElectionProvider(
            Options.Create(options1),
            logger);

        await using var provider2 = new ZooKeeperLeaderElectionProvider(
            Options.Create(options2),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider1.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act
        bool result = await provider2.TryAcquireLeadershipAsync(
            "test-group",
            "participant-2",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        result.ShouldBeFalse();

        // Cleanup
        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task ReleaseLeadership_ShouldAllowNewLeader()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options1 = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        var options2 = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider1 = new ZooKeeperLeaderElectionProvider(
            Options.Create(options1),
            logger);

        await using var provider2 = new ZooKeeperLeaderElectionProvider(
            Options.Create(options2),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider1.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act
        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");

        bool newLeaderAcquired = await provider2.TryAcquireLeadershipAsync(
            "test-group",
            "participant-2",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        newLeaderAcquired.ShouldBeTrue();

        // Cleanup
        await provider2.ReleaseLeadershipAsync("test-group", "participant-2");
    }

    [Fact]
    public async Task ZooKeeper_LeaderElection_ShouldWork()
    {
        // Fail test if ZooKeeper is not available
        if (!await IsZooKeeperAvailableAsync())
        {
            Assert.Fail("ZooKeeper is not available");
        }

        // Arrange
        host1 = CreateHost("participant-1");
        host2 = CreateHost("participant-2");

        await host1.StartAsync();
        await host2.StartAsync();

        ILeaderElectionService leaderElection1 = host1.Services.GetRequiredService<ILeaderElectionService>();
        ILeaderElectionService leaderElection2 = host2.Services.GetRequiredService<ILeaderElectionService>();

        // Start the services - this will trigger the initial election process
        await leaderElection1.StartAsync();
        await leaderElection2.StartAsync();

        // Wait a bit for the initial election process to complete
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Act & Assert
        // After StartAsync, one should be leader and the other should not
        bool isLeader1 = leaderElection1.IsLeader;
        bool isLeader2 = leaderElection2.IsLeader;

        // One should become leader, the other should not
        Assert.True(isLeader1 != isLeader2, "Exactly one participant should become leader");

        ILeaderElectionService leader = isLeader1 ? leaderElection1 : leaderElection2;
        ILeaderElectionService follower = isLeader1 ? leaderElection2 : leaderElection1;

        // Verify leadership status
        Assert.True(leader.IsLeader);
        Assert.False(follower.IsLeader);

        // Get current leader info
        LeaderInfo? leaderInfo = await leader.GetCurrentLeaderAsync();
        Assert.NotNull(leaderInfo);
        Assert.Equal(isLeader1 ? "participant-1" : "participant-2", leaderInfo.LeaderId);

        // Release leadership
        await leader.ReleaseLeadershipAsync();

        // Verify leadership is released
        Assert.False(leader.IsLeader);

        // The other participant should be able to become leader now
        bool becameLeader = await follower.TryAcquireLeadershipAsync();
        Assert.True(becameLeader);
        Assert.True(follower.IsLeader);
    }

    [Fact]
    public async Task ZooKeeper_HealthCheck_ShouldWork()
    {
        // Fail test if ZooKeeper is not available
        if (!await IsZooKeeperAvailableAsync())
        {
            Assert.Fail("ZooKeeper is not available");
        }

        // Arrange
        host1 = CreateHost("participant-1");
        await host1.StartAsync();

        ILeaderElectionProvider provider = host1.Services.GetRequiredService<ILeaderElectionProvider>();

        // Act & Assert
        bool isHealthy = await provider.HealthCheckAsync();
        Assert.True(isHealthy);
    }

    [Fact]
    public async Task ZooKeeper_SingleParticipant_ShouldBecomeLeader()
    {
        // Fail test if ZooKeeper is not available
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        // Arrange
        host1 = CreateHost("participant-1");
        await host1.StartAsync();

        ILeaderElectionService leaderElection1 = host1.Services.GetRequiredService<ILeaderElectionService>();
        await leaderElection1.StartAsync();

        // Act - Try to become leader
        bool becameLeader = await leaderElection1.TryAcquireLeadershipAsync();

        // Debug output
        logger.LogInformation("TryAcquireLeadershipAsync returned: {BecameLeader}", becameLeader);
        logger.LogInformation("IsLeader property: {IsLeader}", leaderElection1.IsLeader);

        // Assert
        Assert.True(becameLeader, "Single participant should be able to become leader");
        Assert.True(leaderElection1.IsLeader, "Participant should be marked as leader");

        // Verify we can get current leader info
        LeaderInfo? currentLeader = await leaderElection1.GetCurrentLeaderAsync();
        Assert.NotNull(currentLeader);
        Assert.Equal("participant-1", currentLeader.LeaderId);
    }

    private static IHost CreateHost(string participantId)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
                services.AddZooKeeperLeaderElection(
                    zooKeeperOptions =>
                    {
                        zooKeeperOptions.ConnectionString = connectionString;
                        // Use shorter session timeout for faster testing
                        zooKeeperOptions.SessionTimeout = TimeSpan.FromSeconds(10);
                    },
                    leaderElectionOptions =>
                    {
                        leaderElectionOptions.ElectionGroup = "integration-test";
                        leaderElectionOptions.ParticipantId = participantId;
                        leaderElectionOptions.HeartbeatInterval = TimeSpan.FromSeconds(5);
                        leaderElectionOptions.HeartbeatTimeout = TimeSpan.FromSeconds(15);
                    });
            })
            .Build();
    }

    [Fact]
    public async Task UpdateHeartbeat_AsLeader_ShouldSucceed()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act
        bool updated = await provider.UpdateHeartbeatAsync(
            "test-group",
            "participant-1",
            metadata);

        // Assert
        updated.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task UpdateHeartbeat_AsNonLeader_ShouldReturnFalse()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        bool updated = await provider.UpdateHeartbeatAsync(
            "test-group",
            "participant-1",
            metadata);

        // Assert
        updated.ShouldBeFalse();
    }

    [Fact]
    public async Task IsLeader_ShouldReturnCorrectStatus()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act - Before acquiring leadership
        bool isLeaderBefore = await provider.IsLeaderAsync("test-group", "participant-1");

        // Acquire leadership
        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act - After acquiring leadership
        bool isLeaderAfter = await provider.IsLeaderAsync("test-group", "participant-1");

        // Assert
        isLeaderBefore.ShouldBeFalse();
        isLeaderAfter.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task GetCurrentLeader_ShouldReturnLeaderInfo()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "test", "data" } };

        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act
        LeaderInfo? leaderInfo = await provider.GetCurrentLeaderAsync("test-group");

        // Assert
        leaderInfo.ShouldNotBeNull();
        leaderInfo.LeaderId.ShouldBe("participant-1");
        leaderInfo.Metadata.ContainsKey("test").ShouldBeTrue();
        leaderInfo.Metadata["test"].ShouldBe("data");

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task TryAcquireLeadership_SameParticipantTwice_ShouldReuseNode()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act - Acquire leadership first time
        bool firstAcquire = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act - Try to acquire again with same participant
        bool secondAcquire = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert - Both should succeed (reusing the same node)
        firstAcquire.ShouldBeTrue();
        secondAcquire.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task GetCurrentLeader_WhenNoLeader_ShouldReturnNull()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act - Get leader when no one has acquired leadership
        LeaderInfo? leaderInfo = await provider.GetCurrentLeaderAsync("non-existent-group");

        // Assert
        leaderInfo.ShouldBeNull();
    }

    [Fact]
    public async Task ReleaseLeadership_WhenNotHoldingLeadership_ShouldNotThrow()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert - Should not throw when releasing non-existent leadership
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task UpdateHeartbeat_WithMetadataChanges_ShouldUpdateMetadata()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        var initialMetadata = new Dictionary<string, string> { { "version", "1" } };
        var updatedMetadata = new Dictionary<string, string> { { "version", "2" } };

        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            initialMetadata,
            TimeSpan.FromMinutes(5));

        // Act - Update heartbeat with new metadata
        bool updated = await provider.UpdateHeartbeatAsync(
            "test-group",
            "participant-1",
            updatedMetadata);

        // Assert
        updated.ShouldBeTrue();

        // Verify metadata was updated
        LeaderInfo? leaderInfo = await provider.GetCurrentLeaderAsync("test-group");
        leaderInfo.ShouldNotBeNull();
        leaderInfo.Metadata["version"].ShouldBe("2");

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task UpdateHeartbeat_WhenNodeDeleted_ShouldReturnFalse()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Release leadership (deletes the node)
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");

        // Act - Try to update heartbeat after node is deleted
        bool updated = await provider.UpdateHeartbeatAsync(
            "test-group",
            "participant-1",
            metadata);

        // Assert
        updated.ShouldBeFalse();
    }

    [Fact]
    public async Task DisposeAsync_ShouldPreventFurtherOperations()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act - Dispose the provider
        await provider.DisposeAsync();

        // Assert - Operations should throw ObjectDisposedException
        await Should.ThrowAsync<ObjectDisposedException>(async () =>
            await provider.TryAcquireLeadershipAsync(
                "test-group",
                "participant-1",
                new Dictionary<string, string>(),
                TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_ShouldBeIdempotent()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act - Dispose twice
        await provider.DisposeAsync();
        await provider.DisposeAsync();

        // Assert - Should not throw
        Assert.True(true);
    }

    [Fact]
    public async Task MultipleElectionGroups_ShouldWorkIndependently()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        await using var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act - Acquire leadership in multiple groups
        bool group1 = await provider.TryAcquireLeadershipAsync(
            "group-1",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        bool group2 = await provider.TryAcquireLeadershipAsync(
            "group-2",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert - Should be leader in both groups
        group1.ShouldBeTrue();
        group2.ShouldBeTrue();

        bool isLeaderGroup1 = await provider.IsLeaderAsync("group-1", "participant-1");
        bool isLeaderGroup2 = await provider.IsLeaderAsync("group-2", "participant-1");

        isLeaderGroup1.ShouldBeTrue();
        isLeaderGroup2.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("group-1", "participant-1");
        await provider.ReleaseLeadershipAsync("group-2", "participant-1");
    }

    [Fact]
    public async Task HealthCheck_AfterDisposal_ShouldReturnFalse()
    {
        // Arrange
        if (!await IsZooKeeperAvailableAsync())
            Assert.Fail("ZooKeeper is not available");

        var options = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = connectionString,
            RootPath = rootPath,
            SessionTimeout = TimeSpan.FromSeconds(30),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Verify it's healthy first
        bool healthyBefore = await provider.HealthCheckAsync();
        healthyBefore.ShouldBeTrue();

        // Act - Dispose the provider
        await provider.DisposeAsync();

        // Assert - Health check should return false after disposal
        bool healthyAfter = await provider.HealthCheckAsync();
        healthyAfter.ShouldBeFalse();
    }

    private async Task<bool> IsZooKeeperAvailableAsync()
    {
        try
        {
            var options = new ZooKeeperLeaderElectionOptions
            {
                ConnectionString = connectionString,
                RootPath = "/health-check",
                SessionTimeout = TimeSpan.FromSeconds(30),
                ConnectionTimeout = TimeSpan.FromSeconds(10)
            };

            await using var provider = new ZooKeeperLeaderElectionProvider(
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

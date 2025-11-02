using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Redis;
using MultiLock.Tests;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("Redis")]
public class RedisIntegrationTests : IAsyncLifetime
{
    private readonly ILogger<RedisLeaderElectionProvider> logger;
    private readonly string keyPrefix;
    private IHost? host1;
    private IHost? host2;

    public RedisIntegrationTests()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<RedisLeaderElectionProvider>();
        keyPrefix = $"test-leader-election-{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        // Wait for Redis to be ready
        await TestHelpers.WaitForConditionAsync(
            () => IsRedisAvailableAsync().GetAwaiter().GetResult(),
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
    public async Task HealthCheck_WithRedis_ShouldReturnTrue()
    {
        // Arrange
        if (!await IsRedisAvailableAsync())
            Assert.Fail("Redis is not available");

        var options = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = keyPrefix,
            Database = 0
        };

        using var provider = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act
        bool isHealthy = await provider.HealthCheckAsync();

        // Assert
        isHealthy.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireLeadership_WithRedis_ShouldSucceed()
    {
        // Arrange
        if (!await IsRedisAvailableAsync())
            Assert.Fail("Redis is not available");

        var options = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = keyPrefix,
            Database = 0
        };

        using var provider = new RedisLeaderElectionProvider(
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
        if (!await IsRedisAvailableAsync())
            Assert.Fail("Redis is not available");

        var options = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = keyPrefix,
            Database = 0
        };

        using var provider1 = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        using var provider2 = new RedisLeaderElectionProvider(
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
        if (!await IsRedisAvailableAsync())
            Assert.Fail("Redis is not available");

        var options = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = keyPrefix,
            Database = 0
        };

        using var provider1 = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        using var provider2 = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        bool acquired1 = await provider1.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");

        bool acquired2 = await provider2.TryAcquireLeadershipAsync(
            "test-group",
            "participant-2",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        acquired1.ShouldBeTrue();
        acquired2.ShouldBeTrue();

        // Cleanup
        await provider2.ReleaseLeadershipAsync("test-group", "participant-2");
    }

    [Fact]
    public async Task UpdateHeartbeat_AsLeader_ShouldSucceed()
    {
        // Arrange
        if (!await IsRedisAvailableAsync())
            Assert.Fail("Redis is not available");

        var options = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = keyPrefix,
            Database = 0
        };

        using var provider = new RedisLeaderElectionProvider(
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
        if (!await IsRedisAvailableAsync())
            Assert.Fail("Redis is not available");

        var options = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = keyPrefix,
            Database = 0
        };

        using var provider = new RedisLeaderElectionProvider(
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
        if (!await IsRedisAvailableAsync())
            Assert.Fail("Redis is not available");

        var options = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = keyPrefix,
            Database = 0
        };

        using var provider = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act & Assert - Before acquiring leadership
        bool isLeaderBefore = await provider.IsLeaderAsync("test-group", "participant-1");
        isLeaderBefore.ShouldBeFalse();

        // Acquire leadership
        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act & Assert - After acquiring leadership
        bool isLeaderAfter = await provider.IsLeaderAsync("test-group", "participant-1");
        isLeaderAfter.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task GetCurrentLeader_ShouldReturnLeaderInfo()
    {
        // Arrange
        if (!await IsRedisAvailableAsync())
            Assert.Fail("Redis is not available");

        var options = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = keyPrefix,
            Database = 0
        };

        using var provider = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        LeaderInfo? leaderInfo = await provider.GetCurrentLeaderAsync("test-group");

        // Assert
        acquired.ShouldBeTrue();
        leaderInfo.ShouldNotBeNull();
        leaderInfo.LeaderId.ShouldBe("participant-1");
        leaderInfo.Metadata.ContainsKey("test").ShouldBeTrue();
        leaderInfo.Metadata["test"].ShouldBe("value");

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task Redis_LeaderElection_ShouldWork()
    {
        // Arrange
        if (!await IsRedisAvailableAsync())
            Assert.Fail("Redis is not available");

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

        // The other participant should be able to become leader now
        bool becameLeader = await follower.TryAcquireLeadershipAsync();
        becameLeader.ShouldBeTrue();
        follower.IsLeader.ShouldBeTrue();
    }

    private static IHost CreateHost(string participantId)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
                services.AddRedisLeaderElection("localhost:6379", options =>
                {
                    options.ElectionGroup = "integration-test";
                    options.ParticipantId = participantId;
                    options.HeartbeatInterval = TimeSpan.FromSeconds(5);
                    options.HeartbeatTimeout = TimeSpan.FromSeconds(15);
                });
            })
            .Build();
    }

    private async Task<bool> IsRedisAvailableAsync()
    {
        try
        {
            var options = new RedisLeaderElectionOptions
            {
                ConnectionString = "localhost:6379",
                KeyPrefix = "health-check",
                Database = 0
            };

            using var provider = new RedisLeaderElectionProvider(
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


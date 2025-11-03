using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.PostgreSQL;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("PostgreSQL")]
public class PostgreSqlIntegrationTests : IAsyncLifetime
{
    private const string connectionString = "Host=localhost;Database=leaderelection;Username=leaderelection;Password=leaderelection123";
    private IHost? host1;
    private IHost? host2;
    private readonly ILogger<PostgreSqlLeaderElectionProvider> logger;

    public PostgreSqlIntegrationTests()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<PostgreSqlLeaderElectionProvider>();
    }

    public async Task InitializeAsync()
    {
        // Verify PostgreSQL is available before running tests
        if (!await IsPostgreSqlAvailableAsync())
        {
            throw new InvalidOperationException("PostgreSQL is not available. Make sure PostgreSQL is running on localhost:5432");
        }
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
    public async Task HealthCheck_WithPostgreSQL_ShouldReturnTrue()
    {
        // Arrange
        var options = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = $"test_leader_election_{Guid.NewGuid():N}",
            SchemaName = "public",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);

        // Act
        bool isHealthy = await provider.HealthCheckAsync();

        // Assert
        isHealthy.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireLeadership_WithPostgreSQL_ShouldSucceed()
    {
        // Arrange
        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "public",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireLeadership_WhenAlreadyAcquired_ShouldReturnFalse()
    {
        // Arrange
        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "public",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider1 = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);
        using var provider2 = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

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
    }

    [Fact]
    public async Task ReleaseLeadership_ShouldAllowNewLeader()
    {
        // Arrange
        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "public",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider1 = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);
        using var provider2 = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool acquired1 = await provider1.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        acquired1.ShouldBeTrue();

        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");

        bool acquired2 = await provider2.TryAcquireLeadershipAsync(
            "test-group",
            "participant-2",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        acquired2.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateHeartbeat_AsLeader_ShouldSucceed()
    {
        // Arrange
        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "public",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        bool heartbeatUpdated = await provider.UpdateHeartbeatAsync("test-group", "participant-1", metadata);

        // Assert
        acquired.ShouldBeTrue();
        heartbeatUpdated.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateHeartbeat_AsNonLeader_ShouldReturnFalse()
    {
        // Arrange
        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "public",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool heartbeatUpdated = await provider.UpdateHeartbeatAsync("test-group", "participant-1", metadata);

        // Assert
        heartbeatUpdated.ShouldBeFalse();
    }

    [Fact]
    public async Task IsLeader_ShouldReturnCorrectStatus()
    {
        // Arrange
        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "public",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool isLeaderBefore = await provider.IsLeaderAsync("test-group", "participant-1");

        bool acquired = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        bool isLeaderAfter = await provider.IsLeaderAsync("test-group", "participant-1");

        // Assert
        isLeaderBefore.ShouldBeFalse();
        acquired.ShouldBeTrue();
        isLeaderAfter.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCurrentLeader_ShouldReturnLeaderInfo()
    {
        // Arrange
        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "public",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new PostgreSqlLeaderElectionProvider(Options.Create(options), logger);
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
    }

    [Fact]
    public async Task PostgreSQL_LeaderElection_ShouldWork()
    {
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
                services.AddPostgreSqlLeaderElection(connectionString, options =>
                {
                    options.ElectionGroup = "integration-test";
                    options.ParticipantId = participantId;
                    options.HeartbeatInterval = TimeSpan.FromSeconds(5);
                    options.HeartbeatTimeout = TimeSpan.FromSeconds(15);
                });
            })
            .Build();
    }

    private static async Task<bool> IsPostgreSqlAvailableAsync()
    {
        try
        {
            var options = new PostgreSqlLeaderElectionOptions
            {
                ConnectionString = connectionString,
                TableName = "health_check",
                HeartbeatInterval = TimeSpan.FromSeconds(30),
                LeaderTimeout = TimeSpan.FromSeconds(60)
            };

            using var provider = new PostgreSqlLeaderElectionProvider(
                Options.Create(options),
                NullLogger<PostgreSqlLeaderElectionProvider>.Instance);

            return await provider.HealthCheckAsync();
        }
        catch
        {
            return false;
        }
    }
}

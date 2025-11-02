using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.SqlServer;
using MultiLock.Tests;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("SqlServer")]
public class SqlServerIntegrationTests : IAsyncLifetime
{
    private const string connectionString = "Server=tcp:localhost,1433;Database=leaderelection;User Id=sa;Password=LeaderElection123!;TrustServerCertificate=True;Encrypt=False;";
    private IHost? host1;
    private IHost? host2;
    private readonly ILogger<SqlServerLeaderElectionProvider> logger;

    public SqlServerIntegrationTests()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<SqlServerLeaderElectionProvider>();
    }

    public async Task InitializeAsync()
    {
        // Wait for SQL Server to be ready
        await TestHelpers.WaitForConditionAsync(
            () => IsSqlServerAvailableAsync().GetAwaiter().GetResult(),
            TimeSpan.FromSeconds(30),
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
    public async Task HealthCheck_WithSqlServer_ShouldReturnTrue()
    {
        // Arrange
        if (!await IsSqlServerAvailableAsync())
            Assert.Fail("SQL Server is not available");

        var options = new SqlServerLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = $"test_leader_election_{Guid.NewGuid():N}",
            SchemaName = "dbo",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new SqlServerLeaderElectionProvider(Options.Create(options), logger);

        // Act
        bool isHealthy = await provider.HealthCheckAsync();

        // Assert
        isHealthy.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireLeadership_WithSqlServer_ShouldSucceed()
    {
        // Arrange
        if (!await IsSqlServerAvailableAsync())
            Assert.Fail("SQL Server is not available");

        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new SqlServerLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "dbo",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new SqlServerLeaderElectionProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task TryAcquireLeadership_WhenAlreadyAcquired_ShouldReturnFalse()
    {
        // Arrange
        if (!await IsSqlServerAvailableAsync())
            Assert.Fail("SQL Server is not available");

        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new SqlServerLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "dbo",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider1 = new SqlServerLeaderElectionProvider(Options.Create(options), logger);
        using var provider2 = new SqlServerLeaderElectionProvider(Options.Create(options), logger);
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

        // Cleanup
        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task ReleaseLeadership_ShouldAllowNewLeader()
    {
        // Arrange
        if (!await IsSqlServerAvailableAsync())
            Assert.Fail("SQL Server is not available");

        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new SqlServerLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "dbo",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider1 = new SqlServerLeaderElectionProvider(Options.Create(options), logger);
        using var provider2 = new SqlServerLeaderElectionProvider(Options.Create(options), logger);
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

        // Cleanup
        await provider2.ReleaseLeadershipAsync("test-group", "participant-2");
    }

    [Fact]
    public async Task UpdateHeartbeat_AsLeader_ShouldSucceed()
    {
        // Arrange
        if (!await IsSqlServerAvailableAsync())
            Assert.Fail("SQL Server is not available");

        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new SqlServerLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "dbo",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new SqlServerLeaderElectionProvider(Options.Create(options), logger);
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

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task UpdateHeartbeat_AsNonLeader_ShouldReturnFalse()
    {
        // Arrange
        if (!await IsSqlServerAvailableAsync())
            Assert.Fail("SQL Server is not available");

        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new SqlServerLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "dbo",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new SqlServerLeaderElectionProvider(Options.Create(options), logger);
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
        if (!await IsSqlServerAvailableAsync())
            Assert.Fail("SQL Server is not available");

        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new SqlServerLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "dbo",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new SqlServerLeaderElectionProvider(Options.Create(options), logger);
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

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }



    [Fact]
    public async Task GetCurrentLeader_ShouldReturnLeaderInfo()
    {
        // Arrange
        if (!await IsSqlServerAvailableAsync())
            Assert.Fail("SQL Server is not available");

        string tableName = $"test_leader_election_{Guid.NewGuid():N}";
        var options = new SqlServerLeaderElectionOptions
        {
            ConnectionString = connectionString,
            TableName = tableName,
            SchemaName = "dbo",
            AutoCreateTable = true,
            CommandTimeoutSeconds = 30
        };

        using var provider = new SqlServerLeaderElectionProvider(Options.Create(options), logger);
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
    public async Task SqlServer_LeaderElection_ShouldWork()
    {
        // Arrange
        if (!await IsSqlServerAvailableAsync())
            Assert.Fail("SQL Server is not available");

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
                services.AddSqlServerLeaderElection(connectionString, options =>
                {
                    options.ElectionGroup = "integration-test";
                    options.ParticipantId = participantId;
                    options.HeartbeatInterval = TimeSpan.FromSeconds(5);
                    options.HeartbeatTimeout = TimeSpan.FromSeconds(15);
                });
            })
            .Build();
    }

    private static async Task<bool> IsSqlServerAvailableAsync()
    {
        try
        {
            using IHost host = CreateHost("test");
            await host.StartAsync();
            ILeaderElectionProvider provider = host.Services.GetRequiredService<ILeaderElectionProvider>();
            bool isHealthy = await provider.HealthCheckAsync();
            await host.StopAsync();
            return isHealthy;
        }
        catch
        {
            return false;
        }
    }
}

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.SqlServer;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("SqlServer")]
public class SqlServerSemaphoreIntegrationTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("MULTILOCK_SQLSERVER_CONNECTION")
        ?? "Server=localhost,1433;Database=leaderelection;User Id=sa;Password=LeaderElection123!;TrustServerCertificate=True";
    private const string SchemaName = "dbo";
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<SqlServerSemaphoreProvider> logger;

    // Unique per test-class-instance so parallel test runs and multiple [Fact] methods never share state.
    private readonly string tableName = $"test_semaphore_{Guid.NewGuid():N}";
    private readonly string semaphoreName = $"semaphore-{Guid.NewGuid():N}";

    public SqlServerSemaphoreIntegrationTests()
    {
        loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<SqlServerSemaphoreProvider>();
    }

    public async Task InitializeAsync()
    {
        if (!await IsSqlServerAvailableAsync())
            throw new InvalidOperationException("SQL Server is not available. Make sure SQL Server is running on localhost:1433");
    }

    public async Task DisposeAsync()
    {
        // Drop the per-test table so the database stays clean across runs.
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"IF OBJECT_ID(N'[{SchemaName}].[{tableName}]', N'U') IS NOT NULL DROP TABLE [{SchemaName}].[{tableName}]";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup — don't fail the test on teardown errors.
        }

        loggerFactory.Dispose();
    }

    [Fact]
    public async Task HealthCheck_WithSqlServer_ShouldReturnTrue()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);

        // Act
        bool isHealthy = await provider.HealthCheckAsync();

        // Assert
        isHealthy.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreEmpty_ShouldSucceed()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool acquired = await provider.TryAcquireAsync(semaphoreName, "holder-1", 3, metadata, TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreFull_ShouldFail()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 2;

        (await provider.TryAcquireAsync(semaphoreName, "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire a slot");
        (await provider.TryAcquireAsync(semaphoreName, "holder-2", maxCount, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-2 should acquire a slot");

        // Act
        bool acquired = await provider.TryAcquireAsync(semaphoreName, "holder-3", maxCount, metadata, TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenHolderAlreadyHasSlot_ShouldRenew()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "key", "value" } };

        (await provider.TryAcquireAsync(semaphoreName, "holder-1", 3, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire the initial slot");

        // Act
        bool acquired = await provider.TryAcquireAsync(semaphoreName, "holder-1", 3, metadata, TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();
        int count = await provider.GetCurrentCountAsync(semaphoreName, TimeSpan.FromMinutes(5));
        count.ShouldBe(1);
    }

    [Fact]
    public async Task ReleaseAsync_ShouldAllowNewHolder()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 1;

        (await provider.TryAcquireAsync(semaphoreName, "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire the initial slot");

        // Act
        await provider.ReleaseAsync(semaphoreName, "holder-1");
        bool acquired = await provider.TryAcquireAsync(semaphoreName, "holder-2", maxCount, metadata, TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenHolding_ShouldSucceed()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "key", "value" } };

        (await provider.TryAcquireAsync(semaphoreName, "holder-1", 3, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire a slot");

        // Act
        bool updated = await provider.UpdateHeartbeatAsync(semaphoreName, "holder-1", metadata);

        // Assert
        updated.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenNotHolding_ShouldFail()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        // Act
        bool updated = await provider.UpdateHeartbeatAsync(semaphoreName, "holder-1", metadata);

        // Assert
        updated.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCurrentCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        (await provider.TryAcquireAsync(semaphoreName, "holder-1", 5, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire a slot");
        (await provider.TryAcquireAsync(semaphoreName, "holder-2", 5, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-2 should acquire a slot");

        // Act
        int count = await provider.GetCurrentCountAsync(semaphoreName, TimeSpan.FromMinutes(5));

        // Assert
        count.ShouldBe(2);
    }

    [Fact]
    public async Task GetHoldersAsync_ShouldReturnAllHolders()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        var metadata1 = new Dictionary<string, string> { { "holder", "1" } };
        var metadata2 = new Dictionary<string, string> { { "holder", "2" } };

        (await provider.TryAcquireAsync(semaphoreName, "holder-1", 5, metadata1, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire a slot");
        (await provider.TryAcquireAsync(semaphoreName, "holder-2", 5, metadata2, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-2 should acquire a slot");

        // Act
        IReadOnlyList<SemaphoreHolder> holders = await provider.GetHoldersAsync(semaphoreName);

        // Assert
        holders.Count.ShouldBe(2);
        holders.ShouldContain(h => h.HolderId == "holder-1");
        holders.ShouldContain(h => h.HolderId == "holder-2");
    }

    [Fact]
    public async Task IsHoldingAsync_ShouldReturnCorrectStatus()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        // Act
        bool isHoldingBefore = await provider.IsHoldingAsync(semaphoreName, "holder-1");
        (await provider.TryAcquireAsync(semaphoreName, "holder-1", 3, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire a slot");
        bool isHoldingAfter = await provider.IsHoldingAsync(semaphoreName, "holder-1");

        // Assert
        isHoldingBefore.ShouldBeFalse();
        isHoldingAfter.ShouldBeTrue();
    }

    [Fact]
    public async Task ConcurrentAcquire_ShouldRespectMaxCount()
    {
        // Arrange
        var options = CreateOptions();
        var metadata = new Dictionary<string, string>();
        const int maxCount = 3;
        const int holderCount = 10;

        // Create a single provider to ensure table is created before concurrent access
        using var mainProvider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
        await mainProvider.HealthCheckAsync();

        var tasks = new List<Task<bool>>();

        // Act
        for (int i = 0; i < holderCount; i++)
        {
            int holderId = i;
            tasks.Add(Task.Run(async () =>
            {
                using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
                return await provider.TryAcquireAsync(
                    semaphoreName,
                    $"holder-{holderId}",
                    maxCount,
                    metadata,
                    TimeSpan.FromMinutes(5));
            }));
        }

        bool[] results = await Task.WhenAll(tasks);

        // Assert - exactly maxCount slots should have been acquired.
        // sp_getapplock with Exclusive mode under ReadCommitted serialises per-semaphore
        // acquisitions and prevents over-admission.
        // With holderCount >> maxCount all slots must be filled.
        int successCount = results.Count(r => r);
        successCount.ShouldBe(maxCount);

        // Verify that the provider's view of the count matches what the acquisitions returned.
        int reportedCount = await mainProvider.GetCurrentCountAsync(semaphoreName, TimeSpan.FromMinutes(5));
        reportedCount.ShouldBe(maxCount);
    }

    private SqlServerSemaphoreOptions CreateOptions() => new()
    {
        ConnectionString = ConnectionString,
        TableName = tableName,
        SchemaName = SchemaName,
        AutoCreateTable = true,
        CommandTimeoutSeconds = 30
    };

    private async Task<bool> IsSqlServerAvailableAsync()
    {
        try
        {
            var options = CreateOptions();
            using var provider = new SqlServerSemaphoreProvider(Options.Create(options), logger);
            return await provider.HealthCheckAsync();
        }
        catch
        {
            return false;
        }
    }
}


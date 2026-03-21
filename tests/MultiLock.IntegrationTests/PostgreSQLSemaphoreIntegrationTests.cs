using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.PostgreSQL;
using Npgsql;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("PostgreSQL")]
public class PostgreSqlSemaphoreIntegrationTests : IAsyncLifetime
{
    private static readonly string connectionString =
        Environment.GetEnvironmentVariable("MULTILOCK_POSTGRES_CONNECTION")
        ?? "Host=localhost;Database=leaderelection;Username=leaderelection;Password=leaderelection123";
    private const string schemaName = "public";
    private readonly ILogger<PostgreSqlSemaphoreProvider> logger;

    // Unique per test-class-instance so parallel test runs and multiple [Fact] methods never share state.
    private readonly string tableName = $"test_semaphore_{Guid.NewGuid():N}";
    private readonly string semaphoreName = $"semaphore-{Guid.NewGuid():N}";

    public PostgreSqlSemaphoreIntegrationTests()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<PostgreSqlSemaphoreProvider>();
    }

    public async Task InitializeAsync()
    {
        // Verify PostgreSQL is available before running tests
        if (!await IsPostgreSqlAvailableAsync())
            throw new InvalidOperationException("PostgreSQL is not available. Make sure PostgreSQL is running on localhost:5432");
    }

    public async Task DisposeAsync()
    {
        // Drop the per-test table so the database stays clean across runs.
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""DROP TABLE IF EXISTS "{schemaName}"."{tableName}" """;
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup — don't fail the test on teardown errors.
        }
    }

    [Fact]
    public async Task HealthCheck_WithPostgreSQL_ShouldReturnTrue()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);

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
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool acquired = await provider.TryAcquireAsync(
            semaphoreName,
            "holder-1",
            3,
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreFull_ShouldFail()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 2;

        // Fill the semaphore
        await provider.TryAcquireAsync(semaphoreName, "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5));
        await provider.TryAcquireAsync(semaphoreName, "holder-2", maxCount, metadata, TimeSpan.FromMinutes(5));

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
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider.TryAcquireAsync(semaphoreName, "holder-1", 3, metadata, TimeSpan.FromMinutes(5));

        // Act - try to acquire again with same holder
        bool acquired = await provider.TryAcquireAsync(semaphoreName, "holder-1", 3, metadata, TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();
        int count = await provider.GetCurrentCountAsync(semaphoreName, TimeSpan.FromMinutes(5));
        count.ShouldBe(1); // Should still be 1, not 2
    }

    [Fact]
    public async Task ReleaseAsync_ShouldAllowNewHolder()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 1;

        await provider.TryAcquireAsync(semaphoreName, "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5));

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
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider.TryAcquireAsync(semaphoreName, "holder-1", 3, metadata, TimeSpan.FromMinutes(5));

        var updatedMetadata = new Dictionary<string, string> { { "key", "updated-value" } };

        // Act
        bool updated = await provider.UpdateHeartbeatAsync(semaphoreName, "holder-1", updatedMetadata);

        // Assert
        updated.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenNotHolding_ShouldFail()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        await provider.TryAcquireAsync(semaphoreName, "holder-1", 5, metadata, TimeSpan.FromMinutes(5));
        await provider.TryAcquireAsync(semaphoreName, "holder-2", 5, metadata, TimeSpan.FromMinutes(5));

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
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
        var metadata1 = new Dictionary<string, string> { { "holder", "1" } };
        var metadata2 = new Dictionary<string, string> { { "holder", "2" } };

        await provider.TryAcquireAsync(semaphoreName, "holder-1", 5, metadata1, TimeSpan.FromMinutes(5));
        await provider.TryAcquireAsync(semaphoreName, "holder-2", 5, metadata2, TimeSpan.FromMinutes(5));

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
        using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        // Act
        bool isHoldingBefore = await provider.IsHoldingAsync(semaphoreName, "holder-1");
        await provider.TryAcquireAsync(semaphoreName, "holder-1", 3, metadata, TimeSpan.FromMinutes(5));
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
        using var mainProvider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);

        // Initialize the table by doing a health check
        await mainProvider.HealthCheckAsync();

        var tasks = new List<Task<bool>>();

        // Act - try to acquire with many holders concurrently using separate providers
        for (int i = 0; i < holderCount; i++)
        {
            int holderId = i;
            tasks.Add(Task.Run(async () =>
            {
                using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
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
        // The provider uses pg_advisory_xact_lock under ReadCommitted, which serialises
        // per-semaphore acquisitions and prevents over-admission.
        // With holderCount >> maxCount all slots should be filled.
        int successCount = results.Count(r => r);
        successCount.ShouldBe(maxCount);

        // Verify that the provider's view of the count matches what the acquisitions returned.
        int reportedCount = await mainProvider.GetCurrentCountAsync(semaphoreName, TimeSpan.FromMinutes(5));
        reportedCount.ShouldBe(maxCount);
    }

    private PostgreSqlSemaphoreOptions CreateOptions() => new()
    {
        ConnectionString = connectionString,
        TableName = tableName,
        SchemaName = schemaName,
        AutoCreateTable = true,
        CommandTimeoutSeconds = 30
    };

    private async Task<bool> IsPostgreSqlAvailableAsync()
    {
        try
        {
            var options = new PostgreSqlSemaphoreOptions
            {
                ConnectionString = connectionString,
                TableName = "health_check"
            };

            using var provider = new PostgreSqlSemaphoreProvider(Options.Create(options), logger);
            return await provider.HealthCheckAsync();
        }
        catch
        {
            return false;
        }
    }
}


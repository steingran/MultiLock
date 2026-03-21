using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Redis;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("Redis")]
public class RedisSemaphoreIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString = "localhost:6379";
    private readonly ILogger<RedisSemaphoreProvider> logger;
    private readonly string keyPrefix;

    public RedisSemaphoreIntegrationTests()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<RedisSemaphoreProvider>();
        keyPrefix = $"test-semaphore-{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        if (!await IsRedisAvailableAsync())
            throw new InvalidOperationException("Redis is not available. Make sure Redis is running on localhost:6379");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task HealthCheck_WithRedis_ShouldReturnTrue()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);

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
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "test", "value" } };

        // Act
        bool acquired = await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreFull_ShouldFail()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 2;

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire");
        (await provider.TryAcquireAsync("test-semaphore", "holder-2", maxCount, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-2 should acquire");

        // Act
        bool acquired = await provider.TryAcquireAsync("test-semaphore", "holder-3", maxCount, metadata, TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenHolderAlreadyHasSlot_ShouldRenew()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "key", "value" } };

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("initial acquire should succeed");

        // Act
        bool acquired = await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();
        int count = await provider.GetCurrentCountAsync("test-semaphore", TimeSpan.FromMinutes(5));
        count.ShouldBe(1);
    }

    [Fact]
    public async Task ReleaseAsync_ShouldAllowNewHolder()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 1;

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire");

        // Act
        await provider.ReleaseAsync("test-semaphore", "holder-1");
        bool acquired = await provider.TryAcquireAsync("test-semaphore", "holder-2", maxCount, metadata, TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenHolding_ShouldSucceed()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "key", "value" } };

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire");

        // Act
        bool updated = await provider.UpdateHeartbeatAsync("test-semaphore", "holder-1", metadata);

        // Assert
        updated.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenNotHolding_ShouldFail()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        // Act
        bool updated = await provider.UpdateHeartbeatAsync("test-semaphore", "holder-1", metadata);

        // Assert
        updated.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCurrentCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 5, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire");
        (await provider.TryAcquireAsync("test-semaphore", "holder-2", 5, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-2 should acquire");

        // Act
        int count = await provider.GetCurrentCountAsync("test-semaphore", TimeSpan.FromMinutes(5));

        // Assert
        count.ShouldBe(2);
    }

    [Fact]
    public async Task GetHoldersAsync_ShouldReturnAllHolders()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
        var metadata1 = new Dictionary<string, string> { { "holder", "1" } };
        var metadata2 = new Dictionary<string, string> { { "holder", "2" } };

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 5, metadata1, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire");
        (await provider.TryAcquireAsync("test-semaphore", "holder-2", 5, metadata2, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-2 should acquire");

        // Act
        IReadOnlyList<SemaphoreHolder> holders = await provider.GetHoldersAsync("test-semaphore");

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
        using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        // Act
        bool isHoldingBefore = await provider.IsHoldingAsync("test-semaphore", "holder-1");
        await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5));
        bool isHoldingAfter = await provider.IsHoldingAsync("test-semaphore", "holder-1");

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

        var tasks = new List<Task<bool>>();

        // Act
        for (int i = 0; i < holderCount; i++)
        {
            int holderId = i;
            tasks.Add(Task.Run(async () =>
            {
                using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
                return await provider.TryAcquireAsync(
                    "test-semaphore",
                    $"holder-{holderId}",
                    maxCount,
                    metadata,
                    TimeSpan.FromMinutes(5));
            }));
        }

        bool[] results = await Task.WhenAll(tasks);

        // Assert - exactly maxCount slots should have been acquired (the Lua script is atomic,
        // so neither over- nor under-admission is possible).
        int successCount = results.Count(r => r);
        successCount.ShouldBe(maxCount);

        // Verify the provider's reported count matches the atomic admission result.
        using var verificationProvider = new RedisSemaphoreProvider(Options.Create(options), logger);
        int reportedCount = await verificationProvider.GetCurrentCountAsync("test-semaphore", TimeSpan.FromMinutes(5));
        reportedCount.ShouldBe(maxCount);
    }

    private RedisSemaphoreOptions CreateOptions() => new()
    {
        ConnectionString = ConnectionString,
        KeyPrefix = keyPrefix,
        Database = 0
    };

    private async Task<bool> IsRedisAvailableAsync()
    {
        try
        {
            var options = CreateOptions();
            using var provider = new RedisSemaphoreProvider(Options.Create(options), logger);
            return await provider.HealthCheckAsync();
        }
        catch
        {
            return false;
        }
    }
}


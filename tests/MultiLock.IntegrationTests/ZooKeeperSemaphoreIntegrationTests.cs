using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.ZooKeeper;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("ZooKeeper")]
public class ZooKeeperSemaphoreIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString = "localhost:2181";
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ZooKeeperSemaphoreProvider> logger;
    private readonly string rootPath;

    public ZooKeeperSemaphoreIntegrationTests()
    {
        loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<ZooKeeperSemaphoreProvider>();
        rootPath = $"/test-semaphore-{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        if (!await IsZooKeeperAvailableAsync())
            throw new InvalidOperationException("ZooKeeper is not available. Make sure ZooKeeper is running on localhost:2181");
    }

    public Task DisposeAsync()
    {
        loggerFactory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task HealthCheck_WithZooKeeper_ShouldReturnTrue()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new ZooKeeperSemaphoreProvider(Options.Create(options), logger);

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
        using var provider = new ZooKeeperSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new ZooKeeperSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new ZooKeeperSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new ZooKeeperSemaphoreProvider(Options.Create(options), logger);
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
    public async Task GetCurrentCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new ZooKeeperSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 5, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire");
        (await provider.TryAcquireAsync("test-semaphore", "holder-2", 5, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-2 should acquire");

        // Act
        int count = await provider.GetCurrentCountAsync("test-semaphore", TimeSpan.FromMinutes(5));

        // Assert
        count.ShouldBe(2);
    }

    [Fact]
    public async Task IsHoldingAsync_ShouldReturnCorrectStatus()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new ZooKeeperSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        // Act
        bool isHoldingBefore = await provider.IsHoldingAsync("test-semaphore", "holder-1");
        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("should acquire");
        bool isHoldingAfter = await provider.IsHoldingAsync("test-semaphore", "holder-1");

        // Assert
        isHoldingBefore.ShouldBeFalse();
        isHoldingAfter.ShouldBeTrue();
    }

    private ZooKeeperSemaphoreOptions CreateOptions() => new()
    {
        ConnectionString = ConnectionString,
        RootPath = rootPath,
        SessionTimeout = TimeSpan.FromSeconds(30),
        ConnectionTimeout = TimeSpan.FromSeconds(10),
        AutoCreateRootPath = true
    };

    private async Task<bool> IsZooKeeperAvailableAsync()
    {
        try
        {
            var options = CreateOptions();
            using var provider = new ZooKeeperSemaphoreProvider(Options.Create(options), logger);
            return await provider.HealthCheckAsync();
        }
        catch
        {
            return false;
        }
    }
}


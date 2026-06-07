using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Consul;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("Consul")]
public class ConsulSemaphoreIntegrationTests : IAsyncLifetime
{
    private const string Address = "http://localhost:8500";
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ConsulSemaphoreProvider> logger;
    private readonly string keyPrefix;

    public ConsulSemaphoreIntegrationTests()
    {
        loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<ConsulSemaphoreProvider>();
        keyPrefix = $"test-semaphore-{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        if (!await IsConsulAvailableAsync())
            throw new InvalidOperationException("Consul is not available. Make sure Consul is running on localhost:8500");
    }

    public Task DisposeAsync()
    {
        loggerFactory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task HealthCheck_WithConsul_ShouldReturnTrue()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new ConsulSemaphoreProvider(Options.Create(options), logger);

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
        using var provider = new ConsulSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new ConsulSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 2;

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire a slot");
        (await provider.TryAcquireAsync("test-semaphore", "holder-2", maxCount, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-2 should acquire a slot");

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
        using var provider = new ConsulSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "key", "value" } };

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire the initial slot");

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
        using var provider = new ConsulSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 1;

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire the initial slot");

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
        using var provider = new ConsulSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 5, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire a slot");
        (await provider.TryAcquireAsync("test-semaphore", "holder-2", 5, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-2 should acquire a slot");

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
        using var provider = new ConsulSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        // Act
        bool isHoldingBefore = await provider.IsHoldingAsync("test-semaphore", "holder-1");
        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("holder-1 should acquire a slot");
        bool isHoldingAfter = await provider.IsHoldingAsync("test-semaphore", "holder-1");

        // Assert
        isHoldingBefore.ShouldBeFalse();
        isHoldingAfter.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_ConcurrentAcquirers_NeverExceedsMaxCount()
    {
        // Arrange: many independent providers (simulating separate processes) race to acquire the
        // same semaphore whose capacity is far smaller than the number of contenders. This exercises
        // the optimistic pre-check / post-check rollback path that bounds the holder count.
        const int maxCount = 3;
        const int concurrency = 12;
        const string semaphore = "concurrency-semaphore";
        var metadata = new Dictionary<string, string>();
        var options = CreateOptions();

        var providers = Enumerable
            .Range(0, concurrency)
            .Select(_ => new ConsulSemaphoreProvider(Options.Create(options), logger))
            .ToList();

        try
        {
            // Act: fire all acquisitions concurrently.
            bool[] results = await Task.WhenAll(
                providers.Select((p, i) =>
                    p.TryAcquireAsync(semaphore, $"holder-{i}", maxCount, metadata, TimeSpan.FromMinutes(5))));

            int successes = results.Count(acquired => acquired);

            // Assert: the safety invariant — no more than maxCount callers may hold a slot, and the
            // authoritative live count must agree with the number of successful acquisitions (every
            // rolled-back acquisition has already destroyed its session/key before returning).
            successes.ShouldBeLessThanOrEqualTo(maxCount);

            int count = await providers[0].GetCurrentCountAsync(semaphore, TimeSpan.FromMinutes(5));
            count.ShouldBeLessThanOrEqualTo(maxCount);
            count.ShouldBe(successes, "the live holder count should match the number of successful acquisitions");
        }
        finally
        {
            foreach (ConsulSemaphoreProvider provider in providers)
                await provider.DisposeAsync();
        }
    }

    private ConsulSemaphoreOptions CreateOptions() => new()
    {
        Address = Address,
        KeyPrefix = keyPrefix,
        SessionTtl = TimeSpan.FromSeconds(60),
        SessionLockDelay = TimeSpan.FromSeconds(15)
    };

    private async Task<bool> IsConsulAvailableAsync()
    {
        try
        {
            var options = CreateOptions();
            using var provider = new ConsulSemaphoreProvider(Options.Create(options), logger);
            return await provider.HealthCheckAsync();
        }
        catch
        {
            return false;
        }
    }
}


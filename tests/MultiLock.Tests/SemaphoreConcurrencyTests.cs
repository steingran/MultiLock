using Microsoft.Extensions.Logging;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class SemaphoreConcurrencyTests : IDisposable
{
    private readonly InMemorySemaphoreProvider provider;
    private readonly ILoggerFactory loggerFactory;

    public SemaphoreConcurrencyTests()
    {
        loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        provider = new InMemorySemaphoreProvider(loggerFactory.CreateLogger<InMemorySemaphoreProvider>());
    }

    public void Dispose()
    {
        provider.Dispose();
        loggerFactory.Dispose();
    }

    [Fact]
    public async Task ConcurrentAcquire_ShouldRespectMaxCount()
    {
        // Arrange
        const string semaphoreName = "concurrent-test";
        const int maxCount = 5;
        const int totalHolders = 20;
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);

        // Act - try to acquire from many holders concurrently
        var tasks = Enumerable.Range(0, totalHolders)
            .Select(i => provider.TryAcquireAsync(semaphoreName, $"holder-{i}", maxCount, metadata, slotTimeout))
            .ToList();

        bool[] results = await Task.WhenAll(tasks);

        // Assert - exactly maxCount should succeed
        int successCount = results.Count(r => r);
        successCount.ShouldBe(maxCount);
    }

    [Fact]
    public async Task ConcurrentAcquireAndRelease_ShouldMaintainConsistency()
    {
        // Arrange
        const string semaphoreName = "acquire-release-test";
        const int maxCount = 3;
        const int iterations = 50;
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);

        // Act - acquire and release in parallel
        var tasks = Enumerable.Range(0, iterations).Select(async i =>
        {
            string holderId = $"holder-{i}";
            bool acquired = await provider.TryAcquireAsync(semaphoreName, holderId, maxCount, metadata, slotTimeout);
            if (acquired)
            {
                await Task.Delay(Random.Shared.Next(1, 10)); // Simulate work
                await provider.ReleaseAsync(semaphoreName, holderId);
            }
            return acquired;
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert - semaphore should be empty after all releases
        int finalCount = await provider.GetCurrentCountAsync(semaphoreName, slotTimeout);
        finalCount.ShouldBe(0);
    }

    [Fact]
    public async Task ConcurrentGetCurrentCount_ShouldBeThreadSafe()
    {
        // Arrange
        const string semaphoreName = "count-test";
        const int maxCount = 10;
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);

        // Acquire some slots
        for (int i = 0; i < 5; i++)
            await provider.TryAcquireAsync(semaphoreName, $"holder-{i}", maxCount, metadata, slotTimeout);

        // Act - read count from many threads concurrently
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => provider.GetCurrentCountAsync(semaphoreName, slotTimeout))
            .ToList();

        int[] counts = await Task.WhenAll(tasks);

        // Assert - all reads should return the same value
        counts.ShouldAllBe(c => c == 5);
    }

    [Fact]
    public async Task ConcurrentIsHolding_ShouldBeThreadSafe()
    {
        // Arrange
        const string semaphoreName = "holding-test";
        const int maxCount = 10;
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireAsync(semaphoreName, "holder-1", maxCount, metadata, slotTimeout);

        // Act - check holding status from many threads concurrently
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => provider.IsHoldingAsync(semaphoreName, "holder-1"))
            .ToList();

        bool[] results = await Task.WhenAll(tasks);

        // Assert - all reads should return true
        results.ShouldAllBe(r => r);
    }

    [Fact]
    public async Task ConcurrentGetHolders_ShouldBeThreadSafe()
    {
        // Arrange
        const string semaphoreName = "holders-test";
        const int maxCount = 10;
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);

        for (int i = 0; i < 5; i++)
            await provider.TryAcquireAsync(semaphoreName, $"holder-{i}", maxCount, metadata, slotTimeout);

        // Act - get holders from many threads concurrently
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => provider.GetHoldersAsync(semaphoreName))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert - all reads should return 5 holders
        results.ShouldAllBe(r => r.Count == 5);
    }

    [Fact]
    public async Task RapidAcquireReleaseCycles_ShouldNotDeadlock()
    {
        // Arrange
        const string semaphoreName = "rapid-cycle-test";
        const int maxCount = 2;
        const int cycles = 100;
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act - rapid acquire/release cycles from multiple holders
        var tasks = Enumerable.Range(0, 4).Select(async holderIndex =>
        {
            string holderId = $"holder-{holderIndex}";
            for (int i = 0; i < cycles && !cts.Token.IsCancellationRequested; i++)
            {
                await provider.TryAcquireAsync(semaphoreName, holderId, maxCount, metadata, slotTimeout, cts.Token);
                await provider.ReleaseAsync(semaphoreName, holderId, cts.Token);
            }
        }).ToList();

        // Assert - should complete without deadlock
        await Task.WhenAll(tasks);
    }
}


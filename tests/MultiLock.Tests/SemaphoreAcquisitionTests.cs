using Microsoft.Extensions.Logging;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class SemaphoreAcquisitionTests : IDisposable
{
    private readonly InMemorySemaphoreProvider provider;
    private readonly ILoggerFactory loggerFactory;
    private const string SemaphoreName = "acq-test";
    private const string HolderId = "holder-1";
    private readonly IReadOnlyDictionary<string, string> metadata = new Dictionary<string, string>();

    public SemaphoreAcquisitionTests()
    {
        loggerFactory = new LoggerFactory();
        provider = new InMemorySemaphoreProvider(loggerFactory.CreateLogger<InMemorySemaphoreProvider>());
    }

    public void Dispose()
    {
        provider.Dispose();
        loggerFactory.Dispose();
    }

    private SemaphoreAcquisition CreateAcquisition() =>
        new(provider, SemaphoreName, HolderId, DateTimeOffset.UtcNow, metadata);

    [Fact]
    public void IsDisposed_BeforeDispose_ShouldReturnFalse()
    {
        var acquisition = CreateAcquisition();
        acquisition.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task DisposeAsync_ShouldSetIsDisposedTrue()
    {
        var acquisition = CreateAcquisition();
        await acquisition.DisposeAsync();
        acquisition.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_ShouldSetIsDisposedTrue()
    {
        var acquisition = CreateAcquisition();
        acquisition.Dispose();
        acquisition.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_ShouldReleaseSlotFromProvider()
    {
        // Acquire a slot first so the provider knows about this holder
        await provider.TryAcquireAsync(SemaphoreName, HolderId, 3, metadata, TimeSpan.FromMinutes(5));
        bool holdingBefore = await provider.IsHoldingAsync(SemaphoreName, HolderId);
        holdingBefore.ShouldBeTrue();

        var acquisition = CreateAcquisition();
        await acquisition.DisposeAsync();

        bool holdingAfter = await provider.IsHoldingAsync(SemaphoreName, HolderId);
        holdingAfter.ShouldBeFalse();
    }

    [Fact]
    public async Task Dispose_ShouldReleaseSlotFromProvider()
    {
        await provider.TryAcquireAsync(SemaphoreName, HolderId, 3, metadata, TimeSpan.FromMinutes(5));

        var acquisition = CreateAcquisition();
        acquisition.Dispose();

        bool holding = await provider.IsHoldingAsync(SemaphoreName, HolderId);
        holding.ShouldBeFalse();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_ShouldNotDoubleRelease()
    {
        // Acquire slot for holder-1, then dispose twice; verify second dispose doesn't throw
        // and count stays correct
        const string otherHolder = "holder-2";
        const int maxCount = 1;
        await provider.TryAcquireAsync(SemaphoreName, HolderId, maxCount, metadata, TimeSpan.FromMinutes(5));

        var acquisition = CreateAcquisition();
        await acquisition.DisposeAsync(); // releases holder-1
        await acquisition.DisposeAsync(); // second call is a no-op

        // holder-2 should be able to acquire since the slot is free
        bool acquired = await provider.TryAcquireAsync(SemaphoreName, otherHolder, maxCount, metadata, TimeSpan.FromMinutes(5));
        acquired.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCalls_ShouldNotDoubleRelease()
    {
        // Arrange: acquire a slot then fire many concurrent DisposeAsync calls.
        // The volatile-bool guard has a read-then-write pattern that is not atomically
        // compare-and-swap, so multiple callers may race past the isDisposed check.
        // The test verifies that even in the worst case the semaphore ends up in a
        // consistent state: the slot is released exactly once, the acquisition is
        // marked disposed, and no exception escapes.
        const string otherHolder = "holder-2";
        const int maxCount = 1;
        const int concurrency = 8;

        await provider.TryAcquireAsync(SemaphoreName, HolderId, maxCount, metadata, TimeSpan.FromMinutes(5));
        var acquisition = CreateAcquisition();

        // Act: Task.WhenAll rethrows the first faulted task; any unexpected exception
        // here is a test failure (DisposeAsync is specified never to throw).
        Task[] disposeTasks = Enumerable
            .Range(0, concurrency)
            .Select(_ => acquisition.DisposeAsync().AsTask())
            .ToArray();
        await Task.WhenAll(disposeTasks);

        // Assert: the acquisition must be marked disposed regardless of which task won.
        acquisition.IsDisposed.ShouldBeTrue();

        // The slot must be free: no phantom holders left from double-release races.
        int count = await provider.GetCurrentCountAsync(SemaphoreName, TimeSpan.FromMinutes(5));
        count.ShouldBe(0, "holder-1 slot should be fully released after concurrent disposal");

        // A new holder should be able to acquire the now-free slot.
        bool acquired = await provider.TryAcquireAsync(
            SemaphoreName, otherHolder, maxCount, metadata, TimeSpan.FromMinutes(5));
        acquired.ShouldBeTrue("slot should be available for a new holder after concurrent disposal");
    }

    [Fact]
    public void Properties_ShouldBeSetCorrectly()
    {
        var acquiredAt = DateTimeOffset.UtcNow;
        var meta = new Dictionary<string, string> { { "k", "v" } };
        var acquisition = new SemaphoreAcquisition(provider, SemaphoreName, HolderId, acquiredAt, meta);

        acquisition.HolderId.ShouldBe(HolderId);
        acquisition.AcquiredAt.ShouldBe(acquiredAt);
        acquisition.Metadata["k"].ShouldBe("v");
    }
}


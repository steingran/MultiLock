using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class SemaphoreServiceTests : IAsyncLifetime
{
    private readonly ILoggerFactory loggerFactory;
    private readonly InMemorySemaphoreProvider provider;
    private SemaphoreService? service;

    public SemaphoreServiceTests()
    {
        loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        provider = new InMemorySemaphoreProvider(loggerFactory.CreateLogger<InMemorySemaphoreProvider>());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (service != null)
            await service.DisposeAsync();
        provider.Dispose();
        loggerFactory.Dispose();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreEmpty_ShouldSucceed()
    {
        // Arrange
        service = CreateService("test-semaphore", 3);

        // Act
        bool acquired = await service.TryAcquireAsync();

        // Assert
        acquired.ShouldBeTrue();
        service.IsHolding.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreFull_ShouldFail()
    {
        // Arrange
        const int maxCount = 2;
        service = CreateService("test-semaphore", maxCount);
        var emptyMetadata = new Dictionary<string, string>();

        // Fill the semaphore with other holders
        await provider.TryAcquireAsync("test-semaphore", "other-1", maxCount, emptyMetadata, TimeSpan.FromMinutes(5));
        await provider.TryAcquireAsync("test-semaphore", "other-2", maxCount, emptyMetadata, TimeSpan.FromMinutes(5));

        // Act
        bool acquired = await service.TryAcquireAsync();

        // Assert
        acquired.ShouldBeFalse();
        service.IsHolding.ShouldBeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_ShouldReleaseSlot()
    {
        // Arrange
        service = CreateService("test-semaphore", 3);
        await service.TryAcquireAsync();
        service.IsHolding.ShouldBeTrue();

        // Act
        await service.ReleaseAsync();

        // Assert
        service.IsHolding.ShouldBeFalse();
    }

    [Fact]
    public async Task CurrentStatus_ShouldReflectHoldingState()
    {
        // Arrange
        service = CreateService("test-semaphore", 3);

        // Act & Assert - before acquiring
        service.CurrentStatus.IsHolding.ShouldBeFalse();

        await service.TryAcquireAsync();

        // After acquiring
        service.CurrentStatus.IsHolding.ShouldBeTrue();
        service.CurrentStatus.CurrentCount.ShouldBe(1);
    }

    [Fact]
    public async Task HolderId_ShouldBeSetFromOptions()
    {
        // Arrange
        const string expectedHolderId = "custom-holder-id";
        var options = new SemaphoreOptions
        {
            SemaphoreName = "test-semaphore",
            MaxCount = 3,
            HolderId = expectedHolderId,
            AutoStart = false
        };
        service = new SemaphoreService(provider, Options.Create(options), loggerFactory.CreateLogger<SemaphoreService>());

        // Assert
        service.HolderId.ShouldBe(expectedHolderId);
    }

    [Fact]
    public async Task SemaphoreName_ShouldBeSetFromOptions()
    {
        // Arrange
        const string expectedName = "my-semaphore";
        service = CreateService(expectedName, 3);

        // Assert
        service.SemaphoreName.ShouldBe(expectedName);
    }

    [Fact]
    public async Task MaxCount_ShouldBeSetFromOptions()
    {
        // Arrange
        const int expectedMaxCount = 5;
        service = CreateService("test-semaphore", expectedMaxCount);

        // Assert
        service.MaxCount.ShouldBe(expectedMaxCount);
    }

    [Fact]
    public async Task GetStatusChangesAsync_ShouldEmitEventsOnAcquire()
    {
        // Arrange
        service = CreateService("test-semaphore", 3);
        var events = new List<SemaphoreChangedEventArgs>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Obtain the enumerator and advance it once. GetStatusChangesAsyncCoreImpl
        // registers the subscriber channel synchronously (before its first await),
        // so by the time MoveNextAsync() suspends the subscription is already in place.
        // No Task.Delay is needed to coordinate ordering.
        IAsyncEnumerator<SemaphoreChangedEventArgs> enumerator =
            service.GetStatusChangesAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        ValueTask<bool> pendingMove = enumerator.MoveNextAsync();

        // Act - subscription is registered; events produced here will be delivered.
        await service.TryAcquireAsync();

        // Drain events until we observe the holding transition.
        try
        {
            while (await pendingMove)
            {
                events.Add(enumerator.Current);
                if (enumerator.Current.CurrentStatus.IsHolding)
                    break;
                pendingMove = enumerator.MoveNextAsync();
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        // Assert
        events.ShouldNotBeEmpty();
        events.ShouldContain(e => e.CurrentStatus.IsHolding);
    }

    [Fact]
    public async Task WaitForSlotAsync_WhenAlreadyHolding_ShouldReturnImmediately()
    {
        service = CreateService("test-semaphore", 3);
        await service.TryAcquireAsync();
        // Should return immediately without subscribing to events
        await service.WaitForSlotAsync();
        service.IsHolding.ShouldBeTrue();
    }

    [Fact]
    public async Task WaitForSlotAsync_WithTimeout_WhenSlotAcquired_ShouldReturnTrue()
    {
        service = CreateService("test-semaphore", 3);

        // WaitForSlotAsync calls GetStatusChangesAsync internally. The implementation
        // of GetStatusChangesAsyncCoreImpl registers the subscriber channel into
        // subscriberChannels synchronously (before any await), so by the time the
        // returned Task<bool> suspends the subscription is already in place.
        // Calling TryAcquireAsync immediately after is therefore safe with no delay.
        Task<bool> waitTask = service.WaitForSlotAsync(TimeSpan.FromSeconds(5));

        await service.TryAcquireAsync();

        bool result = await waitTask;
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task WaitForSlotAsync_WithTimeout_WhenTimeoutExpires_ShouldReturnFalse()
    {
        // Fill the semaphore so no slot is available
        const int maxCount = 1;
        service = CreateService("test-semaphore", maxCount);
        var emptyMeta = new Dictionary<string, string>();
        await provider.TryAcquireAsync("test-semaphore", "other", maxCount, emptyMeta, TimeSpan.FromMinutes(5));

        // Try with a very short timeout — should return false
        bool result = await service.WaitForSlotAsync(TimeSpan.FromMilliseconds(50));
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task GetSemaphoreInfoAsync_ShouldReturnCurrentInfo()
    {
        service = CreateService("test-semaphore", 5);
        await service.TryAcquireAsync();

        SemaphoreInfo? info = await service.GetSemaphoreInfoAsync();

        info.ShouldNotBeNull();
        info.SemaphoreName.ShouldBe("test-semaphore");
        info.MaxCount.ShouldBe(5);
        info.CurrentCount.ShouldBeGreaterThanOrEqualTo(1);
        info.Holders.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task StopAsync_WhenHolding_ShouldRelease()
    {
        service = CreateService("test-semaphore", 3);
        await service.TryAcquireAsync();
        service.IsHolding.ShouldBeTrue();

        await service.StopAsync();

        service.IsHolding.ShouldBeFalse();
    }

    [Fact]
    public async Task StopAsync_WhenNotHolding_ShouldNotThrow()
    {
        service = CreateService("test-semaphore", 3);
        await Should.NotThrowAsync(() => service.StopAsync());
    }

    [Fact]
    public async Task StartAsync_WithAutoStartFalse_ShouldNotAcquireSlot()
    {
        service = CreateService("test-semaphore", 3);
        await service.StartAsync();
        service.IsHolding.ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_WithAutoStartTrue_ShouldAcquireSlot()
    {
        var options = new SemaphoreOptions
        {
            SemaphoreName = "test-semaphore",
            MaxCount = 3,
            AutoStart = true,
            HeartbeatInterval = TimeSpan.FromSeconds(10),
            HeartbeatTimeout = TimeSpan.FromSeconds(30),
            AcquisitionInterval = TimeSpan.FromSeconds(5)
        };
        service = new SemaphoreService(provider, Options.Create(options), loggerFactory.CreateLogger<SemaphoreService>());
        await service.StartAsync();
        service.IsHolding.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        service = CreateService("test-semaphore", 3);
        await Should.NotThrowAsync(() => service.DisposeAsync().AsTask());
        service = null; // prevent double-dispose in teardown
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_ShouldNotThrow()
    {
        service = CreateService("test-semaphore", 3);
        await service.DisposeAsync();
        await Should.NotThrowAsync(() => service.DisposeAsync().AsTask());
        service = null;
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        service = CreateService("test-semaphore", 3);
        Should.NotThrow(() => service.Dispose());
        service = null;
    }

    [Fact]
    public async Task PublicMethods_AfterDispose_ShouldThrowObjectDisposedException()
    {
        service = CreateService("test-semaphore", 3);
        await service.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.TryAcquireAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ReleaseAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.WaitForSlotAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.WaitForSlotAsync(TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.GetSemaphoreInfoAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync());
        service = null;
    }

    [Fact]
    public async Task WaitForSlotAsync_WhenServiceDisposedWhileWaiting_ShouldThrowObjectDisposedException()
    {
        // Fill the semaphore so WaitForSlotAsync must block waiting for a slot.
        const int maxCount = 1;
        service = CreateService("test-semaphore", maxCount);
        var emptyMeta = new Dictionary<string, string>();
        await provider.TryAcquireAsync("test-semaphore", "other", maxCount, emptyMeta, TimeSpan.FromMinutes(5));

        Task waitTask = service.WaitForSlotAsync();

        // Dispose while WaitForSlotAsync is blocking — it should unblock and throw.
        await service.DisposeAsync();
        service = null;

        await Assert.ThrowsAsync<ObjectDisposedException>(() => waitTask);
    }

    [Fact]
    public async Task ReleaseAsync_WhenNotHolding_ShouldBeNoOp()
    {
        service = CreateService("test-semaphore", 3);
        // Should not throw; IsHolding stays false
        await service.ReleaseAsync();
        service.IsHolding.ShouldBeFalse();
    }

    private SemaphoreService CreateService(string semaphoreName, int maxCount) =>
        new(provider, Options.Create(new SemaphoreOptions
        {
            SemaphoreName = semaphoreName,
            MaxCount = maxCount,
            AutoStart = false
        }), loggerFactory.CreateLogger<SemaphoreService>());
}


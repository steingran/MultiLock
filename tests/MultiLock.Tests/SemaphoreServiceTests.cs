using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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

    [Fact]
    public async Task AcquireScopeAsync_WhenSlotAvailable_ReturnsScopeAndReleasesOnDispose()
    {
        // Arrange
        service = CreateService("test-semaphore", 1);

        // Act
        SemaphoreAcquisition? scope = await service.AcquireScopeAsync();

        // Assert: the scope reflects a held slot.
        scope.ShouldNotBeNull();
        scope!.HolderId.ShouldBe(service.HolderId);
        service.IsHolding.ShouldBeTrue();
        (await provider.GetCurrentCountAsync("test-semaphore", TimeSpan.FromSeconds(30))).ShouldBe(1);

        // Disposing the scope releases through the service.
        await scope.DisposeAsync();

        service.IsHolding.ShouldBeFalse();
        (await provider.GetCurrentCountAsync("test-semaphore", TimeSpan.FromSeconds(30))).ShouldBe(0);
    }

    [Fact]
    public async Task AcquireScopeAsync_WhenSemaphoreFull_ReturnsNull()
    {
        // Arrange
        const int maxCount = 1;
        service = CreateService("test-semaphore", maxCount);
        await provider.TryAcquireAsync("test-semaphore", "other-1", maxCount, new Dictionary<string, string>(), TimeSpan.FromMinutes(5));

        // Act
        SemaphoreAcquisition? scope = await service.AcquireScopeAsync();

        // Assert
        scope.ShouldBeNull();
        service.IsHolding.ShouldBeFalse();
    }

    [Fact]
    public async Task WaitForSlotAsync_WhenSlotBecomesAvailable_ReturnsOnceAcquired()
    {
        // Arrange: not holding yet; begin waiting (subscription is registered synchronously).
        service = CreateService("test-semaphore", 1);
        Task waitTask = service.WaitForSlotAsync();

        // Act: acquiring broadcasts an AcquiredSlot transition that the waiter observes.
        (await service.TryAcquireAsync()).ShouldBeTrue();

        // Assert: the wait completes promptly rather than blocking forever.
        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
        service.IsHolding.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenProviderThrowsTransiently_RetriesThenSucceeds()
    {
        // Arrange: provider fails twice then succeeds, exercising the retry + backoff path.
        var mockProvider = new Mock<ISemaphoreProvider>();
        mockProvider.SetupSequence(p => p.TryAcquireAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient-1"))
            .ThrowsAsync(new InvalidOperationException("transient-2"))
            .ReturnsAsync(true);
        mockProvider.Setup(p => p.GetCurrentCountAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await using var svc = new SemaphoreService(
            mockProvider.Object,
            Options.Create(new SemaphoreOptions
            {
                SemaphoreName = "retry-semaphore",
                MaxCount = 1,
                AutoStart = false,
                MaxRetryAttempts = 3,
                RetryBaseDelay = TimeSpan.FromMilliseconds(1),
                RetryMaxDelay = TimeSpan.FromMilliseconds(10),
                EnableDetailedLogging = true
            }),
            NullLogger<SemaphoreService>.Instance);

        // Act
        bool acquired = await svc.TryAcquireAsync();

        // Assert: succeeded on the third attempt.
        acquired.ShouldBeTrue();
        mockProvider.Verify(p => p.TryAcquireAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task TryAcquireAsync_WhenProviderAlwaysThrows_RethrowsAfterExhaustingRetries()
    {
        // Arrange: provider always throws; the service should retry then surface the last exception.
        var mockProvider = new Mock<ISemaphoreProvider>();
        mockProvider.Setup(p => p.TryAcquireAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await using var svc = new SemaphoreService(
            mockProvider.Object,
            Options.Create(new SemaphoreOptions
            {
                SemaphoreName = "retry-semaphore",
                MaxCount = 1,
                AutoStart = false,
                MaxRetryAttempts = 2,
                RetryBaseDelay = TimeSpan.FromMilliseconds(1),
                RetryMaxDelay = TimeSpan.FromMilliseconds(10)
            }),
            NullLogger<SemaphoreService>.Instance);

        // Act & Assert: the original exception propagates after MaxRetryAttempts + 1 tries.
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.TryAcquireAsync());
        mockProvider.Verify(p => p.TryAcquireAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task AutoStart_HeartbeatTimerFires_KeepsSlotHeld()
    {
        // Arrange: short heartbeat interval so the heartbeat timer callback actually fires.
        var options = new SemaphoreOptions
        {
            SemaphoreName = "heartbeat-semaphore",
            MaxCount = 1,
            AutoStart = true,
            HeartbeatInterval = TimeSpan.FromMilliseconds(50),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(250),
            AcquisitionInterval = TimeSpan.FromMilliseconds(50),
            EnableDetailedLogging = true
        };
        service = new SemaphoreService(provider, Options.Create(options), loggerFactory.CreateLogger<SemaphoreService>());

        // Act: AutoStart acquires immediately, then the heartbeat timer renews the slot repeatedly.
        await service.StartAsync();
        await WaitUntilAsync(() => service.IsHolding, TimeSpan.FromSeconds(5));

        // Assert: after several heartbeat intervals the slot is still held (renewals succeeded).
        await Task.Delay(250);
        service.IsHolding.ShouldBeTrue();
    }

    [Fact]
    public async Task AutoStart_WhenInitiallyFull_AcquisitionTimerAcquiresOnceSlotFrees()
    {
        // Arrange: occupy the only slot, then start an auto-starting service that must wait.
        const string semaphore = "acquisition-semaphore";
        var meta = new Dictionary<string, string>();
        await provider.TryAcquireAsync(semaphore, "other-holder", 1, meta, TimeSpan.FromMinutes(5));

        var options = new SemaphoreOptions
        {
            SemaphoreName = semaphore,
            MaxCount = 1,
            AutoStart = true,
            HeartbeatInterval = TimeSpan.FromMilliseconds(50),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(250),
            AcquisitionInterval = TimeSpan.FromMilliseconds(50)
        };
        service = new SemaphoreService(provider, Options.Create(options), loggerFactory.CreateLogger<SemaphoreService>());

        // Act: the acquisition timer keeps retrying while the semaphore is full.
        await service.StartAsync();
        await Task.Delay(150);
        service.IsHolding.ShouldBeFalse();

        // Free the slot; the acquisition timer should pick it up on a subsequent tick.
        await provider.ReleaseAsync(semaphore, "other-holder");

        // Assert
        await WaitUntilAsync(() => service.IsHolding, TimeSpan.FromSeconds(5));
        service.IsHolding.ShouldBeTrue();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            if (cts.IsCancellationRequested)
                throw new TimeoutException("Condition was not met within the allotted time.");
            await Task.Delay(20);
        }
    }

    private SemaphoreService CreateService(string semaphoreName, int maxCount) =>
        new(provider, Options.Create(new SemaphoreOptions
        {
            SemaphoreName = semaphoreName,
            MaxCount = maxCount,
            AutoStart = false
        }), loggerFactory.CreateLogger<SemaphoreService>());
}


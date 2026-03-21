using Microsoft.Extensions.Logging;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class InMemorySemaphoreProviderTests : IDisposable
{
    private readonly InMemorySemaphoreProvider provider;
    private readonly ILoggerFactory loggerFactory;

    public InMemorySemaphoreProviderTests()
    {
        loggerFactory = new LoggerFactory();
        ILogger<InMemorySemaphoreProvider> logger = loggerFactory.CreateLogger<InMemorySemaphoreProvider>();
        provider = new InMemorySemaphoreProvider(logger);
    }

    public void Dispose()
    {
        provider.Dispose();
        loggerFactory.Dispose();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreEmpty_ShouldSucceed()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        const string holderId = "holder-1";
        var metadata = new Dictionary<string, string> { { "key", "value" } };
        var slotTimeout = TimeSpan.FromMinutes(5);

        // Act
        bool result = await provider.TryAcquireAsync(semaphoreName, holderId, 3, metadata, slotTimeout);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreFull_ShouldFail()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);
        const int maxCount = 2;

        // Fill the semaphore
        await provider.TryAcquireAsync(semaphoreName, "holder-1", maxCount, metadata, slotTimeout);
        await provider.TryAcquireAsync(semaphoreName, "holder-2", maxCount, metadata, slotTimeout);

        // Act
        bool result = await provider.TryAcquireAsync(semaphoreName, "holder-3", maxCount, metadata, slotTimeout);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenHolderAlreadyHasSlot_ShouldRenew()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        const string holderId = "holder-1";
        var metadata = new Dictionary<string, string> { { "key", "value" } };
        var slotTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireAsync(semaphoreName, holderId, 3, metadata, slotTimeout);

        // Act - try to acquire again with same holder
        bool result = await provider.TryAcquireAsync(semaphoreName, holderId, 3, metadata, slotTimeout);

        // Assert
        result.ShouldBeTrue();
        int count = await provider.GetCurrentCountAsync(semaphoreName, slotTimeout);
        count.ShouldBe(1); // Should still be 1, not 2
    }

    [Fact]
    public async Task ReleaseAsync_WhenHolding_ShouldRelease()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        const string holderId = "holder-1";
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireAsync(semaphoreName, holderId, 3, metadata, slotTimeout);

        // Act
        await provider.ReleaseAsync(semaphoreName, holderId);

        // Assert
        bool isHolding = await provider.IsHoldingAsync(semaphoreName, holderId);
        isHolding.ShouldBeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_WhenNotHolding_ShouldNotThrow()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        const string holderId = "holder-1";

        // Act & Assert - should not throw
        await provider.ReleaseAsync(semaphoreName, holderId);
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenHolding_ShouldSucceed()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        const string holderId = "holder-1";
        var metadata = new Dictionary<string, string> { { "key", "value" } };
        var slotTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireAsync(semaphoreName, holderId, 3, metadata, slotTimeout);

        var updatedMetadata = new Dictionary<string, string> { { "key", "updated-value" } };

        // Act
        bool result = await provider.UpdateHeartbeatAsync(semaphoreName, holderId, updatedMetadata);

        // Assert
        result.ShouldBeTrue();
        IReadOnlyList<SemaphoreHolder> holders = await provider.GetHoldersAsync(semaphoreName);
        holders.Count.ShouldBe(1);
        holders[0].Metadata["key"].ShouldBe("updated-value");
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenNotHolding_ShouldFail()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        const string holderId = "holder-1";
        var metadata = new Dictionary<string, string>();

        // Act
        bool result = await provider.UpdateHeartbeatAsync(semaphoreName, holderId, metadata);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCurrentCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireAsync(semaphoreName, "holder-1", 5, metadata, slotTimeout);
        await provider.TryAcquireAsync(semaphoreName, "holder-2", 5, metadata, slotTimeout);

        // Act
        int count = await provider.GetCurrentCountAsync(semaphoreName, slotTimeout);

        // Assert
        count.ShouldBe(2);
    }

    [Fact]
    public async Task GetCurrentCountAsync_WhenNoHolders_ShouldReturnZero()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";

        // Act
        int count = await provider.GetCurrentCountAsync(semaphoreName, TimeSpan.FromMinutes(5));

        // Assert
        count.ShouldBe(0);
    }

    [Fact]
    public async Task GetHoldersAsync_ShouldReturnAllHolders()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        var metadata1 = new Dictionary<string, string> { { "holder", "1" } };
        var metadata2 = new Dictionary<string, string> { { "holder", "2" } };
        var slotTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireAsync(semaphoreName, "holder-1", 5, metadata1, slotTimeout);
        await provider.TryAcquireAsync(semaphoreName, "holder-2", 5, metadata2, slotTimeout);

        // Act
        IReadOnlyList<SemaphoreHolder> holders = await provider.GetHoldersAsync(semaphoreName);

        // Assert
        holders.Count.ShouldBe(2);
        holders.ShouldContain(h => h.HolderId == "holder-1");
        holders.ShouldContain(h => h.HolderId == "holder-2");
    }

    [Fact]
    public async Task GetHoldersAsync_WhenNoHolders_ShouldReturnEmptyList()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";

        // Act
        IReadOnlyList<SemaphoreHolder> holders = await provider.GetHoldersAsync(semaphoreName);

        // Assert
        holders.ShouldBeEmpty();
    }

    [Fact]
    public async Task IsHoldingAsync_WhenHolding_ShouldReturnTrue()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        const string holderId = "holder-1";
        var metadata = new Dictionary<string, string>();
        var slotTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireAsync(semaphoreName, holderId, 3, metadata, slotTimeout);

        // Act
        bool isHolding = await provider.IsHoldingAsync(semaphoreName, holderId);

        // Assert
        isHolding.ShouldBeTrue();
    }

    [Fact]
    public async Task IsHoldingAsync_WhenNotHolding_ShouldReturnFalse()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        const string holderId = "holder-1";

        // Act
        bool isHolding = await provider.IsHoldingAsync(semaphoreName, holderId);

        // Assert
        isHolding.ShouldBeFalse();
    }

    [Fact]
    public async Task HealthCheckAsync_ShouldReturnTrue()
    {
        // Act
        bool isHealthy = await provider.HealthCheckAsync();

        // Assert
        isHealthy.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSlotExpired_ShouldAllowNewHolder()
    {
        // Arrange
        const string semaphoreName = "test-semaphore";
        var metadata = new Dictionary<string, string>();
        var shortTimeout = TimeSpan.FromMilliseconds(100);
        const int maxCount = 1;

        // First holder acquires with short timeout
        await provider.TryAcquireAsync(semaphoreName, "holder-1", maxCount, metadata, shortTimeout);

        // Poll until the slot is considered expired or the deadline is reached.
        // Using a bounded loop avoids fragile fixed delays that fail on slow CI machines.
        bool result = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!deadline.Token.IsCancellationRequested)
        {
            result = await provider.TryAcquireAsync(semaphoreName, "holder-2", maxCount, metadata, shortTimeout);
            if (result)
                break;
            await Task.Delay(10, deadline.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var testProvider = new InMemorySemaphoreProvider(
            loggerFactory.CreateLogger<InMemorySemaphoreProvider>());

        // Act & Assert
        testProvider.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var testProvider = new InMemorySemaphoreProvider(
            loggerFactory.CreateLogger<InMemorySemaphoreProvider>());

        // Act & Assert
        testProvider.Dispose(); // First dispose
        testProvider.Dispose(); // Second dispose should not throw
    }

    [Fact]
    public async Task MethodsAfterDispose_ShouldThrow()
    {
        // Arrange
        var testProvider = new InMemorySemaphoreProvider(
            loggerFactory.CreateLogger<InMemorySemaphoreProvider>());

        testProvider.Dispose();

        var metadata = new Dictionary<string, string>();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => testProvider.TryAcquireAsync("test", "holder", 3, metadata, TimeSpan.FromMinutes(1)));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => testProvider.ReleaseAsync("test", "holder"));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => testProvider.UpdateHeartbeatAsync("test", "holder", metadata));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => testProvider.GetCurrentCountAsync("test", TimeSpan.FromMinutes(5)));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => testProvider.GetHoldersAsync("test"));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => testProvider.IsHoldingAsync("test", "holder"));
    }

    // Parameter validation tests

    [Fact]
    public async Task TryAcquireAsync_WithNullSemaphoreName_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync(null!, "holder", 1, new Dictionary<string, string>(), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_WithEmptySemaphoreName_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync("", "holder", 1, new Dictionary<string, string>(), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_WithNullHolderId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync("sem", null!, 1, new Dictionary<string, string>(), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_WithInvalidMaxCount_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync("sem", "h", 0, new Dictionary<string, string>(), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_WithZeroSlotTimeout_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync("sem", "h", 1, new Dictionary<string, string>(), TimeSpan.Zero));
    }

    [Fact]
    public async Task ReleaseAsync_WithNullSemaphoreName_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ReleaseAsync(null!, "holder"));
    }

    [Fact]
    public async Task ReleaseAsync_WithNullHolderId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ReleaseAsync("sem", null!));
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WithNullSemaphoreName_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.UpdateHeartbeatAsync(null!, "holder", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WithNullHolderId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.UpdateHeartbeatAsync("sem", null!, new Dictionary<string, string>()));
    }

    [Fact]
    public async Task GetCurrentCountAsync_WithNullSemaphoreName_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetCurrentCountAsync(null!, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task GetCurrentCountAsync_WithInvalidSlotTimeout_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetCurrentCountAsync("sem", TimeSpan.Zero));
    }

    [Fact]
    public async Task GetHoldersAsync_WithNullSemaphoreName_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetHoldersAsync(null!));
    }

    [Fact]
    public async Task IsHoldingAsync_WithNullSemaphoreName_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.IsHoldingAsync(null!, "holder"));
    }

    [Fact]
    public async Task IsHoldingAsync_WithNullHolderId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.IsHoldingAsync("sem", null!));
    }
}


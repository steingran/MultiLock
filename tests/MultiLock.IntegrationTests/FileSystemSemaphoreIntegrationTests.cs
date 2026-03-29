using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.FileSystem;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

public class FileSystemSemaphoreIntegrationTests : IAsyncLifetime
{
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<FileSystemSemaphoreProvider> logger;
    private readonly string directoryPath;

    public FileSystemSemaphoreIntegrationTests()
    {
        loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<FileSystemSemaphoreProvider>();
        directoryPath = Path.Combine(Path.GetTempPath(), $"test-semaphore-{Guid.NewGuid():N}");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        loggerFactory.Dispose();
        // Clean up test directory
        if (Directory.Exists(directoryPath))
            Directory.Delete(directoryPath, true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task HealthCheck_WithFileSystem_ShouldReturnTrue()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new FileSystemSemaphoreProvider(Options.Create(options), logger);

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
        using var provider = new FileSystemSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new FileSystemSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 2;

        await provider.TryAcquireAsync("test-semaphore", "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5));
        await provider.TryAcquireAsync("test-semaphore", "holder-2", maxCount, metadata, TimeSpan.FromMinutes(5));

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
        using var provider = new FileSystemSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5));

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
        using var provider = new FileSystemSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();
        const int maxCount = 1;

        await provider.TryAcquireAsync("test-semaphore", "holder-1", maxCount, metadata, TimeSpan.FromMinutes(5));

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
        using var provider = new FileSystemSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        await provider.TryAcquireAsync("test-semaphore", "holder-1", 5, metadata, TimeSpan.FromMinutes(5));
        await provider.TryAcquireAsync("test-semaphore", "holder-2", 5, metadata, TimeSpan.FromMinutes(5));

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
        using var provider = new FileSystemSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        // Act
        bool isHoldingBefore = await provider.IsHoldingAsync("test-semaphore", "holder-1");
        await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5));
        bool isHoldingAfter = await provider.IsHoldingAsync("test-semaphore", "holder-1");

        // Assert
        isHoldingBefore.ShouldBeFalse();
        isHoldingAfter.ShouldBeTrue();
    }

    private FileSystemSemaphoreOptions CreateOptions() => new()
    {
        DirectoryPath = directoryPath,
        FileExtension = ".holder",
        AutoCreateDirectory = true
    };
}


using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.AzureBlobStorage;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

[Collection("AzureBlobStorage")]
public class AzureBlobStorageSemaphoreIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<AzureBlobStorageSemaphoreProvider> logger;
    private readonly string containerName;

    public AzureBlobStorageSemaphoreIntegrationTests()
    {
        loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<AzureBlobStorageSemaphoreProvider>();
        containerName = $"test-semaphore-{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        if (!await IsAzuriteAvailableAsync())
            throw new InvalidOperationException("Azurite is not available. Make sure Azurite is running on localhost:10000");
    }

    public Task DisposeAsync()
    {
        loggerFactory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task HealthCheck_WithAzurite_ShouldReturnTrue()
    {
        // Arrange
        var options = CreateOptions();
        using var provider = new AzureBlobStorageSemaphoreProvider(Options.Create(options), logger);

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
        using var provider = new AzureBlobStorageSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new AzureBlobStorageSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new AzureBlobStorageSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new AzureBlobStorageSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new AzureBlobStorageSemaphoreProvider(Options.Create(options), logger);
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
        using var provider = new AzureBlobStorageSemaphoreProvider(Options.Create(options), logger);
        var metadata = new Dictionary<string, string>();

        // Act
        bool isHoldingBefore = await provider.IsHoldingAsync("test-semaphore", "holder-1");
        (await provider.TryAcquireAsync("test-semaphore", "holder-1", 3, metadata, TimeSpan.FromMinutes(5))).ShouldBeTrue("should acquire");
        bool isHoldingAfter = await provider.IsHoldingAsync("test-semaphore", "holder-1");

        // Assert
        isHoldingBefore.ShouldBeFalse();
        isHoldingAfter.ShouldBeTrue();
    }

    private AzureBlobStorageSemaphoreOptions CreateOptions() => new()
    {
        ConnectionString = ConnectionString,
        ContainerName = containerName,
        AutoCreateContainer = true,
        MaxRetryAttempts = 5
    };

    private async Task<bool> IsAzuriteAvailableAsync()
    {
        try
        {
            var options = CreateOptions();
            using var provider = new AzureBlobStorageSemaphoreProvider(Options.Create(options), logger);
            return await provider.HealthCheckAsync();
        }
        catch
        {
            return false;
        }
    }
}


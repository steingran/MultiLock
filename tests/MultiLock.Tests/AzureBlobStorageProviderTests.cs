using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.AzureBlobStorage;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class AzureBlobStorageProviderTests
{
    private readonly ILogger<AzureBlobStorageLeaderElectionProvider> logger = new LoggerFactory().CreateLogger<AzureBlobStorageLeaderElectionProvider>();
    private readonly AzureBlobStorageLeaderElectionOptions options = new()
    {
        ConnectionString = "UseDevelopmentStorage=true",
        ContainerName = "test-leader-election",
        AutoCreateContainer = true
    };

    [Fact]
    public void Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var provider = new AzureBlobStorageLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void Options_WithEmptyConnectionString_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new AzureBlobStorageLeaderElectionOptions
        {
            ConnectionString = "",
            ContainerName = "test"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithEmptyContainerName_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new AzureBlobStorageLeaderElectionOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = ""
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithInvalidLeaseDuration_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new AzureBlobStorageLeaderElectionOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "test",
            LeaseDuration = TimeSpan.FromSeconds(5) // Too short
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithValidLeaseDuration_ShouldSucceed()
    {
        // Arrange
        var validOptions = new AzureBlobStorageLeaderElectionOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "test",
            LeaseDuration = TimeSpan.FromSeconds(30)
        };

        // Act & Assert
        validOptions.Validate(); // Should not throw
    }

    [Fact]
    public void Options_WithInfiniteLeaseDuration_ShouldSucceed()
    {
        // Arrange
        var validOptions = new AzureBlobStorageLeaderElectionOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "test",
            LeaseDuration = TimeSpan.FromSeconds(-1) // Infinite
        };

        // Act & Assert
        validOptions.Validate(); // Should not throw
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var provider = new AzureBlobStorageLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        provider.Dispose(); // Should not throw
    }
}

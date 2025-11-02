using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.ZooKeeper;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class ZooKeeperProviderTests
{
    private readonly ILogger<ZooKeeperLeaderElectionProvider> logger = new LoggerFactory().CreateLogger<ZooKeeperLeaderElectionProvider>();
    private readonly ZooKeeperLeaderElectionOptions options = new()
    {
        ConnectionString = "localhost:1111",
        RootPath = "/test-leader-election",
        SessionTimeout = TimeSpan.FromSeconds(30),
        ConnectionTimeout = TimeSpan.FromSeconds(10),
        MaxRetries = 3,
        RetryDelay = TimeSpan.FromSeconds(1),
        AutoCreateRootPath = true
    };

    [Fact]
    public void Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void Options_WithEmptyConnectionString_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = "",
            RootPath = "/test"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithEmptyRootPath_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = "localhost:2181",
            RootPath = ""
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithInvalidRootPath_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = "localhost:2181",
            RootPath = "invalid-path" // Should start with /
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithInvalidSessionTimeout_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = "localhost:2181",
            RootPath = "/test",
            SessionTimeout = TimeSpan.FromMinutes(70) // Too long
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithInvalidConnectionTimeout_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = "localhost:2181",
            RootPath = "/test",
            ConnectionTimeout = TimeSpan.FromMinutes(15) // Too long
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithInvalidMaxRetries_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = "localhost:2181",
            RootPath = "/test",
            MaxRetries = 15 // Too many
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithInvalidRetryDelay_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = "localhost:2181",
            RootPath = "/test",
            RetryDelay = TimeSpan.FromMinutes(10) // Too long
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithValidConfiguration_ShouldSucceed()
    {
        // Arrange
        var validOptions = new ZooKeeperLeaderElectionOptions
        {
            ConnectionString = "localhost:2181,localhost:2182",
            RootPath = "/leader-election",
            SessionTimeout = TimeSpan.FromSeconds(45),
            ConnectionTimeout = TimeSpan.FromSeconds(15),
            MaxRetries = 5,
            RetryDelay = TimeSpan.FromSeconds(2)
        };

        // Act & Assert
        validOptions.Validate(); // Should not throw
    }



    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        provider.Dispose(); // Should not throw
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public async Task MethodsAfterDispose_ShouldThrow()
    {
        // Arrange
        var provider = new ZooKeeperLeaderElectionProvider(
            Options.Create(options),
            logger);

        provider.Dispose();

        var metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.TryAcquireLeadershipAsync("test", "participant", metadata, TimeSpan.FromMinutes(1)));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.GetCurrentLeaderAsync("test"));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.UpdateHeartbeatAsync("test", "participant", metadata));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.ReleaseLeadershipAsync("test", "participant"));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.IsLeaderAsync("test", "participant"));
    }
}

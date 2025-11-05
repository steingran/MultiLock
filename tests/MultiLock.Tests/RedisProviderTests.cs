using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Redis;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class RedisProviderTests
{
    private readonly ILogger<RedisLeaderElectionProvider> logger = new LoggerFactory().CreateLogger<RedisLeaderElectionProvider>();
    private readonly RedisLeaderElectionOptions options = new()
    {
        ConnectionString = "localhost:6379",
        KeyPrefix = "test-leader-election",
        Database = 0
    };

    [Fact]
    public void Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var provider = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void Options_WithEmptyConnectionString_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new RedisLeaderElectionOptions
        {
            ConnectionString = "",
            KeyPrefix = "test"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithEmptyKeyPrefix_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = ""
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithNegativeDatabase_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = "test",
            Database = -1
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithValidConfiguration_ShouldSucceed()
    {
        // Arrange
        var validOptions = new RedisLeaderElectionOptions
        {
            ConnectionString = "localhost:6379",
            KeyPrefix = "test",
            Database = 5
        };

        // Act & Assert
        validOptions.Validate(); // Should not throw
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var provider = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var provider = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        provider.Dispose(); // First dispose
        provider.Dispose(); // Second dispose should not throw
    }

    [Fact]
    public async Task MethodsAfterDispose_ShouldThrow()
    {
        // Arrange
        var provider = new RedisLeaderElectionProvider(
            Options.Create(options),
            logger);

        provider.Dispose();

        var metadata = new Dictionary<string, string>();

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

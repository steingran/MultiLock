using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Consul;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class ConsulProviderTests
{
    private readonly ILogger<ConsulLeaderElectionProvider> logger = new LoggerFactory().CreateLogger<ConsulLeaderElectionProvider>();
    private readonly ConsulLeaderElectionOptions options = new()
    {
        Address = "http://localhost:8500",
        KeyPrefix = "test-leader-election",
        SessionTtl = TimeSpan.FromSeconds(60),
        SessionLockDelay = TimeSpan.FromSeconds(15)
    };

    [Fact]
    public void Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void Options_WithEmptyAddress_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ConsulLeaderElectionOptions
        {
            Address = "",
            KeyPrefix = "test"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithEmptyKeyPrefix_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = ""
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithInvalidSessionTtl_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = "test",
            SessionTtl = TimeSpan.FromSeconds(5) // Too short
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithInvalidSessionLockDelay_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = "test",
            SessionLockDelay = TimeSpan.FromMinutes(70) // Too long
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithValidConfiguration_ShouldSucceed()
    {
        // Arrange
        var validOptions = new ConsulLeaderElectionOptions
        {
            Address = "http://localhost:8500",
            KeyPrefix = "test",
            SessionTtl = TimeSpan.FromMinutes(5),
            SessionLockDelay = TimeSpan.FromSeconds(30),
            Datacenter = "dc1",
            Token = "test-token"
        };

        // Act & Assert
        validOptions.Validate(); // Should not throw
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        provider.Dispose(); // First dispose
        provider.Dispose(); // Second dispose should not throw
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        // Arrange
        var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        await provider.DisposeAsync(); // Should not throw
    }

    [Fact]
    public async Task DisposeAsync_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var provider = new ConsulLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        await provider.DisposeAsync(); // First dispose
        await provider.DisposeAsync(); // Second dispose should not throw
    }

    [Fact]
    public async Task MethodsAfterDispose_ShouldThrow()
    {
        // Arrange
        var provider = new ConsulLeaderElectionProvider(
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

using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.PostgreSQL;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class PostgreSqlProviderTests
{
    private readonly ILogger<PostgreSqlLeaderElectionProvider> logger = new LoggerFactory().CreateLogger<PostgreSqlLeaderElectionProvider>();
    private readonly PostgreSqlLeaderElectionOptions options = new()
    {
        ConnectionString = "Host=localhost;Database=dummy;Username=dummy;Password=dummy",
        TableName = "test_leader_election",
        SchemaName = "public",
        AutoCreateTable = true,
        CommandTimeoutSeconds = 30
    };

    [Fact]
    public void Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var provider = new PostgreSqlLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void Options_WithEmptyConnectionString_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = "",
            TableName = "test"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithEmptyTableName_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            TableName = ""
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithEmptySchemaName_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            TableName = "test",
            SchemaName = ""
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithInvalidCommandTimeout_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            TableName = "test",
            CommandTimeoutSeconds = -1
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithValidConfiguration_ShouldSucceed()
    {
        // Arrange
        var validOptions = new PostgreSqlLeaderElectionOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            TableName = "leader_election",
            SchemaName = "public",
            CommandTimeoutSeconds = 60
        };

        // Act & Assert
        validOptions.Validate(); // Should not throw
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var provider = new PostgreSqlLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var provider = new PostgreSqlLeaderElectionProvider(
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
        var provider = new PostgreSqlLeaderElectionProvider(
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

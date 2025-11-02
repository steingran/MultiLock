using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.SqlServer;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class SqlServerProviderTests
{
    private readonly ILogger<SqlServerLeaderElectionProvider> logger = new LoggerFactory().CreateLogger<SqlServerLeaderElectionProvider>();
    private readonly SqlServerLeaderElectionOptions options = new()
    {
        ConnectionString = "Server=localhost;Database=dummy;User Id=dummy;Password=dummy",
        TableName = "test_leader_election",
        SchemaName = "dbo",
        AutoCreateTable = true,
        CommandTimeoutSeconds = 30
    };

    [Fact]
    public void Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var provider = new SqlServerLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void Options_WithEmptyConnectionString_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new SqlServerLeaderElectionOptions
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
        var invalidOptions = new SqlServerLeaderElectionOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            TableName = ""
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => invalidOptions.Validate());
    }

    [Fact]
    public void Options_WithEmptySchemaName_ShouldThrow()
    {
        // Arrange
        var invalidOptions = new SqlServerLeaderElectionOptions
        {
            ConnectionString = "Server=localhost;Database=test",
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
        var invalidOptions = new SqlServerLeaderElectionOptions
        {
            ConnectionString = "Server=localhost;Database=test",
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
        var validOptions = new SqlServerLeaderElectionOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            TableName = "leader_election",
            SchemaName = "dbo",
            CommandTimeoutSeconds = 60
        };

        // Act & Assert
        validOptions.Validate(); // Should not throw
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var provider = new SqlServerLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var provider = new SqlServerLeaderElectionProvider(
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
        var provider = new SqlServerLeaderElectionProvider(
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


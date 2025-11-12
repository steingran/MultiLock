using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.FileSystem;
using Shouldly;
using Xunit;

namespace MultiLock.IntegrationTests;

public class FileSystemIntegrationTests : IAsyncLifetime
{
    private readonly ILogger<FileSystemLeaderElectionProvider> logger;
    private readonly string testDirectory;

    public FileSystemIntegrationTests()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        logger = loggerFactory.CreateLogger<FileSystemLeaderElectionProvider>();
        testDirectory = Path.Combine(Path.GetTempPath(), $"multilock-test-{Guid.NewGuid():N}");
    }

    public Task InitializeAsync()
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, true);

        Directory.CreateDirectory(testDirectory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(testDirectory))
        {
            try
            {
                Directory.Delete(testDirectory, true);
            }
            catch (IOException)
            {
                // Ignore cleanup errors
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore cleanup errors
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task HealthCheck_WithFileSystem_ShouldReturnTrue()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act
        bool isHealthy = await provider.HealthCheckAsync();

        // Assert
        isHealthy.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireLeadership_WithFileSystem_ShouldSucceed()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        bool acquired = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task TryAcquireLeadership_WhenAlreadyHeld_ShouldFail()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider1 = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        using var provider2 = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider1.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act
        bool acquired = await provider2.TryAcquireLeadershipAsync(
            "test-group",
            "participant-2",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        acquired.ShouldBeFalse();

        // Cleanup
        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task UpdateHeartbeat_WhenLeader_ShouldSucceed()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        var updatedMetadata = new Dictionary<string, string> { { "key", "updated-value" } };

        // Act
        bool updated = await provider.UpdateHeartbeatAsync(
            "test-group",
            "participant-1",
            updatedMetadata);

        // Assert
        updated.ShouldBeTrue();

        LeaderInfo? leader = await provider.GetCurrentLeaderAsync("test-group");
        leader.ShouldNotBeNull();
        leader.Metadata["key"].ShouldBe("updated-value");

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task GetCurrentLeader_WhenLeaderExists_ShouldReturnLeaderInfo()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "test", "data" } };

        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act
        LeaderInfo? leader = await provider.GetCurrentLeaderAsync("test-group");

        // Assert
        leader.ShouldNotBeNull();
        leader.LeaderId.ShouldBe("participant-1");
        leader.Metadata["test"].ShouldBe("data");

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task GetCurrentLeader_WhenNoLeader_ShouldReturnNull()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act
        LeaderInfo? leader = await provider.GetCurrentLeaderAsync("test-group");

        // Assert
        leader.ShouldBeNull();
    }

    [Fact]
    public async Task IsLeader_WhenLeader_ShouldReturnTrue()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act
        bool isLeader = await provider.IsLeaderAsync("test-group", "participant-1");

        // Assert
        isLeader.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task IsLeader_WhenNotLeader_ShouldReturnFalse()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act
        bool isLeader = await provider.IsLeaderAsync("test-group", "participant-1");

        // Assert
        isLeader.ShouldBeFalse();
    }

    [Fact]
    public async Task ReleaseLeadership_WhenLeader_ShouldReleaseLeadership()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");

        // Assert
        LeaderInfo? leader = await provider.GetCurrentLeaderAsync("test-group");
        leader.ShouldBeNull();
    }



    [Fact]
    public async Task TryAcquireLeadership_SameParticipantTwice_ShouldSucceed()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act - Acquire leadership twice with the same participant
        bool firstAcquire = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        bool secondAcquire = await provider.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Assert
        firstAcquire.ShouldBeTrue();
        secondAcquire.ShouldBeTrue();

        // Cleanup
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task UpdateHeartbeat_WhenNotLeader_ShouldFail()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider1 = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        using var provider2 = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        var metadata = new Dictionary<string, string> { { "key", "value" } };

        await provider1.TryAcquireLeadershipAsync(
            "test-group",
            "participant-1",
            metadata,
            TimeSpan.FromMinutes(5));

        // Act - Try to update heartbeat as a different participant
        bool updated = await provider2.UpdateHeartbeatAsync(
            "test-group",
            "participant-2",
            metadata);

        // Assert
        updated.ShouldBeFalse();

        // Cleanup
        await provider1.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task ReleaseLeadership_WhenNotLeader_ShouldNotThrow()
    {
        // Arrange
        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = testDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        // Act & Assert - Should not throw
        await provider.ReleaseLeadershipAsync("test-group", "participant-1");
    }

    [Fact]
    public async Task AutoCreateDirectory_WhenEnabled_ShouldCreateDirectory()
    {
        // Arrange
        string newDirectory = Path.Combine(Path.GetTempPath(), $"multilock-auto-create-{Guid.NewGuid():N}");

        var options = new FileSystemLeaderElectionOptions
        {
            DirectoryPath = newDirectory,
            AutoCreateDirectory = true
        };

        using var provider = new FileSystemLeaderElectionProvider(
            Options.Create(options),
            logger);

        try
        {
            // Act
            bool isHealthy = await provider.HealthCheckAsync();

            // Assert
            isHealthy.ShouldBeTrue();
            Directory.Exists(newDirectory).ShouldBeTrue();
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(newDirectory))
                Directory.Delete(newDirectory, true);
        }
    }
}

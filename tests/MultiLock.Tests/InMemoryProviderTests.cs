using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class InMemoryProviderTests : IDisposable
{
    private readonly InMemoryLeaderElectionProvider provider;
    private readonly ILoggerFactory loggerFactory;

    public InMemoryProviderTests()
    {
        loggerFactory = new LoggerFactory();
        ILogger<InMemoryLeaderElectionProvider> logger = loggerFactory.CreateLogger<InMemoryLeaderElectionProvider>();
        provider = new InMemoryLeaderElectionProvider(logger);
    }

    public void Dispose()
    {
        provider.Dispose();
        loggerFactory.Dispose();
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WhenNoExistingLeader_ShouldSucceed()
    {
        // Arrange
        const string electionGroup = "test-group";
        const string participantId = "participant-1";
        var metadata = new Dictionary<string, string> { { "key", "value" } };
        var lockTimeout = TimeSpan.FromMinutes(5);

        // Act
        bool result = await provider.TryAcquireLeadershipAsync(electionGroup, participantId, metadata, lockTimeout);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WhenLeaderExists_ShouldFail()
    {
        // Arrange
        const string electionGroup = "test-group";
        const string participant1 = "participant-1";
        const string participant2 = "participant-2";
        var metadata = new Dictionary<string, string> { { "key", "value" } };
        var lockTimeout = TimeSpan.FromMinutes(5);

        // First participant acquires leadership
        await provider.TryAcquireLeadershipAsync(electionGroup, participant1, metadata, lockTimeout);

        // Act
        bool result = await provider.TryAcquireLeadershipAsync(electionGroup, participant2, metadata, lockTimeout);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCurrentLeaderAsync_WhenLeaderExists_ShouldReturnLeaderInfo()
    {
        // Arrange
        const string electionGroup = "test-group";
        const string participantId = "participant-1";
        var metadata = new Dictionary<string, string> { { "key", "value" } };
        var lockTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireLeadershipAsync(electionGroup, participantId, metadata, lockTimeout);

        // Act
        LeaderInfo? leader = await provider.GetCurrentLeaderAsync(electionGroup);

        // Assert
        leader.ShouldNotBeNull();
        leader.LeaderId.ShouldBe(participantId);
        leader.Metadata.ContainsKey("key").ShouldBeTrue();
        leader.Metadata["key"].ShouldBe("value");
    }

    [Fact]
    public async Task GetCurrentLeaderAsync_WhenNoLeader_ShouldReturnNull()
    {
        // Arrange
        const string electionGroup = "test-group";

        // Act
        LeaderInfo? leader = await provider.GetCurrentLeaderAsync(electionGroup);

        // Assert
        leader.ShouldBeNull();
    }

    [Fact]
    public async Task IsLeaderAsync_WhenIsLeader_ShouldReturnTrue()
    {
        // Arrange
        const string electionGroup = "test-group";
        const string participantId = "participant-1";
        var metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        var lockTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireLeadershipAsync(electionGroup, participantId, metadata, lockTimeout);

        // Act
        bool isLeader = await provider.IsLeaderAsync(electionGroup, participantId);

        // Assert
        isLeader.ShouldBeTrue();
    }

    [Fact]
    public async Task IsLeaderAsync_WhenNotLeader_ShouldReturnFalse()
    {
        // Arrange
        const string electionGroup = "test-group";
        const string participantId = "participant-1";

        // Act
        bool isLeader = await provider.IsLeaderAsync(electionGroup, participantId);

        // Assert
        isLeader.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenIsLeader_ShouldSucceed()
    {
        // Arrange
        const string electionGroup = "test-group";
        const string participantId = "participant-1";
        var metadata = new Dictionary<string, string> { { "key", "value" } };
        var lockTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireLeadershipAsync(electionGroup, participantId, metadata, lockTimeout);

        var updatedMetadata = new Dictionary<string, string> { { "key", "updated-value" } };

        // Act
        bool result = await provider.UpdateHeartbeatAsync(electionGroup, participantId, updatedMetadata);

        // Assert
        result.ShouldBeTrue();

        LeaderInfo? leader = await provider.GetCurrentLeaderAsync(electionGroup);
        leader.ShouldNotBeNull();
        leader.Metadata.ContainsKey("key").ShouldBeTrue();
        leader.Metadata["key"].ShouldBe("updated-value");
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenNotLeader_ShouldFail()
    {
        // Arrange
        const string electionGroup = "test-group";
        const string participantId = "participant-1";
        var metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        // Act
        bool result = await provider.UpdateHeartbeatAsync(electionGroup, participantId, metadata);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ReleaseLeadershipAsync_WhenIsLeader_ShouldReleaseLeadership()
    {
        // Arrange
        const string electionGroup = "test-group";
        const string participantId = "participant-1";
        var metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        var lockTimeout = TimeSpan.FromMinutes(5);

        await provider.TryAcquireLeadershipAsync(electionGroup, participantId, metadata, lockTimeout);

        // Act
        await provider.ReleaseLeadershipAsync(electionGroup, participantId);

        // Assert
        LeaderInfo? leader = await provider.GetCurrentLeaderAsync(electionGroup);
        leader.ShouldBeNull();
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
    public async Task TryAcquireLeadershipAsync_WhenLeaderExpired_ShouldSucceed()
    {
        // Arrange
        const string electionGroup = "test-group";
        const string participant1 = "participant-1";
        const string participant2 = "participant-2";
        var metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        var shortTimeout = TimeSpan.FromMilliseconds(100);

        // First participant acquires leadership
        await provider.TryAcquireLeadershipAsync(electionGroup, participant1, metadata, TimeSpan.FromMinutes(5));

        // Wait for the lock to "expire" (simulate by using a very short timeout)
        await Task.Delay(200);

        // Act
        bool result = await provider.TryAcquireLeadershipAsync(electionGroup, participant2, metadata, shortTimeout);

        // Assert
        result.ShouldBeTrue();
    }
}

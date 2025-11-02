using System.Collections.ObjectModel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MultiLock.InMemory;
using Xunit;

namespace MultiLock.Tests;

public class InMemoryProviderTests
{
    private readonly InMemoryLeaderElectionProvider provider;

    public InMemoryProviderTests()
    {
        ILogger<InMemoryLeaderElectionProvider> logger = new LoggerFactory().CreateLogger<InMemoryLeaderElectionProvider>();
        provider = new InMemoryLeaderElectionProvider(logger);
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
        result.Should().BeTrue();
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
        result.Should().BeFalse();
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
        leader.Should().NotBeNull();
        leader!.LeaderId.Should().Be(participantId);
        leader.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("value");
    }

    [Fact]
    public async Task GetCurrentLeaderAsync_WhenNoLeader_ShouldReturnNull()
    {
        // Arrange
        const string electionGroup = "test-group";

        // Act
        LeaderInfo? leader = await provider.GetCurrentLeaderAsync(electionGroup);

        // Assert
        leader.Should().BeNull();
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
        isLeader.Should().BeTrue();
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
        isLeader.Should().BeFalse();
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
        result.Should().BeTrue();

        LeaderInfo? leader = await provider.GetCurrentLeaderAsync(electionGroup);
        leader!.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("updated-value");
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
        result.Should().BeFalse();
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
        leader.Should().BeNull();
    }

    [Fact]
    public async Task HealthCheckAsync_ShouldReturnTrue()
    {
        // Act
        bool isHealthy = await provider.HealthCheckAsync();

        // Assert
        isHealthy.Should().BeTrue();
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
        result.Should().BeTrue();
    }
}

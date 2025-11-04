using Microsoft.Extensions.Logging;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Tests to verify that all provider implementations properly validate their input parameters.
/// These tests use InMemoryLeaderElectionProvider as a representative provider since all providers
/// should have identical validation behavior.
/// </summary>
public sealed class ProviderInputValidationTests : IDisposable
{
    private readonly InMemoryLeaderElectionProvider provider;
    private readonly ILoggerFactory loggerFactory;
    private readonly Dictionary<string, string> validMetadata = new() { { "key", "value" } };
    private const string validElectionGroup = "test-group";
    private const string validParticipantId = "participant-1";
    private readonly TimeSpan validLockTimeout = TimeSpan.FromMinutes(5);

    public ProviderInputValidationTests()
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

    // TryAcquireLeadershipAsync validation tests

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithNullElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync(null!, validParticipantId, validMetadata, validLockTimeout));
        exception.ParamName.ShouldBe("electionGroup");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithEmptyElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync("", validParticipantId, validMetadata, validLockTimeout));
        exception.ParamName.ShouldBe("electionGroup");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithInvalidElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync("invalid group", validParticipantId, validMetadata, validLockTimeout));
        exception.ParamName.ShouldBe("electionGroup");
        exception.Message.ShouldContain("invalid characters");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithTooLongElectionGroup_ShouldThrowArgumentException()
    {
        // Arrange
        string longGroup = new('a', 256);

        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync(longGroup, validParticipantId, validMetadata, validLockTimeout));
        exception.ParamName.ShouldBe("electionGroup");
        exception.Message.ShouldContain("cannot exceed 255 characters");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithNullParticipantId_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync(validElectionGroup, null!, validMetadata, validLockTimeout));
        exception.ParamName.ShouldBe("participantId");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithEmptyParticipantId_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync(validElectionGroup, "", validMetadata, validLockTimeout));
        exception.ParamName.ShouldBe("participantId");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithInvalidParticipantId_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync(validElectionGroup, "invalid@participant", validMetadata, validLockTimeout));
        exception.ParamName.ShouldBe("participantId");
        exception.Message.ShouldContain("invalid characters");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithNullMetadata_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        ArgumentNullException exception = await Should.ThrowAsync<ArgumentNullException>(async () =>
            await provider.TryAcquireLeadershipAsync(validElectionGroup, validParticipantId, null!, validLockTimeout));
        exception.ParamName.ShouldBe("metadata");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithTooManyMetadataEntries_ShouldThrowArgumentException()
    {
        // Arrange
        var tooMuchMetadata = new Dictionary<string, string>();
        for (int i = 0; i < 101; i++)
        {
            tooMuchMetadata[$"key{i}"] = $"value{i}";
        }

        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync(validElectionGroup, validParticipantId, tooMuchMetadata, validLockTimeout));
        exception.ParamName.ShouldBe("metadata");
        exception.Message.ShouldContain("cannot contain more than 100 entries");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithZeroLockTimeout_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync(validElectionGroup, validParticipantId, validMetadata, TimeSpan.Zero));
        exception.ParamName.ShouldBe("lockTimeout");
        exception.Message.ShouldContain("must be positive");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithNegativeLockTimeout_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync(validElectionGroup, validParticipantId, validMetadata, TimeSpan.FromSeconds(-1)));
        exception.ParamName.ShouldBe("lockTimeout");
        exception.Message.ShouldContain("must be positive");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithTooLongLockTimeout_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.TryAcquireLeadershipAsync(validElectionGroup, validParticipantId, validMetadata, TimeSpan.FromDays(2)));
        exception.ParamName.ShouldBe("lockTimeout");
        exception.Message.ShouldContain("cannot exceed 1 day");
    }

    [Fact]
    public async Task TryAcquireLeadershipAsync_WithValidParameters_ShouldSucceed()
    {
        // Act
        bool result = await provider.TryAcquireLeadershipAsync(validElectionGroup, validParticipantId, validMetadata, validLockTimeout);

        // Assert
        result.ShouldBeTrue();
    }

    // ReleaseLeadershipAsync validation tests

    [Fact]
    public async Task ReleaseLeadershipAsync_WithNullElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.ReleaseLeadershipAsync(null!, validParticipantId));
        exception.ParamName.ShouldBe("electionGroup");
    }

    [Fact]
    public async Task ReleaseLeadershipAsync_WithEmptyElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.ReleaseLeadershipAsync("", validParticipantId));
        exception.ParamName.ShouldBe("electionGroup");
    }

    [Fact]
    public async Task ReleaseLeadershipAsync_WithNullParticipantId_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.ReleaseLeadershipAsync(validElectionGroup, null!));
        exception.ParamName.ShouldBe("participantId");
    }

    [Fact]
    public async Task ReleaseLeadershipAsync_WithEmptyParticipantId_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.ReleaseLeadershipAsync(validElectionGroup, ""));
        exception.ParamName.ShouldBe("participantId");
    }

    // UpdateHeartbeatAsync validation tests

    [Fact]
    public async Task UpdateHeartbeatAsync_WithNullElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.UpdateHeartbeatAsync(null!, validParticipantId, validMetadata));
        exception.ParamName.ShouldBe("electionGroup");
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WithNullParticipantId_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.UpdateHeartbeatAsync(validElectionGroup, null!, validMetadata));
        exception.ParamName.ShouldBe("participantId");
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WithNullMetadata_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        ArgumentNullException exception = await Should.ThrowAsync<ArgumentNullException>(async () =>
            await provider.UpdateHeartbeatAsync(validElectionGroup, validParticipantId, null!));
        exception.ParamName.ShouldBe("metadata");
    }

    // GetCurrentLeaderAsync validation tests

    [Fact]
    public async Task GetCurrentLeaderAsync_WithNullElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.GetCurrentLeaderAsync(null!));
        exception.ParamName.ShouldBe("electionGroup");
    }

    [Fact]
    public async Task GetCurrentLeaderAsync_WithEmptyElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.GetCurrentLeaderAsync(""));
        exception.ParamName.ShouldBe("electionGroup");
    }

    [Fact]
    public async Task GetCurrentLeaderAsync_WithInvalidElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.GetCurrentLeaderAsync("invalid group!"));
        exception.ParamName.ShouldBe("electionGroup");
        exception.Message.ShouldContain("invalid characters");
    }

    // IsLeaderAsync validation tests

    [Fact]
    public async Task IsLeaderAsync_WithNullElectionGroup_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.IsLeaderAsync(null!, validParticipantId));
        exception.ParamName.ShouldBe("electionGroup");
    }

    [Fact]
    public async Task IsLeaderAsync_WithNullParticipantId_ShouldThrowArgumentException()
    {
        // Act & Assert
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await provider.IsLeaderAsync(validElectionGroup, null!));
        exception.ParamName.ShouldBe("participantId");
    }

    [Fact]
    public async Task IsLeaderAsync_WithValidParameters_ShouldSucceed()
    {
        // Act
        bool result = await provider.IsLeaderAsync(validElectionGroup, validParticipantId);

        // Assert - Should not throw, result can be true or false
        result.ShouldBeOfType<bool>();
    }
}


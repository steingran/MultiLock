using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MultiLock.InMemory;

/// <summary>
/// In-memory implementation of the leader election provider.
/// This provider is intended for testing and development scenarios only.
/// It does not provide true distributed coordination across multiple processes or machines.
/// </summary>
public sealed class InMemoryLeaderElectionProvider : ILeaderElectionProvider
{
    private readonly ILogger<InMemoryLeaderElectionProvider> logger;
    private readonly ConcurrentDictionary<string, LeaderRecord> leaders = new();
    private readonly object @lock = new();
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryLeaderElectionProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public InMemoryLeaderElectionProvider(ILogger<InMemoryLeaderElectionProvider> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<bool> TryAcquireLeadershipAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        ParameterValidation.ValidateMetadata(metadata);
        ParameterValidation.ValidateLockTimeout(lockTimeout);

        lock (@lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset expiryTime = now.Subtract(lockTimeout);

            if (leaders.TryGetValue(electionGroup, out LeaderRecord? existingLeader))
            {
                // Check if existing leader has expired or is the same participant
                if (existingLeader.LastHeartbeat >= expiryTime && existingLeader.LeaderId != participantId)
                {
                    logger.LogDebug("Leadership acquisition failed for participant {ParticipantId} in group {ElectionGroup}. Current leader: {CurrentLeader}",
                        participantId, electionGroup, existingLeader.LeaderId);
                    return Task.FromResult(false);
                }
            }

            // Acquire or renew leadership
            var newLeader = new LeaderRecord(
                participantId,
                now,
                now,
                new Dictionary<string, string>(metadata));

            leaders.AddOrUpdate(electionGroup, newLeader, (_, _) => newLeader);

            logger.LogInformation("Successfully acquired leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task ReleaseLeadershipAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);

        lock (@lock)
        {
            if (!leaders.TryGetValue(electionGroup, out LeaderRecord? existingLeader) ||
                existingLeader.LeaderId != participantId) return Task.CompletedTask;

            leaders.TryRemove(electionGroup, out _);
            logger.LogInformation("Released leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> UpdateHeartbeatAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        ParameterValidation.ValidateMetadata(metadata);

        lock (@lock)
        {
            if (leaders.TryGetValue(electionGroup, out LeaderRecord? existingLeader) &&
                existingLeader.LeaderId == participantId)
            {
                var updatedLeader = new LeaderRecord(
                    participantId,
                    existingLeader.LeadershipAcquiredAt,
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, string>(metadata));

                leaders.AddOrUpdate(electionGroup, updatedLeader, (_, _) => updatedLeader);

                logger.LogDebug("Updated heartbeat for leader {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);

                return Task.FromResult(true);
            }

            logger.LogWarning("Heartbeat update failed for participant {ParticipantId} in group {ElectionGroup} - not the current leader",
                participantId, electionGroup);

            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<LeaderInfo?> GetCurrentLeaderAsync(
        string electionGroup,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);

        lock (@lock)
        {
            if (!leaders.TryGetValue(electionGroup, out LeaderRecord? leader))
                return Task.FromResult<LeaderInfo?>(null);

            var leaderInfo = new LeaderInfo(
                leader.LeaderId,
                leader.LeadershipAcquiredAt,
                leader.LastHeartbeat,
                leader.Metadata);

            return Task.FromResult<LeaderInfo?>(leaderInfo);
        }
    }

    /// <inheritdoc />
    public Task<bool> IsLeaderAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);

        lock (@lock)
        {
            return leaders.TryGetValue(electionGroup, out LeaderRecord? leader)
                ? Task.FromResult(leader.LeaderId == participantId)
                : Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!isDisposed);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        leaders.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (!isDisposed) return;
        throw new ObjectDisposedException(nameof(InMemoryLeaderElectionProvider));
    }
}

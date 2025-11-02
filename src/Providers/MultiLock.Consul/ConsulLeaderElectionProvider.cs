using System.Text;
using System.Text.Json;
using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;

namespace MultiLock.Consul;

/// <summary>
/// Consul implementation of the leader election provider.
/// Uses Consul sessions and key-value store for distributed coordination.
/// </summary>
public sealed class ConsulLeaderElectionProvider : ILeaderElectionProvider
{
    private readonly ConsulLeaderElectionOptions options;
    private readonly ILogger<ConsulLeaderElectionProvider> logger;
    private readonly ConsulClient consulClient;
    private readonly Dictionary<string, string> activeSessions = new();
    private readonly object sessionLock = new();
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsulLeaderElectionProvider"/> class.
    /// </summary>
    /// <param name="options">The Consul options.</param>
    /// <param name="logger">The logger.</param>
    public ConsulLeaderElectionProvider(
        IOptions<ConsulLeaderElectionOptions> options,
        ILogger<ConsulLeaderElectionProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();

        var consulConfig = new ConsulClientConfiguration
        {
            Address = new Uri(this.options.Address)
        };

        if (!string.IsNullOrEmpty(this.options.Datacenter))
            consulConfig.Datacenter = this.options.Datacenter;

        if (!string.IsNullOrEmpty(this.options.Token))
            consulConfig.Token = this.options.Token;

        consulClient = new ConsulClient(consulConfig);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireLeadershipAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // Create a session for this election attempt
            string sessionId = await CreateSessionAsync(participantId, cancellationToken);
            string key = GetLeaderKey(electionGroup);

            var leaderData = new LeaderData
            {
                LeaderId = participantId,
                LeadershipAcquiredAt = DateTimeOffset.UtcNow,
                LastHeartbeat = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(metadata),
                SessionId = sessionId
            };

            string json = JsonSerializer.Serialize(leaderData);
            byte[] value = Encoding.UTF8.GetBytes(json);

            // Try to acquire the lock using the session
            var kvPair = new KVPair(key)
            {
                Value = value,
                Session = sessionId
            };

            WriteResult<bool>? acquireResult = await consulClient.KV.Acquire(kvPair, cancellationToken);

            if (acquireResult.Response)
            {
                lock (sessionLock)
                {
                    activeSessions[electionGroup] = sessionId;
                }

                logger.LogInformation("Successfully acquired leadership for participant {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);

                return true;
            }
            else
            {
                // Failed to acquire, destroy the session
                await consulClient.Session.Destroy(sessionId, cancellationToken);

                logger.LogDebug("Failed to acquire leadership for participant {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);

                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error acquiring leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to acquire leadership", ex);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseLeadershipAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            string? sessionId;
            lock (sessionLock)
            {
                if (!activeSessions.TryGetValue(electionGroup, out sessionId))
                {
                    logger.LogWarning("No active session found for participant {ParticipantId} in group {ElectionGroup}",
                        participantId, electionGroup);
                    return;
                }
                activeSessions.Remove(electionGroup);
            }

            // Destroy the session, which will automatically release any locks
            await consulClient.Session.Destroy(sessionId, cancellationToken);

            logger.LogInformation("Released leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error releasing leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to release leadership", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateHeartbeatAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            string? sessionId;
            lock (sessionLock)
            {
                if (!activeSessions.TryGetValue(electionGroup, out sessionId))
                    return false;
            }

            // Renew the session
            WriteResult<SessionEntry>? renewResult = await consulClient.Session.Renew(sessionId, cancellationToken);
            if (renewResult.Response == null)
            {
                // Session expired or doesn't exist
                lock (sessionLock)
                {
                    activeSessions.Remove(electionGroup);
                }
                return false;
            }

            // Update the key value with new heartbeat
            string key = GetLeaderKey(electionGroup);
            QueryResult<KVPair>? getResult = await consulClient.KV.Get(key, cancellationToken);

            if (getResult.Response?.Session != sessionId)
            {
                // We no longer hold the lock
                lock (sessionLock)
                {
                    activeSessions.Remove(electionGroup);
                }
                return false;
            }

            LeaderData? existingData = JsonSerializer.Deserialize<LeaderData>(Encoding.UTF8.GetString(getResult.Response.Value));
            if (existingData?.LeaderId != participantId)
                return false;

            existingData.LastHeartbeat = DateTimeOffset.UtcNow;
            existingData.Metadata = new Dictionary<string, string>(metadata);

            string json = JsonSerializer.Serialize(existingData);
            byte[] value = Encoding.UTF8.GetBytes(json);

            var kvPair = new KVPair(key)
            {
                Value = value,
                Session = sessionId,
                ModifyIndex = getResult.Response.ModifyIndex
            };

            WriteResult<bool>? putResult = await consulClient.KV.CAS(kvPair, cancellationToken);

            logger.LogDebug("Updated heartbeat for leader {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);

            return putResult.Response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating heartbeat for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to update heartbeat", ex);
        }
    }

    /// <inheritdoc />
    public async Task<LeaderInfo?> GetCurrentLeaderAsync(
        string electionGroup,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            string key = GetLeaderKey(electionGroup);
            QueryResult<KVPair>? getResult = await consulClient.KV.Get(key, cancellationToken);

            if (getResult.Response?.Value == null)
                return null;

            LeaderData? leaderData = JsonSerializer.Deserialize<LeaderData>(Encoding.UTF8.GetString(getResult.Response.Value));

            if (leaderData == null)
                return null;

            return new LeaderInfo(
                leaderData.LeaderId,
                leaderData.LeadershipAcquiredAt,
                leaderData.LastHeartbeat,
                leaderData.Metadata);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting current leader for group {ElectionGroup}", electionGroup);
            throw new LeaderElectionProviderException("Failed to get current leader", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsLeaderAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        LeaderInfo? leader = await GetCurrentLeaderAsync(electionGroup, cancellationToken);
        return leader?.LeaderId == participantId;
    }

    /// <inheritdoc />
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
        {
            return false;
        }

        try
        {
            QueryResult<Dictionary<string, Dictionary<string, dynamic>>>? result = await consulClient.Agent.Self(cancellationToken);
            return result.StatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for Consul provider");
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        // Clean up active sessions
        var sessionsToDestroy = new List<string>();
        lock (sessionLock)
        {
            sessionsToDestroy.AddRange(activeSessions.Values);
            activeSessions.Clear();
        }

        foreach (string sessionId in sessionsToDestroy)
        {
            try
            {
                consulClient.Session.Destroy(sessionId).Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error destroying session {SessionId} during disposal", sessionId);
            }
        }

        consulClient.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (!isDisposed) return;
        throw new ObjectDisposedException(nameof(ConsulLeaderElectionProvider));
    }

    private async Task<string> CreateSessionAsync(string participantId, CancellationToken cancellationToken)
    {
        var sessionEntry = new SessionEntry
        {
            Name = $"leader-election-{participantId}",
            TTL = options.SessionTtl,
            LockDelay = options.SessionLockDelay,
            Behavior = SessionBehavior.Release
        };

        WriteResult<string>? createResult = await consulClient.Session.Create(sessionEntry, cancellationToken);
        return createResult.Response;
    }

    private string GetLeaderKey(string electionGroup)
    {
        return $"{options.KeyPrefix}/{electionGroup}";
    }

    /// <summary>
    /// Internal class to represent leader data stored in Consul.
    /// </summary>
    private sealed class LeaderData
    {
        public string LeaderId { get; init; } = string.Empty;
        public DateTimeOffset LeadershipAcquiredAt { get; init; }
        public DateTimeOffset LastHeartbeat { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public string SessionId { get; init; } = string.Empty;
    }
}

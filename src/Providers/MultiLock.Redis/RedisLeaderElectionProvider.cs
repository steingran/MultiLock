using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;
using StackExchange.Redis;

namespace MultiLock.Redis;

/// <summary>
/// Redis implementation of the leader election provider.
/// Uses Redis SET with EX and NX options for atomic leader election.
/// </summary>
public sealed class RedisLeaderElectionProvider : ILeaderElectionProvider
{
    private readonly RedisLeaderElectionOptions options;
    private readonly ILogger<RedisLeaderElectionProvider> logger;
    private readonly Lazy<ConnectionMultiplexer> connectionMultiplexer;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisLeaderElectionProvider"/> class.
    /// </summary>
    /// <param name="options">The Redis options.</param>
    /// <param name="logger">The logger.</param>
    public RedisLeaderElectionProvider(
        IOptions<RedisLeaderElectionOptions> options,
        ILogger<RedisLeaderElectionProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();

        connectionMultiplexer = new Lazy<ConnectionMultiplexer>(() =>
            ConnectionMultiplexer.Connect(this.options.ConnectionString));
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
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        ParameterValidation.ValidateMetadata(metadata);
        ParameterValidation.ValidateLockTimeout(lockTimeout);

        try
        {
            IDatabase database = GetDatabase();
            string key = GetLeaderKey(electionGroup);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            var leaderData = new LeaderData
            {
                LeaderId = participantId,
                LeadershipAcquiredAt = now,
                LastHeartbeat = now,
                Metadata = new Dictionary<string, string>(metadata)
            };

            string json = JsonSerializer.Serialize(leaderData);

            // Use SET with NX (only if not exists) and EX (expiration) for atomic operation
            bool acquired = await database.StringSetAsync(key, json, lockTimeout, When.NotExists);

            if (acquired)
            {
                logger.LogInformation("Successfully acquired leadership for participant {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);
            }
            else
            {
                logger.LogDebug("Failed to acquire leadership for participant {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);
            }

            return acquired;
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
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);

        try
        {
            IDatabase database = GetDatabase();
            string key = GetLeaderKey(electionGroup);

            // Use Lua script to atomically check and delete only if we are the current leader
            const string script = @"
                local current = redis.call('GET', KEYS[1])
                if current then
                    local data = cjson.decode(current)
                    if data.LeaderId == ARGV[1] then
                        return redis.call('DEL', KEYS[1])
                    end
                end
                return 0";

            RedisResult result = await database.ScriptEvaluateAsync(script, [key], [participantId]);

            if ((int)result == 1)
            {
                logger.LogInformation("Released leadership for participant {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);
            }
            else
            {
                logger.LogWarning("Cannot release leadership for participant {ParticipantId} in group {ElectionGroup} - not the current leader",
                    participantId, electionGroup);
            }
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
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        ParameterValidation.ValidateMetadata(metadata);

        try
        {
            IDatabase database = GetDatabase();
            string key = GetLeaderKey(electionGroup);

            // Use Lua script to atomically check, update, and extend TTL only if we are the current leader
            const string script = @"
                local current = redis.call('GET', KEYS[1])
                if current then
                    local data = cjson.decode(current)
                    if data.LeaderId == ARGV[1] then
                        data.LastHeartbeat = ARGV[2]
                        data.Metadata = cjson.decode(ARGV[3])
                        local updated = cjson.encode(data)
                        redis.call('SET', KEYS[1], updated, 'EX', ARGV[4])
                        return 1
                    end
                end
                return 0";

            string now = DateTimeOffset.UtcNow.ToString("O");
            string metadataJson = JsonSerializer.Serialize(metadata);
            int ttlSeconds = (int)TimeSpan.FromMinutes(2).TotalSeconds; // Default TTL extension

            RedisResult result = await database.ScriptEvaluateAsync(script,
                [key],
                [participantId, now, metadataJson, ttlSeconds]);

            bool success = (int)result == 1;

            if (success)
            {
                logger.LogDebug("Updated heartbeat for leader {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);
            }
            else
            {
                logger.LogWarning("Heartbeat update failed for participant {ParticipantId} in group {ElectionGroup} - not the current leader",
                    participantId, electionGroup);
            }

            return success;
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
        ParameterValidation.ValidateElectionGroup(electionGroup);

        try
        {
            IDatabase database = GetDatabase();
            string key = GetLeaderKey(electionGroup);

            RedisValue value = await database.StringGetAsync(key);

            if (!value.HasValue)
            {
                return null;
            }

            LeaderData? leaderData = JsonSerializer.Deserialize<LeaderData>(value!);

            if (leaderData == null)
            {
                return null;
            }

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
        ThrowIfDisposed();
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
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
            IDatabase database = GetDatabase();
            await database.PingAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for Redis provider");
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;

            if (connectionMultiplexer.IsValueCreated)
            {
                connectionMultiplexer.Value.Dispose();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (isDisposed)
        {
            throw new ObjectDisposedException(nameof(RedisLeaderElectionProvider));
        }
    }

    private IDatabase GetDatabase()
    {
        return connectionMultiplexer.Value.GetDatabase(options.Database);
    }

    private string GetLeaderKey(string electionGroup)
    {
        return $"{options.KeyPrefix}:{electionGroup}";
    }

    /// <summary>
    /// Internal class to represent leader data stored in Redis.
    /// </summary>
    private sealed class LeaderData
    {
        public string LeaderId { get; set; } = string.Empty;
        public DateTimeOffset LeadershipAcquiredAt { get; set; }
        public DateTimeOffset LastHeartbeat { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;
using StackExchange.Redis;

namespace MultiLock.Redis;

/// <summary>
/// Redis implementation of the semaphore provider.
/// Uses Redis sorted sets (ZSET) for semaphore implementation with Lua scripts for atomic operations.
/// </summary>
public sealed class RedisSemaphoreProvider : ISemaphoreProvider
{
    private readonly RedisSemaphoreOptions options;
    private readonly ILogger<RedisSemaphoreProvider> logger;
    private readonly Lazy<ConnectionMultiplexer> connectionMultiplexer;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisSemaphoreProvider"/> class.
    /// </summary>
    /// <param name="options">The Redis options.</param>
    /// <param name="logger">The logger.</param>
    public RedisSemaphoreProvider(
        IOptions<RedisSemaphoreOptions> options,
        ILogger<RedisSemaphoreProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();

        connectionMultiplexer = new Lazy<ConnectionMultiplexer>(() =>
            ConnectionMultiplexer.Connect(this.options.ConnectionString));
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(
        string semaphoreName,
        string holderId,
        int maxCount,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan slotTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);
        ParameterValidation.ValidateMaxCount(maxCount);
        ParameterValidation.ValidateMetadata(metadata);
        ParameterValidation.ValidateSlotTimeout(slotTimeout);

        try
        {
            IDatabase database = GetDatabase();
            string holdersKey = GetHoldersKey(semaphoreName);
            string dataKey = GetDataKey(semaphoreName);
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            double expiryTime = now - slotTimeout.TotalMilliseconds;

            // Lua script for atomic acquire operation:
            // 1. Remove expired holders
            // 2. Check if holder already exists (renew)
            // 3. Check if there's room for new holder
            // 4. Add holder if room available
            const string script = @"
                -- Collect and remove expired holders, then also purge their hash data
                local expired = redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
                redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
                if #expired > 0 then
                    redis.call('HDEL', KEYS[2], unpack(expired))
                end

                -- Check if holder already exists
                local existingScore = redis.call('ZSCORE', KEYS[1], ARGV[2])
                if existingScore then
                    -- Renew heartbeat score; preserve original AcquiredAt from stored data
                    redis.call('ZADD', KEYS[1], ARGV[3], ARGV[2])
                    local existingJson = redis.call('HGET', KEYS[2], ARGV[2])
                    if existingJson then
                        local existing = cjson.decode(existingJson)
                        local newData  = cjson.decode(ARGV[4])
                        newData['AcquiredAt'] = existing['AcquiredAt']
                        redis.call('HSET', KEYS[2], ARGV[2], cjson.encode(newData))
                    else
                        redis.call('HSET', KEYS[2], ARGV[2], ARGV[4])
                    end
                    return 1
                end

                -- Check current count
                local currentCount = redis.call('ZCARD', KEYS[1])
                if currentCount >= tonumber(ARGV[5]) then
                    return 0
                end

                -- Add new holder
                redis.call('ZADD', KEYS[1], ARGV[3], ARGV[2])
                redis.call('HSET', KEYS[2], ARGV[2], ARGV[4])
                return 1";

            var holderData = new HolderData
            {
                HolderId = holderId,
                AcquiredAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(metadata)
            };
            string holderDataJson = JsonSerializer.Serialize(holderData);

            RedisResult result = await database.ScriptEvaluateAsync(script,
                [holdersKey, dataKey],
                [expiryTime, holderId, now, holderDataJson, maxCount]);

            bool acquired = (int)result == 1;

            if (acquired)
                logger.LogInformation("Acquired slot for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
            else
                logger.LogDebug("Failed to acquire slot for holder {HolderId} in semaphore {SemaphoreName} - full",
                    holderId, semaphoreName);

            return acquired;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error acquiring slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to acquire semaphore slot", ex);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);

        try
        {
            IDatabase database = GetDatabase();
            string holdersKey = GetHoldersKey(semaphoreName);
            string dataKey = GetDataKey(semaphoreName);

            // Lua script to atomically remove holder from both keys
            const string script = @"
                redis.call('ZREM', KEYS[1], ARGV[1])
                redis.call('HDEL', KEYS[2], ARGV[1])
                return 1";

            await database.ScriptEvaluateAsync(script, [holdersKey, dataKey], [holderId]);

            logger.LogInformation("Released slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error releasing slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to release semaphore slot", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateHeartbeatAsync(
        string semaphoreName,
        string holderId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);
        ParameterValidation.ValidateMetadata(metadata);

        try
        {
            IDatabase database = GetDatabase();
            string holdersKey = GetHoldersKey(semaphoreName);
            string dataKey = GetDataKey(semaphoreName);
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Lua script to update heartbeat only if holder exists
            const string script = @"
                local existingScore = redis.call('ZSCORE', KEYS[1], ARGV[1])
                if existingScore then
                    redis.call('ZADD', KEYS[1], ARGV[2], ARGV[1])
                    local existingData = redis.call('HGET', KEYS[2], ARGV[1])
                    if existingData then
                        local data = cjson.decode(existingData)
                        data.Metadata = cjson.decode(ARGV[3])
                        redis.call('HSET', KEYS[2], ARGV[1], cjson.encode(data))
                    end
                    return 1
                end
                return 0";

            string metadataJson = JsonSerializer.Serialize(metadata);

            RedisResult result = await database.ScriptEvaluateAsync(script,
                [holdersKey, dataKey],
                [holderId, now, metadataJson]);

            bool success = (int)result == 1;

            if (success)
                logger.LogDebug("Updated heartbeat for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
            else
                logger.LogWarning("Heartbeat update failed for holder {HolderId} in semaphore {SemaphoreName} - not a holder",
                    holderId, semaphoreName);

            return success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating heartbeat for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to update heartbeat", ex);
        }
    }

    /// <inheritdoc />
    public async Task<int> GetCurrentCountAsync(
        string semaphoreName,
        TimeSpan slotTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);

        try
        {
            IDatabase database = GetDatabase();
            string holdersKey = GetHoldersKey(semaphoreName);

            // Only count members whose score (last heartbeat as Unix milliseconds) is within the slot timeout.
            double minScore = DateTimeOffset.UtcNow.Subtract(slotTimeout).ToUnixTimeMilliseconds();
            long count = await database.SortedSetLengthAsync(holdersKey, minScore, double.PositiveInfinity);
            return (int)count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting current count for semaphore {SemaphoreName}", semaphoreName);
            throw new SemaphoreProviderException("Failed to get current count", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemaphoreHolder>> GetHoldersAsync(
        string semaphoreName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);

        try
        {
            IDatabase database = GetDatabase();
            string holdersKey = GetHoldersKey(semaphoreName);
            string dataKey = GetDataKey(semaphoreName);

            // Get all holders with their scores (heartbeat timestamps)
            SortedSetEntry[] entries = await database.SortedSetRangeByRankWithScoresAsync(holdersKey);

            if (entries.Length == 0)
                return [];

            // Batch-fetch all holder data in a single round trip instead of N individual calls.
            RedisValue[] holderIds = Array.ConvertAll(entries, e => e.Element);
            RedisValue[] dataValues = await database.HashGetAsync(dataKey, holderIds);

            var holders = new List<SemaphoreHolder>(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                string holderId = entries[i].Element!;
                DateTimeOffset lastHeartbeat = DateTimeOffset.FromUnixTimeMilliseconds((long)entries[i].Score);
                DateTimeOffset acquiredAt = lastHeartbeat;
                IReadOnlyDictionary<string, string> metadata = new Dictionary<string, string>();

                if (dataValues[i].HasValue)
                {
                    HolderData? holderData = JsonSerializer.Deserialize<HolderData>((string)dataValues[i]!);
                    if (holderData != null)
                    {
                        acquiredAt = holderData.AcquiredAt;
                        metadata = holderData.Metadata;
                    }
                }

                holders.Add(new SemaphoreHolder(holderId, acquiredAt, lastHeartbeat, metadata));
            }

            return holders;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting holders for semaphore {SemaphoreName}", semaphoreName);
            throw new SemaphoreProviderException("Failed to get holders", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHoldingAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);

        try
        {
            IDatabase database = GetDatabase();
            string holdersKey = GetHoldersKey(semaphoreName);

            double? score = await database.SortedSetScoreAsync(holdersKey, holderId);
            return score.HasValue;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if holder {HolderId} is holding semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to check holding status", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
            return false;

        try
        {
            IDatabase database = GetDatabase();
            await database.PingAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for Redis semaphore provider");
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (connectionMultiplexer.IsValueCreated)
            connectionMultiplexer.Value.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (!isDisposed) return;
        throw new ObjectDisposedException(nameof(RedisSemaphoreProvider));
    }

    private IDatabase GetDatabase()
    {
        return connectionMultiplexer.Value.GetDatabase(options.Database);
    }

    private string GetHoldersKey(string semaphoreName)
    {
        return $"{options.KeyPrefix}:{semaphoreName}:holders";
    }

    private string GetDataKey(string semaphoreName)
    {
        return $"{options.KeyPrefix}:{semaphoreName}:data";
    }

    /// <summary>
    /// Internal class to represent holder data stored in Redis.
    /// </summary>
    private sealed class HolderData
    {
        public string HolderId { get; set; } = string.Empty;
        public DateTimeOffset AcquiredAt { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}

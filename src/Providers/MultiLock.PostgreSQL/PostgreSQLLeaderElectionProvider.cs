using System.Data;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;
using Npgsql;

namespace MultiLock.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of the leader election provider.
/// </summary>
public sealed class PostgreSqlLeaderElectionProvider : ILeaderElectionProvider
{
    private readonly PostgreSqlLeaderElectionOptions options;
    private readonly ILogger<PostgreSqlLeaderElectionProvider> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool isInitialized;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlLeaderElectionProvider"/> class.
    /// </summary>
    /// <param name="options">The PostgreSQL options.</param>
    /// <param name="logger">The logger.</param>
    public PostgreSqlLeaderElectionProvider(
        IOptions<PostgreSqlLeaderElectionOptions> options,
        ILogger<PostgreSqlLeaderElectionProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string metadataJson = JsonSerializer.Serialize(metadata);
            DateTimeOffset expiryTime = now.Subtract(lockTimeout);

            // Try to acquire leadership using INSERT ... ON CONFLICT for atomic operation
            string sql = $@"
                INSERT INTO {options.SchemaName}.{options.TableName}
                    (election_group, leader_id, leadership_acquired_at, last_heartbeat, metadata)
                VALUES (@ElectionGroup, @ParticipantId, @Now, @Now, @Metadata::jsonb)
                ON CONFLICT (election_group) DO UPDATE SET
                    leader_id = @ParticipantId,
                    leadership_acquired_at = @Now,
                    last_heartbeat = @Now,
                    metadata = @Metadata::jsonb
                WHERE {options.SchemaName}.{options.TableName}.last_heartbeat < @ExpiryTime
                   OR {options.SchemaName}.{options.TableName}.leader_id = @ParticipantId
                RETURNING CASE WHEN xmax = 0 THEN 1 ELSE 1 END;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);
            command.Parameters.AddWithValue("@ParticipantId", participantId);
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@ExpiryTime", expiryTime);
            command.Parameters.AddWithValue("@Metadata", metadataJson);

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            bool acquired = result != null;

            if (acquired)
            {
                logger.LogInformation("Successfully acquired leadership for participant {ParticipantId} in group {ElectionGroup}",
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                DELETE FROM {options.SchemaName}.{options.TableName}
                WHERE election_group = @ElectionGroup AND leader_id = @ParticipantId";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);
            command.Parameters.AddWithValue("@ParticipantId", participantId);

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

            if (rowsAffected > 0)
            {
                logger.LogInformation("Released leadership for participant {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);
            }
            else
            {
                logger.LogWarning("No leadership found to release for participant {ParticipantId} in group {ElectionGroup}",
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string metadataJson = JsonSerializer.Serialize(metadata);

            string sql = $@"
                UPDATE {options.SchemaName}.{options.TableName}
                SET last_heartbeat = @Now, metadata = @Metadata::jsonb
                WHERE election_group = @ElectionGroup AND leader_id = @ParticipantId";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);
            command.Parameters.AddWithValue("@ParticipantId", participantId);
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@Metadata", metadataJson);

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            bool updated = rowsAffected > 0;

            logger.LogDebug("Updated heartbeat for leader {ParticipantId} in group {ElectionGroup}: {Updated}",
                participantId, electionGroup, updated);

            return updated;
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                SELECT leader_id, leadership_acquired_at, last_heartbeat, metadata
                FROM {options.SchemaName}.{options.TableName}
                WHERE election_group = @ElectionGroup";

            using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = options.CommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken)) return null;

            string leaderId = reader.GetString("leader_id");
            DateTimeOffset leadershipAcquiredAt = reader.GetFieldValue<DateTimeOffset>("leadership_acquired_at");
            DateTimeOffset lastHeartbeat = reader.GetFieldValue<DateTimeOffset>("last_heartbeat");
            string metadataJson = reader.GetString("metadata");

            Dictionary<string, string> metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson)
                                                  ?? new Dictionary<string, string>();

            return new LeaderInfo(leaderId, leadershipAcquiredAt, lastHeartbeat, metadata);

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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                SELECT COUNT(1)
                FROM {options.SchemaName}.{options.TableName}
                WHERE election_group = @ElectionGroup AND leader_id = @ParticipantId";

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = options.CommandTimeoutSeconds
            };

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);
            command.Parameters.AddWithValue("@ParticipantId", participantId);

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if participant {ParticipantId} is leader in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to check leadership status", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
            return false;

        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for PostgreSQL provider");
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        initializationLock.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (!isDisposed) return;
        throw new ObjectDisposedException(nameof(PostgreSqlLeaderElectionProvider));
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (isInitialized)
            return;

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (isInitialized)
                return;

            if (options.AutoCreateTable)
                await CreateTableIfNotExistsAsync(cancellationToken);

            isInitialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task CreateTableIfNotExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Create schema if it doesn't exist
            string createSchemaSql = $@"CREATE SCHEMA IF NOT EXISTS {options.SchemaName};";

            await using var schemaCommand = new NpgsqlCommand(createSchemaSql, connection);
            schemaCommand.CommandTimeout = options.CommandTimeoutSeconds;

            await schemaCommand.ExecuteNonQueryAsync(cancellationToken);

            // Create table if it doesn't exist
            string sql = $@"
                CREATE TABLE IF NOT EXISTS {options.SchemaName}.{options.TableName} (
                    election_group VARCHAR(255) PRIMARY KEY,
                    leader_id VARCHAR(255) NOT NULL,
                    leadership_acquired_at TIMESTAMPTZ NOT NULL,
                    last_heartbeat TIMESTAMPTZ NOT NULL,
                    metadata JSONB NOT NULL DEFAULT '{{}}'::jsonb
                );

                CREATE INDEX IF NOT EXISTS idx_{options.TableName}_last_heartbeat
                ON {options.SchemaName}.{options.TableName} (last_heartbeat);";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            await command.ExecuteNonQueryAsync(cancellationToken);

            logger.LogInformation("Leader election table {Schema}.{Table} created or verified",
                options.SchemaName, options.TableName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating leader election table {Schema}.{Table}",
                options.SchemaName, options.TableName);
            throw new LeaderElectionProviderException("Failed to create leader election table", ex);
        }
    }
}

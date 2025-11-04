using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;

namespace MultiLock.SqlServer;

/// <summary>
/// SQL Server implementation of the leader election provider.
/// </summary>
public sealed class SqlServerLeaderElectionProvider : ILeaderElectionProvider
{
    private readonly SqlServerLeaderElectionOptions options;
    private readonly ILogger<SqlServerLeaderElectionProvider> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool isInitialized;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerLeaderElectionProvider"/> class.
    /// </summary>
    /// <param name="options">The SQL Server options.</param>
    /// <param name="logger">The logger.</param>
    public SqlServerLeaderElectionProvider(
        IOptions<SqlServerLeaderElectionOptions> options,
        ILogger<SqlServerLeaderElectionProvider> logger)
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
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        ParameterValidation.ValidateMetadata(metadata);
        ParameterValidation.ValidateLockTimeout(lockTimeout);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string metadataJson = JsonSerializer.Serialize(metadata);
            DateTimeOffset expiryTime = now.Subtract(lockTimeout);

            // Try to acquire leadership using MERGE for atomic operation
            string sql = $@"
                MERGE [{options.SchemaName}].[{options.TableName}] AS target
                USING (SELECT @ElectionGroup AS ElectionGroup) AS source
                ON target.ElectionGroup = source.ElectionGroup
                WHEN MATCHED AND (target.LastHeartbeat < @ExpiryTime OR target.LeaderId = @ParticipantId) THEN
                    UPDATE SET
                        LeaderId = @ParticipantId,
                        LeadershipAcquiredAt = @Now,
                        LastHeartbeat = @Now,
                        Metadata = @Metadata
                WHEN NOT MATCHED THEN
                    INSERT (ElectionGroup, LeaderId, LeadershipAcquiredAt, LastHeartbeat, Metadata)
                    VALUES (@ElectionGroup, @ParticipantId, @Now, @Now, @Metadata);

                SELECT CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);
            command.Parameters.AddWithValue("@ParticipantId", participantId);
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@ExpiryTime", expiryTime);
            command.Parameters.AddWithValue("@Metadata", metadataJson);

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            bool acquired = Convert.ToInt32(result) > 0;

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
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                DELETE FROM [{options.SchemaName}].[{options.TableName}]
                WHERE ElectionGroup = @ElectionGroup AND LeaderId = @ParticipantId";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);
            command.Parameters.AddWithValue("@ParticipantId", participantId);

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

            logger.LogInformation("Released leadership for participant {ParticipantId} in group {ElectionGroup}. Rows affected: {RowsAffected}",
                participantId, electionGroup, rowsAffected);
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string metadataJson = JsonSerializer.Serialize(metadata);

            string sql = $@"
                UPDATE [{options.SchemaName}].[{options.TableName}]
                SET LastHeartbeat = @Now, Metadata = @Metadata
                WHERE ElectionGroup = @ElectionGroup AND LeaderId = @ParticipantId";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);
            command.Parameters.AddWithValue("@ParticipantId", participantId);
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@Metadata", metadataJson);

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            return rowsAffected > 0;
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                SELECT LeaderId, LeadershipAcquiredAt, LastHeartbeat, Metadata
                FROM [{options.SchemaName}].[{options.TableName}]
                WHERE ElectionGroup = @ElectionGroup";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);

            await using SqlDataReader? reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return null;

            string leaderId = reader.GetString("LeaderId");
            DateTimeOffset leadershipAcquiredAt = reader.GetDateTimeOffset(reader.GetOrdinal("LeadershipAcquiredAt"));
            DateTimeOffset lastHeartbeat = reader.GetDateTimeOffset(reader.GetOrdinal("LastHeartbeat"));
            string metadataJson = reader.GetString("Metadata");

            Dictionary<string, string> metadata = string.IsNullOrEmpty(metadataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>();

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
        ParameterValidation.ValidateElectionGroup(electionGroup);
        ParameterValidation.ValidateParticipantId(participantId);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                SELECT COUNT(1)
                FROM [{options.SchemaName}].[{options.TableName}]
                WHERE ElectionGroup = @ElectionGroup AND LeaderId = @ParticipantId";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            command.Parameters.AddWithValue("@ElectionGroup", electionGroup);
            command.Parameters.AddWithValue("@ParticipantId", participantId);

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to check leadership", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
            return false;

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand("SELECT 1", connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for SQL Server provider");
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            initializationLock.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (isDisposed)
            throw new ObjectDisposedException(nameof(SqlServerLeaderElectionProvider));
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
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{options.SchemaName}].[{options.TableName}]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE [{options.SchemaName}].[{options.TableName}] (
                        ElectionGroup NVARCHAR(255) NOT NULL PRIMARY KEY,
                        LeaderId NVARCHAR(255) NOT NULL,
                        LeadershipAcquiredAt DATETIMEOFFSET NOT NULL,
                        LastHeartbeat DATETIMEOFFSET NOT NULL,
                        Metadata NVARCHAR(MAX) NULL
                    );

                    CREATE INDEX IX_{options.TableName}_LastHeartbeat ON [{options.SchemaName}].[{options.TableName}] (LastHeartbeat);
                END";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            await command.ExecuteNonQueryAsync(cancellationToken);

            logger.LogInformation("Leader election table [{Schema}].[{Table}] created or verified",
                options.SchemaName, options.TableName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating leader election table [{Schema}].[{Table}]",
                options.SchemaName, options.TableName);
            throw new LeaderElectionProviderException("Failed to create leader election table", ex);
        }
    }
}

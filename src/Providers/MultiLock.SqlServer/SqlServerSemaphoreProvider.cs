using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;

namespace MultiLock.SqlServer;

/// <summary>
/// SQL Server implementation of the semaphore provider.
/// </summary>
public sealed class SqlServerSemaphoreProvider : ISemaphoreProvider
{
    private readonly SqlServerSemaphoreOptions options;
    private readonly ILogger<SqlServerSemaphoreProvider> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool isInitialized;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerSemaphoreProvider"/> class.
    /// </summary>
    /// <param name="options">The SQL Server options.</param>
    /// <param name="logger">The logger.</param>
    public SqlServerSemaphoreProvider(
        IOptions<SqlServerSemaphoreOptions> options,
        ILogger<SqlServerSemaphoreProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string metadataJson = JsonSerializer.Serialize(metadata);
            DateTimeOffset expiryTime = now.Subtract(slotTimeout);

            // ReadCommitted is sufficient because sp_getapplock below provides an exclusive
            // per-semaphore serialisation point. SERIALIZABLE + range locks on an empty table
            // causes deadlocks: two concurrent transactions take key-range locks on the empty
            // SemaphoreName range, then deadlock when both try to INSERT.
            await using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);

            try
            {
                // Acquire an exclusive application lock for this semaphore name, scoped to the
                // transaction lifetime. sp_getapplock serialises all concurrent callers without
                // requiring any existing rows (unlike UPDLOCK/HOLDLOCK on an empty range).
                // Return values: 0 = granted, 1 = granted after waiting; < 0 = error/timeout.
                const string appLockSql = @"
                    DECLARE @result INT;
                    EXEC @result = sp_getapplock
                        @Resource    = @SemaphoreName,
                        @LockMode    = 'Exclusive',
                        @LockOwner   = 'Transaction',
                        @LockTimeout = @TimeoutMs;
                    IF @result < 0
                        THROW 50001, 'sp_getapplock failed to acquire exclusive lock', 1;";

                await using (var lockCommand = new SqlCommand(appLockSql, connection, transaction))
                {
                    lockCommand.CommandTimeout = options.CommandTimeoutSeconds;
                    lockCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                    lockCommand.Parameters.AddWithValue("@TimeoutMs", options.CommandTimeoutSeconds * 1000);
                    await lockCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                // Clean up expired holders
                string cleanupSql = $@"
                    DELETE FROM [{options.SchemaName}].[{options.TableName}]
                    WHERE SemaphoreName = @SemaphoreName AND LastHeartbeat < @ExpiryTime";

                await using (var cleanupCommand = new SqlCommand(cleanupSql, connection, transaction))
                {
                    cleanupCommand.CommandTimeout = options.CommandTimeoutSeconds;
                    cleanupCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                    cleanupCommand.Parameters.AddWithValue("@ExpiryTime", expiryTime);
                    await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                // Check if holder already exists (renewal case)
                string checkExistingSql = $@"
                    SELECT COUNT(1) FROM [{options.SchemaName}].[{options.TableName}]
                    WHERE SemaphoreName = @SemaphoreName AND HolderId = @HolderId";

                await using (var checkCommand = new SqlCommand(checkExistingSql, connection, transaction))
                {
                    checkCommand.CommandTimeout = options.CommandTimeoutSeconds;
                    checkCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                    checkCommand.Parameters.AddWithValue("@HolderId", holderId);

                    int existingCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken));
                    if (existingCount > 0)
                    {
                        string renewSql = $@"
                            UPDATE [{options.SchemaName}].[{options.TableName}]
                            SET LastHeartbeat = @Now, Metadata = @Metadata
                            WHERE SemaphoreName = @SemaphoreName AND HolderId = @HolderId";

                        await using var renewCommand = new SqlCommand(renewSql, connection, transaction);
                        renewCommand.CommandTimeout = options.CommandTimeoutSeconds;
                        renewCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                        renewCommand.Parameters.AddWithValue("@HolderId", holderId);
                        renewCommand.Parameters.AddWithValue("@Now", now);
                        renewCommand.Parameters.AddWithValue("@Metadata", metadataJson);
                        await renewCommand.ExecuteNonQueryAsync(cancellationToken);

                        await transaction.CommitAsync(cancellationToken);
                        logger.LogInformation("Renewed slot for holder {HolderId} in semaphore {SemaphoreName}",
                            holderId, semaphoreName);
                        return true;
                    }
                }

                // Count current holders (app lock guarantees no concurrent mutations)
                string countSql = $@"
                    SELECT COUNT(1) FROM [{options.SchemaName}].[{options.TableName}]
                    WHERE SemaphoreName = @SemaphoreName";

                int currentCount;
                await using (var countCommand = new SqlCommand(countSql, connection, transaction))
                {
                    countCommand.CommandTimeout = options.CommandTimeoutSeconds;
                    countCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                    currentCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
                }

                if (currentCount >= maxCount)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    logger.LogDebug("Failed to acquire slot for holder {HolderId} in semaphore {SemaphoreName} - full ({CurrentCount}/{MaxCount})",
                        holderId, semaphoreName, currentCount, maxCount);
                    return false;
                }

                // Insert new holder
                string insertSql = $@"
                    INSERT INTO [{options.SchemaName}].[{options.TableName}]
                    (SemaphoreName, HolderId, AcquiredAt, LastHeartbeat, Metadata)
                    VALUES (@SemaphoreName, @HolderId, @Now, @Now, @Metadata)";

                await using (var insertCommand = new SqlCommand(insertSql, connection, transaction))
                {
                    insertCommand.CommandTimeout = options.CommandTimeoutSeconds;
                    insertCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                    insertCommand.Parameters.AddWithValue("@HolderId", holderId);
                    insertCommand.Parameters.AddWithValue("@Now", now);
                    insertCommand.Parameters.AddWithValue("@Metadata", metadataJson);
                    await insertCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Acquired slot for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
                return true;
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { /* already rolled back */ }
                throw;
            }
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                DELETE FROM [{options.SchemaName}].[{options.TableName}]
                WHERE SemaphoreName = @SemaphoreName AND HolderId = @HolderId";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
            command.Parameters.AddWithValue("@HolderId", holderId);

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

            logger.LogInformation("Released slot for holder {HolderId} in semaphore {SemaphoreName}. Rows affected: {RowsAffected}",
                holderId, semaphoreName, rowsAffected);
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
                WHERE SemaphoreName = @SemaphoreName AND HolderId = @HolderId";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
            command.Parameters.AddWithValue("@HolderId", holderId);
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@Metadata", metadataJson);

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            return rowsAffected > 0;
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
        ParameterValidation.ValidateSlotTimeout(slotTimeout);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset expiryTime = DateTimeOffset.UtcNow.Subtract(slotTimeout);

            string sql = $@"
                SELECT COUNT(1) FROM [{options.SchemaName}].[{options.TableName}]
                WHERE SemaphoreName = @SemaphoreName AND LastHeartbeat >= @ExpiryTime";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
            command.Parameters.AddWithValue("@ExpiryTime", expiryTime);

            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                SELECT HolderId, AcquiredAt, LastHeartbeat, Metadata
                FROM [{options.SchemaName}].[{options.TableName}]
                WHERE SemaphoreName = @SemaphoreName
                ORDER BY AcquiredAt";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);

            var holders = new List<SemaphoreHolder>();
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                string holderId = reader.GetString(reader.GetOrdinal("HolderId"));
                DateTimeOffset acquiredAt = reader.GetDateTimeOffset(reader.GetOrdinal("AcquiredAt"));
                DateTimeOffset lastHeartbeat = reader.GetDateTimeOffset(reader.GetOrdinal("LastHeartbeat"));
                string metadataJson = reader.GetString(reader.GetOrdinal("Metadata"));

                Dictionary<string, string> metadata = string.IsNullOrEmpty(metadataJson)
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>();

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
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                SELECT COUNT(1) FROM [{options.SchemaName}].[{options.TableName}]
                WHERE SemaphoreName = @SemaphoreName AND HolderId = @HolderId";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
            command.Parameters.AddWithValue("@HolderId", holderId);

            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
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
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand("SELECT 1", connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for SQL Server semaphore provider");
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
        if (isDisposed)
            throw new ObjectDisposedException(nameof(SqlServerSemaphoreProvider));
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

            // Use separate idempotent statements so a partial failure (e.g., table created but
            // index not yet) is recovered on the next initialisation without skipping the index.
            string tableName = options.TableName;
            string pkName = $"PK_{(tableName.Length > 125 ? tableName[..125] : tableName)}";
            string ixName = $"IX_{(tableName.Length > 97 ? tableName[..97] : tableName)}_SemaphoreName_LastHeartbeat";

            string sql = $@"
                IF OBJECT_ID(N'[{options.SchemaName}].[{tableName}]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [{options.SchemaName}].[{tableName}] (
                        SemaphoreName NVARCHAR(255) NOT NULL,
                        HolderId NVARCHAR(255) NOT NULL,
                        AcquiredAt DATETIMEOFFSET NOT NULL,
                        LastHeartbeat DATETIMEOFFSET NOT NULL,
                        Metadata NVARCHAR(MAX) NULL,
                        CONSTRAINT {pkName} PRIMARY KEY (SemaphoreName, HolderId)
                    );
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'{ixName}'
                      AND object_id = OBJECT_ID(N'[{options.SchemaName}].[{tableName}]')
                )
                BEGIN
                    CREATE INDEX {ixName}
                        ON [{options.SchemaName}].[{tableName}] (SemaphoreName, LastHeartbeat);
                END";

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            await command.ExecuteNonQueryAsync(cancellationToken);

            logger.LogInformation("Semaphore holders table [{Schema}].[{Table}] created or verified",
                options.SchemaName, options.TableName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating semaphore holders table [{Schema}].[{Table}]",
                options.SchemaName, options.TableName);
            throw new SemaphoreProviderException("Failed to create semaphore holders table", ex);
        }
    }
}

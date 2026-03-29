using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;
using Npgsql;

namespace MultiLock.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of the semaphore provider.
/// </summary>
public sealed class PostgreSqlSemaphoreProvider : ISemaphoreProvider
{
    private readonly PostgreSqlSemaphoreOptions options;
    private readonly ILogger<PostgreSqlSemaphoreProvider> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool isInitialized;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlSemaphoreProvider"/> class.
    /// </summary>
    /// <param name="options">The PostgreSQL options.</param>
    /// <param name="logger">The logger.</param>
    public PostgreSqlSemaphoreProvider(
        IOptions<PostgreSqlSemaphoreOptions> options,
        ILogger<PostgreSqlSemaphoreProvider> logger)
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
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string metadataJson = JsonSerializer.Serialize(metadata);
            DateTimeOffset expiryTime = now.Subtract(slotTimeout);

            // ReadCommitted is sufficient because we use a per-semaphore advisory lock below to
            // serialize all concurrent acquisitions for the same semaphore name. SERIALIZABLE +
            // FOR UPDATE fails when the table is empty because there are no rows to lock, causing
            // all concurrent transactions to see count=0 and then receive serialization failures.
            await using var transaction = await connection.BeginTransactionAsync(
                System.Data.IsolationLevel.ReadCommitted, cancellationToken);

            try
            {
                // Acquire an exclusive advisory lock scoped to this transaction.
                // hashtext() maps the semaphore name to a stable int4 key; the advisory lock
                // serialises all concurrent callers for the same semaphore without requiring any
                // existing rows in the table (unlike FOR UPDATE).
                string advisoryLockSql = "SELECT pg_advisory_xact_lock(hashtext(@SemaphoreName))";
                await using (var lockCommand = new NpgsqlCommand(advisoryLockSql, connection, transaction))
                {
                    lockCommand.CommandTimeout = options.CommandTimeoutSeconds;
                    lockCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                    await lockCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                // Clean up expired slots
                string cleanupSql = $@"
                    DELETE FROM {options.SchemaName}.{options.TableName}
                    WHERE semaphore_name = @SemaphoreName AND last_heartbeat < @ExpiryTime";

                await using (var cleanupCommand = new NpgsqlCommand(cleanupSql, connection, transaction))
                {
                    cleanupCommand.CommandTimeout = options.CommandTimeoutSeconds;
                    cleanupCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                    cleanupCommand.Parameters.AddWithValue("@ExpiryTime", expiryTime);
                    await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                // Check if holder already has a slot (renew it)
                string checkExistingSql = $@"
                    UPDATE {options.SchemaName}.{options.TableName}
                    SET last_heartbeat = @Now, metadata = @Metadata::jsonb
                    WHERE semaphore_name = @SemaphoreName AND holder_id = @HolderId
                    RETURNING 1";

                await using (var checkCommand = new NpgsqlCommand(checkExistingSql, connection, transaction))
                {
                    checkCommand.CommandTimeout = options.CommandTimeoutSeconds;
                    checkCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                    checkCommand.Parameters.AddWithValue("@HolderId", holderId);
                    checkCommand.Parameters.AddWithValue("@Now", now);
                    checkCommand.Parameters.AddWithValue("@Metadata", metadataJson);

                    object? existingResult = await checkCommand.ExecuteScalarAsync(cancellationToken);
                    if (existingResult != null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        logger.LogDebug("Renewed existing slot for holder {HolderId} in semaphore {SemaphoreName}",
                            holderId, semaphoreName);
                        return true;
                    }
                }

                // Count current holders (advisory lock guarantees no concurrent inserts)
                string countSql = $@"
                    SELECT COUNT(*) FROM {options.SchemaName}.{options.TableName}
                    WHERE semaphore_name = @SemaphoreName";

                int currentCount;
                await using (var countCommand = new NpgsqlCommand(countSql, connection, transaction))
                {
                    countCommand.CommandTimeout = options.CommandTimeoutSeconds;
                    countCommand.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
                    currentCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
                }

                if (currentCount >= maxCount)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    logger.LogDebug("Semaphore {SemaphoreName} is full ({CurrentCount}/{MaxCount})",
                        semaphoreName, currentCount, maxCount);
                    return false;
                }

                // Insert new holder
                string insertSql = $@"
                    INSERT INTO {options.SchemaName}.{options.TableName}
                        (semaphore_name, holder_id, acquired_at, last_heartbeat, metadata)
                    VALUES (@SemaphoreName, @HolderId, @Now, @Now, @Metadata::jsonb)";

                await using (var insertCommand = new NpgsqlCommand(insertSql, connection, transaction))
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
                // Only rollback if the transaction is still active
                if (transaction.Connection != null)
                    await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not SemaphoreException)
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
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                DELETE FROM {options.SchemaName}.{options.TableName}
                WHERE semaphore_name = @SemaphoreName AND holder_id = @HolderId";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
            command.Parameters.AddWithValue("@HolderId", holderId);

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

            if (rowsAffected > 0)
                logger.LogInformation("Released slot for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
            else
                logger.LogWarning("No slot found to release for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
        }
        catch (OperationCanceledException)
        {
            throw;
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
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string metadataJson = JsonSerializer.Serialize(metadata);

            string sql = $@"
                UPDATE {options.SchemaName}.{options.TableName}
                SET last_heartbeat = @Now, metadata = @Metadata::jsonb
                WHERE semaphore_name = @SemaphoreName AND holder_id = @HolderId";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
            command.Parameters.AddWithValue("@HolderId", holderId);
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@Metadata", metadataJson);

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            bool updated = rowsAffected > 0;

            logger.LogDebug("Updated heartbeat for holder {HolderId} in semaphore {SemaphoreName}: {Updated}",
                holderId, semaphoreName, updated);

            return updated;
        }
        catch (OperationCanceledException)
        {
            throw;
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
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset expiryTime = DateTimeOffset.UtcNow.Subtract(slotTimeout);

            string sql = $@"
                SELECT COUNT(*) FROM {options.SchemaName}.{options.TableName}
                WHERE semaphore_name = @SemaphoreName AND last_heartbeat >= @ExpiryTime";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
            command.Parameters.AddWithValue("@ExpiryTime", expiryTime);

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }
        catch (OperationCanceledException)
        {
            throw;
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
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                SELECT holder_id, acquired_at, last_heartbeat, metadata
                FROM {options.SchemaName}.{options.TableName}
                WHERE semaphore_name = @SemaphoreName
                ORDER BY acquired_at";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            var holders = new List<SemaphoreHolder>();
            while (await reader.ReadAsync(cancellationToken))
            {
                string holderId = reader.GetString(reader.GetOrdinal("holder_id"));
                DateTimeOffset acquiredAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("acquired_at"));
                DateTimeOffset lastHeartbeat = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_heartbeat"));
                string metadataJson = reader.GetString(reader.GetOrdinal("metadata"));

                Dictionary<string, string> metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson)
                                                      ?? new Dictionary<string, string>();

                holders.Add(new SemaphoreHolder(holderId, acquiredAt, lastHeartbeat, metadata));
            }

            return holders;
        }
        catch (OperationCanceledException)
        {
            throw;
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
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string sql = $@"
                SELECT COUNT(1) FROM {options.SchemaName}.{options.TableName}
                WHERE semaphore_name = @SemaphoreName AND holder_id = @HolderId";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@SemaphoreName", semaphoreName);
            command.Parameters.AddWithValue("@HolderId", holderId);

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
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
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;

            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for PostgreSQL semaphore provider");
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
        throw new ObjectDisposedException(nameof(PostgreSqlSemaphoreProvider));
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
                    semaphore_name VARCHAR(255) NOT NULL,
                    holder_id VARCHAR(255) NOT NULL,
                    acquired_at TIMESTAMPTZ NOT NULL,
                    last_heartbeat TIMESTAMPTZ NOT NULL,
                    metadata JSONB NOT NULL DEFAULT '{{}}'::jsonb,
                    PRIMARY KEY (semaphore_name, holder_id)
                );

                CREATE INDEX IF NOT EXISTS idx_{(options.TableName.Length > 56 ? options.TableName[..56] : options.TableName)}_hb
                ON {options.SchemaName}.{options.TableName} (semaphore_name, last_heartbeat);";

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = options.CommandTimeoutSeconds;
            await command.ExecuteNonQueryAsync(cancellationToken);

            logger.LogInformation("Semaphore table {Schema}.{Table} created or verified",
                options.SchemaName, options.TableName);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P07" or "42710" or "23505")
        {
            // 42P07: relation already exists - table was created concurrently
            // 42710: type already exists - another process created the table concurrently
            // 23505: duplicate key - another process created the table concurrently
            // All are safe to ignore since the table now exists
            logger.LogDebug("Semaphore table {Schema}.{Table} was created by another process",
                options.SchemaName, options.TableName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating semaphore table {Schema}.{Table}",
                options.SchemaName, options.TableName);
            throw new SemaphoreProviderException("Failed to create semaphore table", ex);
        }
    }
}

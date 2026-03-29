namespace MultiLock.PostgreSQL;

/// <summary>
/// Configuration options for the PostgreSQL semaphore provider.
/// </summary>
public sealed class PostgreSqlSemaphoreOptions
{
    /// <summary>
    /// Gets or sets the PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the table used for semaphore holders.
    /// Default is "semaphore_holders".
    /// </summary>
    public string TableName { get; set; } = "semaphore_holders";

    /// <summary>
    /// Gets or sets the schema name for the semaphore table.
    /// Default is "public".
    /// </summary>
    public string SchemaName { get; set; } = "public";

    /// <summary>
    /// Gets or sets a value indicating whether to automatically create the semaphore table if it doesn't exist.
    /// Default is true.
    /// </summary>
    public bool AutoCreateTable { get; set; } = true;

    /// <summary>
    /// Gets or sets the command timeout in seconds for PostgreSQL operations.
    /// Default is 30 seconds.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(ConnectionString));

        if (string.IsNullOrWhiteSpace(TableName))
            throw new ArgumentException("Table name cannot be null or empty.", nameof(TableName));

        if (string.IsNullOrWhiteSpace(SchemaName))
            throw new ArgumentException("Schema name cannot be null or empty.", nameof(SchemaName));

        ParameterValidation.ValidateSqlIdentifier(TableName, nameof(TableName));
        ParameterValidation.ValidateSqlIdentifier(SchemaName, nameof(SchemaName));

        if (CommandTimeoutSeconds <= 0)
            throw new ArgumentException("Command timeout must be positive.", nameof(CommandTimeoutSeconds));
    }
}


namespace MultiLock.PostgreSQL;

/// <summary>
/// Configuration options for the PostgreSQL leader election provider.
/// </summary>
public sealed class PostgreSqlLeaderElectionOptions
{
    /// <summary>
    /// Gets or sets the PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the table used for leader election.
    /// Default is "leader_election".
    /// </summary>
    public string TableName { get; set; } = "leader_election";

    /// <summary>
    /// Gets or sets the schema name for the leader election table.
    /// Default is "public".
    /// </summary>
    public string SchemaName { get; set; } = "public";

    /// <summary>
    /// Gets or sets a value indicating whether to automatically create the leader election table if it doesn't exist.
    /// Default is true.
    /// </summary>
    public bool AutoCreateTable { get; init; } = true;

    /// <summary>
    /// Gets or sets the command timeout in seconds for PostgreSQL operations.
    /// Default is 30 seconds.
    /// </summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

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

        if (CommandTimeoutSeconds <= 0)
            throw new ArgumentException("Command timeout must be positive.", nameof(CommandTimeoutSeconds));
    }
}

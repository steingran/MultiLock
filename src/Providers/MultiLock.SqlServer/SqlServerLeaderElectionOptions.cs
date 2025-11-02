namespace MultiLock.SqlServer;

/// <summary>
/// Configuration options for the SQL Server leader election provider.
/// </summary>
public sealed class SqlServerLeaderElectionOptions
{
    /// <summary>
    /// Gets or sets the SQL Server connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the table used for leader election.
    /// Default is "LeaderElection".
    /// </summary>
    public string TableName { get; set; } = "LeaderElection";

    /// <summary>
    /// Gets or sets the schema name for the leader election table.
    /// Default is "dbo".
    /// </summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>
    /// Gets or sets a value indicating whether to automatically create the leader election table if it doesn't exist.
    /// Default is true.
    /// </summary>
    public bool AutoCreateTable { get; set; } = true;

    /// <summary>
    /// Gets or sets the command timeout in seconds for SQL operations.
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
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(ConnectionString));
        }

        if (string.IsNullOrWhiteSpace(TableName))
        {
            throw new ArgumentException("Table name cannot be null or empty.", nameof(TableName));
        }

        if (string.IsNullOrWhiteSpace(SchemaName))
        {
            throw new ArgumentException("Schema name cannot be null or empty.", nameof(SchemaName));
        }

        if (CommandTimeoutSeconds <= 0)
        {
            throw new ArgumentException("Command timeout must be positive.", nameof(CommandTimeoutSeconds));
        }
    }
}

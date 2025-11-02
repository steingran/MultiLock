namespace MultiLock.Redis;

/// <summary>
/// Configuration options for the Redis leader election provider.
/// </summary>
public sealed class RedisLeaderElectionOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the key prefix for leader election keys.
    /// Default is "leader-election".
    /// </summary>
    public string KeyPrefix { get; set; } = "leader-election";

    /// <summary>
    /// Gets or sets the database number to use.
    /// Default is 0.
    /// </summary>
    public int Database { get; set; } = 0;

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

        if (string.IsNullOrWhiteSpace(KeyPrefix))
        {
            throw new ArgumentException("Key prefix cannot be null or empty.", nameof(KeyPrefix));
        }

        if (Database < 0)
        {
            throw new ArgumentException("Database number cannot be negative.", nameof(Database));
        }
    }
}

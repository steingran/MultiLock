namespace MultiLock.Redis;

/// <summary>
/// Configuration options for the Redis semaphore provider.
/// </summary>
public sealed class RedisSemaphoreOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the key prefix for semaphore keys.
    /// Default is "semaphore".
    /// </summary>
    public string KeyPrefix { get; set; } = "semaphore";

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
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(ConnectionString));

        if (string.IsNullOrWhiteSpace(KeyPrefix))
            throw new ArgumentException("Key prefix cannot be null or empty.", nameof(KeyPrefix));

        if (Database < 0)
            throw new ArgumentException("Database number cannot be negative.", nameof(Database));
    }
}


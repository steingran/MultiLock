namespace MultiLock.ZooKeeper;

/// <summary>
/// Configuration options for the ZooKeeper leader election provider.
/// </summary>
public sealed class ZooKeeperLeaderElectionOptions
{
    /// <summary>
    /// Gets or sets the ZooKeeper connection string.
    /// Default is "localhost:2181".
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:2181";

    /// <summary>
    /// Gets or sets the root path for leader election nodes in ZooKeeper.
    /// Default is "/leader-election".
    /// </summary>
    public string RootPath { get; set; } = "/leader-election";

    /// <summary>
    /// Gets or sets the session timeout for ZooKeeper connections.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the connection timeout for ZooKeeper connections.
    /// Default is 10 seconds.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the retry policy for ZooKeeper operations.
    /// Default is 3 retries with exponential backoff.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay for retry operations.
    /// Default is 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets a value indicating whether to automatically create the root path if it doesn't exist.
    /// Default is true.
    /// </summary>
    public bool AutoCreateRootPath { get; set; } = true;

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

        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new ArgumentException("Root path cannot be null or empty.", nameof(RootPath));
        }

        if (!RootPath.StartsWith("/"))
        {
            throw new ArgumentException("Root path must start with '/'.", nameof(RootPath));
        }

        if (SessionTimeout <= TimeSpan.Zero || SessionTimeout > TimeSpan.FromMinutes(60))
        {
            throw new ArgumentException("Session timeout must be between 0 and 60 minutes.", nameof(SessionTimeout));
        }

        if (ConnectionTimeout <= TimeSpan.Zero || ConnectionTimeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentException("Connection timeout must be between 0 and 10 minutes.", nameof(ConnectionTimeout));
        }

        if (MaxRetries < 0 || MaxRetries > 10)
        {
            throw new ArgumentException("Max retries must be between 0 and 10.", nameof(MaxRetries));
        }

        if (RetryDelay < TimeSpan.Zero || RetryDelay > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentException("Retry delay must be between 0 and 5 minutes.", nameof(RetryDelay));
        }
    }
}

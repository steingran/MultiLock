namespace MultiLock.Consul;

/// <summary>
/// Configuration options for the Consul leader election provider.
/// </summary>
public sealed class ConsulLeaderElectionOptions
{
    /// <summary>
    /// Gets or sets the Consul server address.
    /// Default is "http://localhost:8500".
    /// </summary>
    public string Address { get; set; } = "http://localhost:8500";

    /// <summary>
    /// Gets or inits the datacenter name.
    /// If not specified, uses the default datacenter.
    /// </summary>
    public string? Datacenter { get; init; }

    /// <summary>
    /// Gets or sets the ACL token for authentication.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or inits the key prefix for leader election keys.
    /// Default is "leader-election".
    /// </summary>
    public string KeyPrefix { get; init; } = "leader-election";

    /// <summary>
    /// Gets or inits the session TTL for Consul sessions.
    /// Default is 60 seconds.
    /// </summary>
    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or inits the session lock delay.
    /// Default is 15 seconds.
    /// </summary>
    public TimeSpan SessionLockDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Address))
            throw new ArgumentException("Address cannot be null or empty.", nameof(Address));

        if (string.IsNullOrWhiteSpace(KeyPrefix))
            throw new ArgumentException("Key prefix cannot be null or empty.", nameof(KeyPrefix));

        if (SessionTtl < TimeSpan.FromSeconds(10) || SessionTtl > TimeSpan.FromHours(24))
            throw new ArgumentException("Session TTL must be between 10 seconds and 24 hours.", nameof(SessionTtl));

        if (SessionLockDelay < TimeSpan.Zero || SessionLockDelay > TimeSpan.FromMinutes(60))
            throw new ArgumentException("Session lock delay must be between 0 and 60 minutes.", nameof(SessionLockDelay));
    }
}

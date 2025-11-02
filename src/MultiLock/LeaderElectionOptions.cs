namespace MultiLock;

/// <summary>
/// Configuration options for leader election.
/// </summary>
public sealed class LeaderElectionOptions
{
    /// <summary>
    /// Gets or sets the unique identifier for this participant in the election.
    /// If not specified, a unique identifier will be generated.
    /// </summary>
    public string? ParticipantId { get; set; }

    /// <summary>
    /// Gets or sets the name of the election group.
    /// Multiple elections can run simultaneously with different group names.
    /// </summary>
    public string ElectionGroup { get; set; } = "default";

    /// <summary>
    /// Gets or sets the interval between heartbeat updates when this instance is the leader.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the timeout for considering a leader as failed.
    /// Should be significantly larger than <see cref="HeartbeatInterval"/>.
    /// Default is 90 seconds.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Gets or sets the interval between election attempts when no leader is present.
    /// Default is 10 seconds.
    /// </summary>
    public TimeSpan ElectionInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the maximum time to wait for a lock acquisition during election.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient failures.
    /// Default is 3.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay for exponential backoff retry strategy.
    /// Default is 1 second.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum delay for exponential backoff retry strategy.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets optional metadata to associate with this participant when it becomes leader.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether to automatically start the election process
    /// when the service is started. Default is true.
    /// </summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable detailed logging.
    /// Default is false.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ElectionGroup))
        {
            throw new ArgumentException("Election group cannot be null or empty.", nameof(ElectionGroup));
        }

        if (HeartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("Heartbeat interval must be positive.", nameof(HeartbeatInterval));
        }

        if (HeartbeatTimeout <= HeartbeatInterval)
        {
            throw new ArgumentException("Heartbeat timeout must be greater than heartbeat interval.", nameof(HeartbeatTimeout));
        }

        if (ElectionInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("Election interval must be positive.", nameof(ElectionInterval));
        }

        if (LockTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Lock timeout must be positive.", nameof(LockTimeout));
        }

        if (MaxRetryAttempts < 0)
        {
            throw new ArgumentException("Max retry attempts cannot be negative.", nameof(MaxRetryAttempts));
        }

        if (RetryBaseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentException("Retry base delay must be positive.", nameof(RetryBaseDelay));
        }

        if (RetryMaxDelay < RetryBaseDelay)
        {
            throw new ArgumentException("Retry max delay must be greater than or equal to base delay.", nameof(RetryMaxDelay));
        }
    }
}

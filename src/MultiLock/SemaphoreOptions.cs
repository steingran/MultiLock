namespace MultiLock;

/// <summary>
/// Configuration options for the distributed semaphore service.
/// </summary>
public sealed class SemaphoreOptions
{
    /// <summary>
    /// Gets or sets the unique identifier for this participant.
    /// If not specified, a GUID will be generated.
    /// </summary>
    public string? HolderId { get; set; }

    /// <summary>
    /// Gets or sets the name of the semaphore.
    /// This identifies the shared resource being coordinated.
    /// </summary>
    public string SemaphoreName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of concurrent holders allowed.
    /// Default is 1 (equivalent to a distributed lock).
    /// </summary>
    public int MaxCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the interval between heartbeat updates.
    /// Default is 10 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the timeout after which a slot is considered expired if no heartbeat is received.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the interval between acquisition attempts when waiting for a slot.
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan AcquisitionInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient failures.
    /// Default is 3.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay for exponential backoff retries.
    /// Default is 100 milliseconds.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets the maximum delay for exponential backoff retries.
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets optional metadata to associate with this holder.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether to automatically start acquisition attempts.
    /// Default is true.
    /// </summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable detailed logging.
    /// Default is false.
    /// </summary>
    public bool EnableDetailedLogging { get; set; }

    /// <summary>
    /// Validates the options and throws if invalid.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when options are invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SemaphoreName))
            throw new ArgumentException("Semaphore name cannot be null or empty.", nameof(SemaphoreName));

        if (MaxCount < 1)
            throw new ArgumentException("Max count must be at least 1.", nameof(MaxCount));

        if (HeartbeatInterval <= TimeSpan.Zero)
            throw new ArgumentException("Heartbeat interval must be positive.", nameof(HeartbeatInterval));

        if (HeartbeatTimeout <= TimeSpan.Zero)
            throw new ArgumentException("Heartbeat timeout must be positive.", nameof(HeartbeatTimeout));

        if (HeartbeatTimeout <= HeartbeatInterval)
            throw new ArgumentException("Heartbeat timeout must be greater than heartbeat interval.", nameof(HeartbeatTimeout));

        if (AcquisitionInterval <= TimeSpan.Zero)
            throw new ArgumentException("Acquisition interval must be positive.", nameof(AcquisitionInterval));

        if (MaxRetryAttempts < 0)
            throw new ArgumentException("Max retry attempts cannot be negative.", nameof(MaxRetryAttempts));

        if (RetryBaseDelay <= TimeSpan.Zero)
            throw new ArgumentException("Retry base delay must be positive.", nameof(RetryBaseDelay));

        if (RetryMaxDelay <= TimeSpan.Zero)
            throw new ArgumentException("Retry max delay must be positive.", nameof(RetryMaxDelay));

        if (RetryMaxDelay < RetryBaseDelay)
            throw new ArgumentException("Retry max delay must be greater than or equal to retry base delay.", nameof(RetryMaxDelay));
    }
}


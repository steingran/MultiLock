namespace MultiLock.AzureBlobStorage;

/// <summary>
/// Configuration options for the Azure Blob Storage leader election provider.
/// </summary>
public sealed class AzureBlobStorageLeaderElectionOptions
{
    /// <summary>
    /// Gets or sets the Azure Storage connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the container used for leader election.
    /// Default is "leader-election".
    /// </summary>
    public string ContainerName { get; set; } = "leader-election";

    /// <summary>
    /// Gets or inits a value indicating whether to automatically create the container if it doesn't exist.
    /// Default is true.
    /// </summary>
    public bool AutoCreateContainer { get; init; } = true;

    /// <summary>
    /// Gets or inits the lease duration for blob leases.
    /// Must be between 15 and 60 seconds, or -1 for infinite.
    /// Default is 60 seconds.
    /// </summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(ConnectionString));

        if (string.IsNullOrWhiteSpace(ContainerName))
            throw new ArgumentException("Container name cannot be null or empty.", nameof(ContainerName));

        if (LeaseDuration != TimeSpan.FromSeconds(-1) &&
            (LeaseDuration < TimeSpan.FromSeconds(15) || LeaseDuration > TimeSpan.FromSeconds(60)))
            throw new ArgumentException("Lease duration must be between 15 and 60 seconds, or -1 for infinite.", nameof(LeaseDuration));
    }
}

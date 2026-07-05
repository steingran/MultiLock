using System.Text.RegularExpressions;

namespace MultiLock.AzureBlobStorage;

/// <summary>
/// Configuration options for the Azure Blob Storage semaphore provider.
/// </summary>
public sealed partial class AzureBlobStorageSemaphoreOptions
{
    /// <summary>
    /// Gets or sets the Azure Storage connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the container used for semaphores.
    /// Default is "semaphores".
    /// </summary>
    public string ContainerName { get; set; } = "semaphores";

    /// <summary>
    /// Gets or sets a value indicating whether to automatically create the container if it doesn't exist.
    /// Default is true.
    /// </summary>
    public bool AutoCreateContainer { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for optimistic concurrency conflicts.
    /// Default is 5.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 5;

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

        // Validate the container name against Azure's rules up front for a clearer error than the
        // SDK's runtime failure: 3-63 chars, lowercase letters/digits/hyphens, must start and end
        // with a letter or digit, and no consecutive hyphens.
        if (ContainerName.Length is < 3 or > 63 || !ContainerNameRegex().IsMatch(ContainerName))
            throw new ArgumentException(
                "Container name must be 3-63 characters, contain only lowercase letters, digits, and hyphens, " +
                "start and end with a letter or digit, and not contain consecutive hyphens.",
                nameof(ContainerName));

        if (MaxRetryAttempts < 1)
            throw new ArgumentException("Max retry attempts must be at least 1.", nameof(MaxRetryAttempts));
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled)]
    private static partial Regex ContainerNameRegex();
}


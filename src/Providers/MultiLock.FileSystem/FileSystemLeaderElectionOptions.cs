namespace MultiLock.FileSystem;

/// <summary>
/// Configuration options for the File System leader election provider.
/// </summary>
public sealed class FileSystemLeaderElectionOptions
{
    /// <summary>
    /// Gets or sets the directory path where leader election files will be stored.
    /// Default is a "LeaderElection" subdirectory in the system temp directory.
    /// </summary>
    public string DirectoryPath { get; set; } = Path.Combine(Path.GetTempPath(), "LeaderElection");

    /// <summary>
    /// Gets or sets the file extension for leader election files.
    /// Default is ".leader".
    /// </summary>
    public string FileExtension { get; set; } = ".leader";

    /// <summary>
    /// Gets or sets a value indicating whether to automatically create the directory if it doesn't exist.
    /// Default is true.
    /// </summary>
    public bool AutoCreateDirectory { get; set; } = true;

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
            throw new ArgumentException("Directory path cannot be null or empty.", nameof(DirectoryPath));

        if (string.IsNullOrWhiteSpace(FileExtension))
            throw new ArgumentException("File extension cannot be null or empty.", nameof(FileExtension));
    }
}

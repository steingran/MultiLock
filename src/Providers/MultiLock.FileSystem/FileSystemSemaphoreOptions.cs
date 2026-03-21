namespace MultiLock.FileSystem;

/// <summary>
/// Configuration options for the File System semaphore provider.
/// </summary>
public sealed class FileSystemSemaphoreOptions
{
    /// <summary>
    /// Gets or sets the directory path where semaphore files will be stored.
    /// Default is a "Semaphores" subdirectory in the system temp directory.
    /// </summary>
    public string DirectoryPath { get; set; } = Path.Combine(Path.GetTempPath(), "Semaphores");

    /// <summary>
    /// Gets or sets the file extension for semaphore holder files.
    /// Default is ".holder".
    /// </summary>
    public string FileExtension { get; set; } = ".holder";

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

        if (!FileExtension.StartsWith('.') || FileExtension.Length < 2)
            throw new ArgumentException("File extension must start with '.' and have at least one character after it (e.g. '.holder').", nameof(FileExtension));
    }
}


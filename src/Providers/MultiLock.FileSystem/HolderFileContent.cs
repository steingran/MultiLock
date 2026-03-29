namespace MultiLock.FileSystem;

/// <summary>
/// Represents the content of a semaphore holder file.
/// </summary>
internal sealed class HolderFileContent
{
    /// <summary>
    /// Gets or sets the holder identifier.
    /// </summary>
    public string HolderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the slot was acquired.
    /// </summary>
    public DateTimeOffset AcquiredAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last heartbeat.
    /// </summary>
    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// Gets or sets the metadata associated with this holder.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}


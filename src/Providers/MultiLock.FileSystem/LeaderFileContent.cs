namespace MultiLock.FileSystem;

/// <summary>
/// Internal class to represent the content of a leader file.
/// </summary>
internal sealed class LeaderFileContent
{
    public string LeaderId { get; init; } = string.Empty;
    public DateTimeOffset LeadershipAcquiredAt { get; init; }
    public DateTimeOffset LastHeartbeat { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

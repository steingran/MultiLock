namespace MultiLock.InMemory;

/// <summary>
/// Internal record to store leader information.
/// </summary>
internal sealed record LeaderRecord(
    string LeaderId,
    DateTimeOffset LeadershipAcquiredAt,
    DateTimeOffset LastHeartbeat,
    IReadOnlyDictionary<string, string> Metadata);

namespace MultiLock.InMemory;

/// <summary>
/// Internal record to store semaphore slot holder information.
/// </summary>
internal sealed record SemaphoreSlotRecord(
    string HolderId,
    DateTimeOffset AcquiredAt,
    DateTimeOffset LastHeartbeat,
    IReadOnlyDictionary<string, string> Metadata);


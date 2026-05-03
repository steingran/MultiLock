namespace MultiLock;

/// <summary>
/// Represents an acquired semaphore slot that can be disposed to release the slot.
/// </summary>
public sealed class SemaphoreAcquisition : IAsyncDisposable, IDisposable
{
    private readonly ISemaphoreProvider provider;
    private readonly string semaphoreName;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreAcquisition"/> class.
    /// </summary>
    /// <param name="provider">The semaphore provider.</param>
    /// <param name="semaphoreName">The name of the semaphore.</param>
    /// <param name="holderId">The holder ID.</param>
    /// <param name="acquiredAt">The time the slot was acquired.</param>
    /// <param name="metadata">The metadata associated with the holder.</param>
    public SemaphoreAcquisition(
        ISemaphoreProvider provider,
        string semaphoreName,
        string holderId,
        DateTimeOffset acquiredAt,
        IReadOnlyDictionary<string, string> metadata)
    {
        this.provider = provider;
        this.semaphoreName = semaphoreName;
        HolderId = holderId;
        AcquiredAt = acquiredAt;
        Metadata = metadata;
    }

    /// <summary>
    /// Gets the unique identifier of the holder.
    /// </summary>
    public string HolderId { get; }

    /// <summary>
    /// Gets the time when the slot was acquired.
    /// </summary>
    public DateTimeOffset AcquiredAt { get; }

    /// <summary>
    /// Gets the metadata associated with the holder.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Gets a value indicating whether the acquisition has been disposed.
    /// </summary>
    public bool IsDisposed => isDisposed;

    /// <summary>
    /// Releases the semaphore slot asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        try
        {
            await provider.ReleaseAsync(semaphoreName, HolderId);
        }
        catch
        {
            // Swallow exceptions during disposal to prevent masking other exceptions
        }
    }

    /// <summary>
    /// Releases the semaphore slot synchronously.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="DisposeAsync"/> in async contexts. This synchronous path blocks the
    /// calling thread and should not be used when a synchronization context is active.
    /// </remarks>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}


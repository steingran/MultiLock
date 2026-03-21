using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;

namespace MultiLock.FileSystem;

/// <summary>
/// File System implementation of the semaphore provider.
/// Uses files in a directory to track semaphore holders.
/// </summary>
public sealed class FileSystemSemaphoreProvider : ISemaphoreProvider
{
    private readonly FileSystemSemaphoreOptions options;
    private readonly ILogger<FileSystemSemaphoreProvider> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly object fileLock = new();
    private volatile bool isInitialized;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemSemaphoreProvider"/> class.
    /// </summary>
    /// <param name="options">The file system options.</param>
    /// <param name="logger">The logger.</param>
    public FileSystemSemaphoreProvider(
        IOptions<FileSystemSemaphoreOptions> options,
        ILogger<FileSystemSemaphoreProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(
        string semaphoreName,
        string holderId,
        int maxCount,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan slotTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);
        ParameterValidation.ValidateMaxCount(maxCount);
        ParameterValidation.ValidateMetadata(metadata);
        ParameterValidation.ValidateSlotTimeout(slotTimeout);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string semaphoreDir = GetSemaphoreDirectory(semaphoreName);
            EnsureDirectoryExists(semaphoreDir);

            // Use a per-semaphore OS lock file so that the count-then-create operation is
            // atomic across processes (not just across threads). lock(fileLock) alone is a
            // C# monitor and has no cross-process effect. The lock file is held open with
            // FileShare.None for the duration of the critical section; closing/disposing the
            // FileStream releases the OS lock automatically.
            string lockFilePath = Path.Combine(semaphoreDir, ".lock");
            await using FileStream lockFile = await AcquireLockFileAsync(lockFilePath, cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset expiryTime = now.Subtract(slotTimeout);

            // Clean up expired holders
            CleanupExpiredHolders(semaphoreDir, expiryTime);

            // Check if holder already exists (renewal)
            string holderFilePath = GetHolderFilePath(semaphoreDir, holderId);
            if (File.Exists(holderFilePath))
            {
                HolderFileContent? existingHolder = ReadHolderFile(holderFilePath);
                if (existingHolder != null)
                {
                    existingHolder.LastHeartbeat = now;
                    existingHolder.Metadata = new Dictionary<string, string>(metadata);
                    WriteHolderFile(holderFilePath, existingHolder);
                    logger.LogInformation("Renewed slot for holder {HolderId} in semaphore {SemaphoreName}",
                        holderId, semaphoreName);
                    return true;
                }
            }

            // Count only valid, non-expired holders; corrupt or unreadable files must not
            // consume capacity since CleanupExpiredHolders leaves them in place.
            int currentCount = CountActiveHolders(semaphoreDir, expiryTime);
            if (currentCount >= maxCount)
            {
                logger.LogDebug("Failed to acquire slot for holder {HolderId} in semaphore {SemaphoreName} - full ({CurrentCount}/{MaxCount})",
                    holderId, semaphoreName, currentCount, maxCount);
                return false;
            }

            // Create new holder file
            var holderContent = new HolderFileContent
            {
                HolderId = holderId,
                AcquiredAt = now,
                LastHeartbeat = now,
                Metadata = new Dictionary<string, string>(metadata)
            };

            WriteHolderFile(holderFilePath, holderContent);
            logger.LogInformation("Acquired slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error acquiring slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to acquire semaphore slot", ex);
        }
    }

    // Polls until the per-semaphore OS lock file can be opened exclusively (FileShare.None).
    // This provides cross-process mutual exclusion for the count-and-acquire critical section.
    private static async Task<FileStream> AcquireLockFileAsync(string lockFilePath, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string semaphoreDir = GetSemaphoreDirectory(semaphoreName);

            // If the directory doesn't exist there is nothing to release.
            if (!Directory.Exists(semaphoreDir))
            {
                logger.LogDebug("Holder {HolderId} not found in semaphore {SemaphoreName}", holderId, semaphoreName);
                return;
            }

            // Use the same per-semaphore OS lock file as TryAcquireAsync to prevent
            // cross-process races between concurrent Release / Heartbeat / Acquire calls.
            string lockFilePath = Path.Combine(semaphoreDir, ".lock");
            await using FileStream lockFile = await AcquireLockFileAsync(lockFilePath, cancellationToken);

            string holderFilePath = GetHolderFilePath(semaphoreDir, holderId);
            if (File.Exists(holderFilePath))
            {
                File.Delete(holderFilePath);
                logger.LogInformation("Released slot for holder {HolderId} in semaphore {SemaphoreName}",
                    holderId, semaphoreName);
            }
            else
            {
                logger.LogDebug("Holder {HolderId} not found in semaphore {SemaphoreName}", holderId, semaphoreName);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error releasing slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to release semaphore slot", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateHeartbeatAsync(
        string semaphoreName,
        string holderId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);
        ParameterValidation.ValidateMetadata(metadata);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            string semaphoreDir = GetSemaphoreDirectory(semaphoreName);

            if (!Directory.Exists(semaphoreDir))
            {
                logger.LogWarning("Heartbeat update failed for holder {HolderId} in semaphore {SemaphoreName} - not a holder",
                    holderId, semaphoreName);
                return false;
            }

            // Use the same per-semaphore OS lock file as TryAcquireAsync to prevent
            // cross-process races between concurrent Heartbeat / Release / Acquire calls.
            string lockFilePath = Path.Combine(semaphoreDir, ".lock");
            await using FileStream lockFile = await AcquireLockFileAsync(lockFilePath, cancellationToken);

            string holderFilePath = GetHolderFilePath(semaphoreDir, holderId);
            if (!File.Exists(holderFilePath))
            {
                logger.LogWarning("Heartbeat update failed for holder {HolderId} in semaphore {SemaphoreName} - not a holder",
                    holderId, semaphoreName);
                return false;
            }

            HolderFileContent? holder = ReadHolderFile(holderFilePath);
            if (holder == null)
                return false;

            holder.LastHeartbeat = DateTimeOffset.UtcNow;
            holder.Metadata = new Dictionary<string, string>(metadata);
            WriteHolderFile(holderFilePath, holder);

            logger.LogDebug("Updated heartbeat for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating heartbeat for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to update heartbeat", ex);
        }
    }

    /// <inheritdoc />
    public async Task<int> GetCurrentCountAsync(
        string semaphoreName,
        TimeSpan slotTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            lock (fileLock)
            {
                string semaphoreDir = GetSemaphoreDirectory(semaphoreName);
                if (!Directory.Exists(semaphoreDir))
                    return 0;

                DateTimeOffset expiryTime = DateTimeOffset.UtcNow.Subtract(slotTimeout);
                return CountActiveHolders(semaphoreDir, expiryTime);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting current count for semaphore {SemaphoreName}", semaphoreName);
            throw new SemaphoreProviderException("Failed to get current count", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemaphoreHolder>> GetHoldersAsync(
        string semaphoreName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            lock (fileLock)
            {
                string semaphoreDir = GetSemaphoreDirectory(semaphoreName);
                if (!Directory.Exists(semaphoreDir))
                    return [];

                var holders = new List<SemaphoreHolder>();
                foreach (string filePath in Directory.GetFiles(semaphoreDir, $"*{options.FileExtension}"))
                {
                    HolderFileContent? holder = ReadHolderFile(filePath);
                    if (holder != null)
                        holders.Add(new SemaphoreHolder(holder.HolderId, holder.AcquiredAt, holder.LastHeartbeat, holder.Metadata));
                }
                return holders;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting holders for semaphore {SemaphoreName}", semaphoreName);
            throw new SemaphoreProviderException("Failed to get holders", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHoldingAsync(
        string semaphoreName,
        string holderId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ParameterValidation.ValidateSemaphoreName(semaphoreName);
        ParameterValidation.ValidateHolderId(holderId);
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            lock (fileLock)
            {
                string semaphoreDir = GetSemaphoreDirectory(semaphoreName);
                string holderFilePath = GetHolderFilePath(semaphoreDir, holderId);
                return File.Exists(holderFilePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if holder {HolderId} is holding semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to check holding status", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
            return false;

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            return Directory.Exists(options.DirectoryPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for File System semaphore provider");
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        initializationLock.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (!isDisposed) return;
        throw new ObjectDisposedException(nameof(FileSystemSemaphoreProvider));
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (isInitialized)
            return;

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (isInitialized)
                return;

            if (options.AutoCreateDirectory && !Directory.Exists(options.DirectoryPath))
            {
                Directory.CreateDirectory(options.DirectoryPath);
                logger.LogInformation("Created semaphore directory: {DirectoryPath}", options.DirectoryPath);
            }

            isInitialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private string GetSemaphoreDirectory(string semaphoreName)
    {
        // Normalize the base path so Path.GetFullPath comparisons are correct.
        string basePath = Path.GetFullPath(options.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        string candidatePath = Path.GetFullPath(Path.Combine(options.DirectoryPath, semaphoreName));

        // Reject paths that resolve outside the configured base directory (e.g. "..").
        if (!candidatePath.StartsWith(basePath, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Semaphore name '{semaphoreName}' resolves to a path outside the configured directory.",
                nameof(semaphoreName));

        return candidatePath;
    }

    private string GetHolderFilePath(string semaphoreDir, string holderId)
    {
        string normalizedDir = semaphoreDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidatePath = Path.GetFullPath(Path.Combine(semaphoreDir, $"{holderId}{options.FileExtension}"));

        if (!candidatePath.StartsWith(normalizedDir, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Holder ID '{holderId}' resolves to a path outside the semaphore directory.",
                nameof(holderId));

        return candidatePath;
    }

    private static void EnsureDirectoryExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            Directory.CreateDirectory(directoryPath);
    }

    private void CleanupExpiredHolders(string semaphoreDir, DateTimeOffset expiryTime)
    {
        if (!Directory.Exists(semaphoreDir))
            return;

        foreach (string filePath in Directory.GetFiles(semaphoreDir, $"*{options.FileExtension}"))
        {
            try
            {
                HolderFileContent? holder = ReadHolderFile(filePath);
                if (holder != null && holder.LastHeartbeat < expiryTime)
                {
                    File.Delete(filePath);
                    logger.LogDebug("Cleaned up expired holder {HolderId}", holder.HolderId);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error cleaning up holder file {FilePath}", filePath);
            }
        }
    }

    private int CountHolders(string semaphoreDir)
    {
        if (!Directory.Exists(semaphoreDir))
            return 0;
        return Directory.GetFiles(semaphoreDir, $"*{options.FileExtension}").Length;
    }

    private int CountActiveHolders(string semaphoreDir, DateTimeOffset expiryTime)
    {
        if (!Directory.Exists(semaphoreDir))
            return 0;

        return Directory.GetFiles(semaphoreDir, $"*{options.FileExtension}")
            .Select(ReadHolderFile)
            .Count(holder => holder != null && holder.LastHeartbeat >= expiryTime);
    }

    private static HolderFileContent? ReadHolderFile(string filePath)
    {
        try
        {
            string content = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<HolderFileContent>(content);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteHolderFile(string filePath, HolderFileContent content)
    {
        string json = JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }
}

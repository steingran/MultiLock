using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;

namespace MultiLock.FileSystem;

/// <summary>
/// File System implementation of the leader election provider.
/// Uses file locking to coordinate leadership across processes on the same machine.
/// </summary>
public sealed class FileSystemLeaderElectionProvider : ILeaderElectionProvider
{
    private readonly FileSystemLeaderElectionOptions options;
    private readonly ILogger<FileSystemLeaderElectionProvider> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool isInitialized;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemLeaderElectionProvider"/> class.
    /// </summary>
    /// <param name="options">The file system options.</param>
    /// <param name="logger">The logger.</param>
    public FileSystemLeaderElectionProvider(
        IOptions<FileSystemLeaderElectionOptions> options,
        ILogger<FileSystemLeaderElectionProvider> logger)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        this.options.Validate();
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireLeadershipAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken);

        string filePath = GetLeaderFilePath(electionGroup);
        EnsureDirectoryExists();

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset expiryTime = now.Subtract(lockTimeout);

            // Check if there's an existing leader file
            if (File.Exists(filePath))
            {
                try
                {
                    string existingContent = await ReadLeaderFileAsync(filePath, cancellationToken);
                    LeaderFileContent? existingLeader = JsonSerializer.Deserialize<LeaderFileContent>(existingContent);

                    if (existingLeader != null &&
                        existingLeader.LastHeartbeat >= expiryTime &&
                        existingLeader.LeaderId != participantId)
                    {
                        logger.LogDebug("Leadership acquisition failed for participant {ParticipantId} in group {ElectionGroup}. Current leader: {CurrentLeader}",
                            participantId, electionGroup, existingLeader.LeaderId);
                        return false;
                    }
                }
                catch (IOException ex)
                {
                    logger.LogDebug(ex, "Could not read leader file for group {ElectionGroup} - file may be locked by current leader", electionGroup);
                    // File is locked by another process (current leader), so we cannot acquire leadership
                    return false;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error reading existing leader file for group {ElectionGroup}", electionGroup);
                    // Continue with acquisition attempt for other errors
                }
            }

            // Try to acquire leadership by creating/updating the file
            var leaderContent = new LeaderFileContent
            {
                LeaderId = participantId,
                LeadershipAcquiredAt = now,
                LastHeartbeat = now,
                Metadata = new Dictionary<string, string>(metadata)
            };

            string json = JsonSerializer.Serialize(leaderContent, new JsonSerializerOptions { WriteIndented = true });

            try
            {
                // Use FileShare.Read to allow other processes to read while we hold the lock
                // This prevents read errors while still maintaining exclusive write access
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                await using var writer = new StreamWriter(fileStream);
                await writer.WriteAsync(json);
                await writer.FlushAsync(cancellationToken);

                logger.LogInformation("Successfully acquired leadership for participant {ParticipantId} in group {ElectionGroup}",
                    participantId, electionGroup);

                return true;
            }
            catch (IOException ex)
            {
                // File is locked by another process (current leader), so we cannot acquire leadership
                logger.LogDebug(ex, "Could not acquire leadership for participant {ParticipantId} in group {ElectionGroup} - file is locked by current leader",
                    participantId, electionGroup);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error acquiring leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to acquire leadership", ex);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseLeadershipAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken);

        string filePath = GetLeaderFilePath(electionGroup);

        try
        {
            if (File.Exists(filePath))
            {
                // Verify we are the current leader before deleting
                string content = await ReadLeaderFileAsync(filePath, cancellationToken);
                LeaderFileContent? leader = JsonSerializer.Deserialize<LeaderFileContent>(content);

                if (leader?.LeaderId == participantId)
                {
                    try
                    {
                        File.Delete(filePath);
                        logger.LogInformation("Released leadership for participant {ParticipantId} in group {ElectionGroup}",
                            participantId, electionGroup);
                    }
                    catch (IOException ex)
                    {
                        // File is locked by another process - this can happen during concurrent operations
                        // Log as debug since this is not necessarily an error condition
                        logger.LogDebug(ex, "Could not delete leader file for participant {ParticipantId} in group {ElectionGroup} - file is locked",
                            participantId, electionGroup);
                    }
                }
                else
                {
                    logger.LogWarning("Cannot release leadership for participant {ParticipantId} in group {ElectionGroup} - not the current leader",
                        participantId, electionGroup);
                }
            }
        }
        catch (IOException ex)
        {
            // File is locked - log as debug since this is expected during concurrent operations
            logger.LogDebug(ex, "Could not read leader file for participant {ParticipantId} in group {ElectionGroup} - file is locked",
                participantId, electionGroup);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error releasing leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to release leadership", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateHeartbeatAsync(
        string electionGroup,
        string participantId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken);

        string filePath = GetLeaderFilePath(electionGroup);
        EnsureDirectoryExists();

        try
        {
            if (!File.Exists(filePath))
                return false;

            string content = await ReadLeaderFileAsync(filePath, cancellationToken);
            LeaderFileContent? leader = JsonSerializer.Deserialize<LeaderFileContent>(content);

            if (leader?.LeaderId != participantId)
                return false;

            // Update heartbeat
            leader.LastHeartbeat = DateTimeOffset.UtcNow;
            leader.Metadata = new Dictionary<string, string>(metadata);

            string json = JsonSerializer.Serialize(leader, new JsonSerializerOptions { WriteIndented = true });

            // Use FileShare.Read to allow other processes to read while we hold the lock
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(fileStream);
            await writer.WriteAsync(json);
            await writer.FlushAsync(cancellationToken);

            logger.LogDebug("Updated heartbeat for leader {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating heartbeat for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to update heartbeat", ex);
        }
    }

    /// <inheritdoc />
    public async Task<LeaderInfo?> GetCurrentLeaderAsync(
        string electionGroup,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken);

        string filePath = GetLeaderFilePath(electionGroup);

        try
        {
            if (!File.Exists(filePath))
                return null;

            string content = await ReadLeaderFileAsync(filePath, cancellationToken);
            LeaderFileContent? leader = JsonSerializer.Deserialize<LeaderFileContent>(content);

            if (leader == null)
                return null;

            return new LeaderInfo(
                leader.LeaderId,
                leader.LeadershipAcquiredAt,
                leader.LastHeartbeat,
                leader.Metadata);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting current leader for group {ElectionGroup}", electionGroup);
            throw new LeaderElectionProviderException("Failed to get current leader", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsLeaderAsync(
        string electionGroup,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken);

        string filePath = GetLeaderFilePath(electionGroup);

        try
        {
            if (!File.Exists(filePath))
                return false;

            string content = await File.ReadAllTextAsync(filePath, cancellationToken);
            LeaderFileContent? leader = JsonSerializer.Deserialize<LeaderFileContent>(content);

            return leader?.LeaderId == participantId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
            throw new LeaderElectionProviderException("Failed to check leadership", ex);
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
            logger.LogWarning(ex, "Health check failed for File System provider");
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
        throw new ObjectDisposedException(nameof(FileSystemLeaderElectionProvider));
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
                logger.LogInformation("Created leader election directory: {DirectoryPath}", options.DirectoryPath);
            }

            isInitialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private string GetLeaderFilePath(string electionGroup)
    {
        string fileName = $"{electionGroup}{options.FileExtension}";
        return Path.Combine(options.DirectoryPath, fileName);
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(options.DirectoryPath))
        {
            Directory.CreateDirectory(options.DirectoryPath);
            logger.LogDebug("Created leader election directory: {DirectoryPath}", options.DirectoryPath);
        }
    }

    /// <summary>
    /// Reads the leader file content with FileShare.ReadWrite to allow reading even when the file is locked for writing.
    /// </summary>
    private static async Task<string> ReadLeaderFileAsync(string filePath, CancellationToken cancellationToken)
    {
        // Use FileShare.ReadWrite to allow reading even when another process has the file open for writing
        // This is necessary on Linux where file locking is more strict
        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new StreamReader(fileStream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}

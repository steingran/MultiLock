using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;

namespace MultiLock.AzureBlobStorage;

/// <summary>
/// Azure Blob Storage implementation of the leader election provider.
/// Uses blob leases to coordinate leadership across distributed instances.
/// </summary>
public sealed class AzureBlobStorageLeaderElectionProvider : ILeaderElectionProvider
{
    private readonly AzureBlobStorageLeaderElectionOptions options;
    private readonly ILogger<AzureBlobStorageLeaderElectionProvider> logger;
    private readonly BlobServiceClient blobServiceClient;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly Dictionary<string, string> activeLeases = new();
    private readonly object leaseLock = new();
    private volatile bool isInitialized;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobStorageLeaderElectionProvider"/> class.
    /// </summary>
    /// <param name="options">The Azure Blob Storage options.</param>
    /// <param name="logger">The logger.</param>
    public AzureBlobStorageLeaderElectionProvider(
        IOptions<AzureBlobStorageLeaderElectionOptions> options,
        ILogger<AzureBlobStorageLeaderElectionProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();

        blobServiceClient = new BlobServiceClient(this.options.ConnectionString);
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            BlobClient blobClient = await GetBlobClientAsync(electionGroup, cancellationToken);
            BlobLeaseClient? leaseClient = blobClient.GetBlobLeaseClient();

            // Try to acquire lease
            Response<BlobLease>? leaseResponse = await leaseClient.AcquireAsync(options.LeaseDuration, cancellationToken: cancellationToken);
            string? leaseId = leaseResponse.Value.LeaseId;

            // Update blob content with leader information
            var leaderData = new LeaderData
            {
                LeaderId = participantId,
                LeadershipAcquiredAt = DateTimeOffset.UtcNow,
                LastHeartbeat = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(metadata)
            };

            string json = JsonSerializer.Serialize(leaderData);
            byte[] content = Encoding.UTF8.GetBytes(json);

            var conditions = new BlobRequestConditions { LeaseId = leaseId };
            await blobClient.UploadAsync(
                new BinaryData(content),
                new BlobUploadOptions { Conditions = conditions },
                cancellationToken);

            // Store lease ID for later use
            lock (leaseLock)
            {
                activeLeases[electionGroup] = leaseId;
            }

            logger.LogInformation("Successfully acquired leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);

            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
        {
            // Lease already exists or precondition failed
            logger.LogDebug("Failed to acquire leadership for participant {ParticipantId} in group {ElectionGroup}: {Error}",
                participantId, electionGroup, ex.Message);
            return false;
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            string? leaseId;
            lock (leaseLock)
            {
                if (!activeLeases.TryGetValue(electionGroup, out leaseId))
                {
                    logger.LogWarning("No active lease found for participant {ParticipantId} in group {ElectionGroup}",
                        participantId, electionGroup);
                    return;
                }
                activeLeases.Remove(electionGroup);
            }

            BlobClient blobClient = await GetBlobClientAsync(electionGroup, cancellationToken);
            BlobLeaseClient? leaseClient = blobClient.GetBlobLeaseClient(leaseId);

            await leaseClient.ReleaseAsync(cancellationToken: cancellationToken);

            logger.LogInformation("Released leadership for participant {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Lease already released or expired
            logger.LogDebug("Lease already released for participant {ParticipantId} in group {ElectionGroup}",
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            string? leaseId;
            lock (leaseLock)
            {
                if (!activeLeases.TryGetValue(electionGroup, out leaseId))
                {
                    return false;
                }
            }

            BlobClient blobClient = await GetBlobClientAsync(electionGroup, cancellationToken);
            BlobLeaseClient? leaseClient = blobClient.GetBlobLeaseClient(leaseId);

            // Renew the lease
            await leaseClient.RenewAsync(cancellationToken: cancellationToken);

            // Update blob content with new heartbeat
            Response<BlobDownloadResult>? existingContent = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            LeaderData? existingData = JsonSerializer.Deserialize<LeaderData>(existingContent.Value.Content.ToString());

            if (existingData?.LeaderId != participantId)
                return false;

            existingData.LastHeartbeat = DateTimeOffset.UtcNow;
            existingData.Metadata = new Dictionary<string, string>(metadata);

            string json = JsonSerializer.Serialize(existingData);
            byte[] content = Encoding.UTF8.GetBytes(json);

            var conditions = new BlobRequestConditions { LeaseId = leaseId };
            await blobClient.UploadAsync(
                new BinaryData(content),
                new BlobUploadOptions { Conditions = conditions },
                cancellationToken);

            logger.LogDebug("Updated heartbeat for leader {ParticipantId} in group {ElectionGroup}",
                participantId, electionGroup);

            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
        {
            // Lease expired or lost
            lock (leaseLock)
            {
                activeLeases.Remove(electionGroup);
            }
            return false;
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            BlobClient blobClient = await GetBlobClientAsync(electionGroup, cancellationToken);

            if (!await blobClient.ExistsAsync(cancellationToken))
                return null;

            Response<BlobDownloadResult>? response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            LeaderData? leaderData = JsonSerializer.Deserialize<LeaderData>(response.Value.Content.ToString());

            if (leaderData == null)
                return null;

            return new LeaderInfo(
                leaderData.LeaderId,
                leaderData.LeadershipAcquiredAt,
                leaderData.LastHeartbeat,
                leaderData.Metadata);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
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
        LeaderInfo? leader = await GetCurrentLeaderAsync(electionGroup, cancellationToken);
        return leader?.LeaderId == participantId;
    }

    /// <inheritdoc />
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (isDisposed)
            return false;

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            BlobContainerClient? containerClient = blobServiceClient.GetBlobContainerClient(options.ContainerName);
            await containerClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for Azure Blob Storage provider");
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
        throw new ObjectDisposedException(nameof(AzureBlobStorageLeaderElectionProvider));
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

            if (options.AutoCreateContainer)
            {
                BlobContainerClient? containerClient = blobServiceClient.GetBlobContainerClient(options.ContainerName);
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                logger.LogInformation("Container {ContainerName} created or verified", options.ContainerName);
            }

            isInitialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task<BlobClient> GetBlobClientAsync(string electionGroup, CancellationToken cancellationToken)
    {
        BlobContainerClient? containerClient = blobServiceClient.GetBlobContainerClient(options.ContainerName);
        string blobName = $"{electionGroup}.json";
        BlobClient? blobClient = containerClient.GetBlobClient(blobName);

        // Ensure blob exists with empty content if it doesn't exist
        if (await blobClient.ExistsAsync(cancellationToken)) return blobClient;

        try
        {
            byte[] emptyContent = "{}"u8.ToArray();
            await blobClient.UploadAsync(new BinaryData(emptyContent), cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Blob was created by another instance, ignore
        }

        return blobClient;
    }

    /// <summary>
    /// Internal class to represent leader data stored in blob.
    /// </summary>
    private sealed class LeaderData
    {
        public string LeaderId { get; init; } = string.Empty;
        public DateTimeOffset LeadershipAcquiredAt { get; init; }
        public DateTimeOffset LastHeartbeat { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}

using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.Exceptions;

namespace MultiLock.AzureBlobStorage;

/// <summary>
/// Azure Blob Storage implementation of the semaphore provider.
/// Uses a blob to store holder information with optimistic concurrency (ETags) for atomic updates.
/// </summary>
public sealed class AzureBlobStorageSemaphoreProvider : ISemaphoreProvider
{
    private readonly AzureBlobStorageSemaphoreOptions options;
    private readonly ILogger<AzureBlobStorageSemaphoreProvider> logger;
    private readonly BlobServiceClient blobServiceClient;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool isInitialized;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobStorageSemaphoreProvider"/> class.
    /// </summary>
    /// <param name="options">The Azure Blob Storage options.</param>
    /// <param name="logger">The logger.</param>
    public AzureBlobStorageSemaphoreProvider(
        IOptions<AzureBlobStorageSemaphoreOptions> options,
        ILogger<AzureBlobStorageSemaphoreProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        this.options.Validate();

        blobServiceClient = new BlobServiceClient(this.options.ConnectionString);
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);

            for (int attempt = 0; attempt < options.MaxRetryAttempts; attempt++)
            {
                BlobClient blobClient = await GetBlobClientAsync(semaphoreName, cancellationToken);
                (SemaphoreData data, ETag etag) = await ReadSemaphoreDataAsync(blobClient, cancellationToken);

                DateTimeOffset now = DateTimeOffset.UtcNow;
                DateTimeOffset expiryTime = now.Subtract(slotTimeout);

                // Remove expired holders
                data.Holders.RemoveAll(h => h.LastHeartbeat < expiryTime);

                // Check if holder already exists (renewal)
                HolderData? existingHolder = data.Holders.Find(h => h.HolderId == holderId);
                if (existingHolder != null)
                {
                    existingHolder.LastHeartbeat = now;
                    existingHolder.Metadata = new Dictionary<string, string>(metadata);

                    if (await TryWriteSemaphoreDataAsync(blobClient, data, etag, cancellationToken))
                    {
                        logger.LogInformation("Renewed slot for holder {HolderId} in semaphore {SemaphoreName}",
                            holderId, semaphoreName);
                        return true;
                    }
                    continue; // Retry on conflict
                }

                // Check if there's room
                if (data.Holders.Count >= maxCount)
                {
                    logger.LogDebug("Failed to acquire slot for holder {HolderId} in semaphore {SemaphoreName} - full ({CurrentCount}/{MaxCount})",
                        holderId, semaphoreName, data.Holders.Count, maxCount);
                    return false;
                }

                // Add new holder
                data.Holders.Add(new HolderData
                {
                    HolderId = holderId,
                    AcquiredAt = now,
                    LastHeartbeat = now,
                    Metadata = new Dictionary<string, string>(metadata)
                });

                if (await TryWriteSemaphoreDataAsync(blobClient, data, etag, cancellationToken))
                {
                    logger.LogInformation("Acquired slot for holder {HolderId} in semaphore {SemaphoreName}",
                        holderId, semaphoreName);
                    return true;
                }
                // Retry on conflict
            }

            logger.LogWarning("Failed to acquire slot for holder {HolderId} in semaphore {SemaphoreName} after {MaxRetries} attempts",
                holderId, semaphoreName, options.MaxRetryAttempts);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error acquiring slot for holder {HolderId} in semaphore {SemaphoreName}",
                holderId, semaphoreName);
            throw new SemaphoreProviderException("Failed to acquire semaphore slot", ex);
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);

            for (int attempt = 0; attempt < options.MaxRetryAttempts; attempt++)
            {
                BlobClient blobClient = await GetBlobClientAsync(semaphoreName, cancellationToken);
                (SemaphoreData data, ETag etag) = await ReadSemaphoreDataAsync(blobClient, cancellationToken);

                int removed = data.Holders.RemoveAll(h => h.HolderId == holderId);
                if (removed == 0)
                {
                    logger.LogDebug("Holder {HolderId} not found in semaphore {SemaphoreName}", holderId, semaphoreName);
                    return;
                }

                if (await TryWriteSemaphoreDataAsync(blobClient, data, etag, cancellationToken))
                {
                    logger.LogInformation("Released slot for holder {HolderId} in semaphore {SemaphoreName}",
                        holderId, semaphoreName);
                    return;
                }
            }

            logger.LogWarning("Failed to release slot for holder {HolderId} in semaphore {SemaphoreName} after {MaxRetries} attempts due to repeated ETag conflicts",
                holderId, semaphoreName, options.MaxRetryAttempts);
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);

            for (int attempt = 0; attempt < options.MaxRetryAttempts; attempt++)
            {
                BlobClient blobClient = await GetBlobClientAsync(semaphoreName, cancellationToken);
                (SemaphoreData data, ETag etag) = await ReadSemaphoreDataAsync(blobClient, cancellationToken);

                HolderData? holder = data.Holders.Find(h => h.HolderId == holderId);
                if (holder == null)
                {
                    logger.LogWarning("Heartbeat update failed for holder {HolderId} in semaphore {SemaphoreName} - not a holder",
                        holderId, semaphoreName);
                    return false;
                }

                holder.LastHeartbeat = DateTimeOffset.UtcNow;
                holder.Metadata = new Dictionary<string, string>(metadata);

                if (await TryWriteSemaphoreDataAsync(blobClient, data, etag, cancellationToken))
                {
                    logger.LogDebug("Updated heartbeat for holder {HolderId} in semaphore {SemaphoreName}",
                        holderId, semaphoreName);
                    return true;
                }
            }

            return false;
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            BlobClient blobClient = await GetBlobClientAsync(semaphoreName, cancellationToken);
            (SemaphoreData data, _) = await ReadSemaphoreDataAsync(blobClient, cancellationToken);
            DateTimeOffset expiryTime = DateTimeOffset.UtcNow.Subtract(slotTimeout);
            return data.Holders.Count(h => h.LastHeartbeat >= expiryTime);
        }
        catch (OperationCanceledException)
        {
            throw;
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            BlobClient blobClient = await GetBlobClientAsync(semaphoreName, cancellationToken);
            (SemaphoreData data, _) = await ReadSemaphoreDataAsync(blobClient, cancellationToken);

            return data.Holders
                .Select(h => new SemaphoreHolder(h.HolderId, h.AcquiredAt, h.LastHeartbeat, h.Metadata))
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
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

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            BlobClient blobClient = await GetBlobClientAsync(semaphoreName, cancellationToken);
            (SemaphoreData data, _) = await ReadSemaphoreDataAsync(blobClient, cancellationToken);
            return data.Holders.Exists(h => h.HolderId == holderId);
        }
        catch (OperationCanceledException)
        {
            throw;
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
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(options.ContainerName);
            await containerClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check failed for Azure Blob Storage semaphore provider");
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
        throw new ObjectDisposedException(nameof(AzureBlobStorageSemaphoreProvider));
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
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(options.ContainerName);
                await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                logger.LogInformation("Container {ContainerName} created or verified", options.ContainerName);
            }

            isInitialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task<BlobClient> GetBlobClientAsync(string semaphoreName, CancellationToken cancellationToken)
    {
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(options.ContainerName);
        string blobName = $"{semaphoreName}.json";
        BlobClient blobClient = containerClient.GetBlobClient(blobName);

        if (await blobClient.ExistsAsync(cancellationToken)) return blobClient;

        try
        {
            var emptyData = new SemaphoreData { Holders = [] };
            string json = JsonSerializer.Serialize(emptyData);
            byte[] content = Encoding.UTF8.GetBytes(json);
            await blobClient.UploadAsync(new BinaryData(content), cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Blob was created by another instance, ignore
        }

        return blobClient;
    }

    private async Task<(SemaphoreData Data, ETag ETag)> ReadSemaphoreDataAsync(
        BlobClient blobClient,
        CancellationToken cancellationToken)
    {
        Response<BlobDownloadResult> response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
        SemaphoreData? data = JsonSerializer.Deserialize<SemaphoreData>(response.Value.Content.ToString());
        return (data ?? new SemaphoreData { Holders = [] }, response.Value.Details.ETag);
    }

    private async Task<bool> TryWriteSemaphoreDataAsync(
        BlobClient blobClient,
        SemaphoreData data,
        ETag etag,
        CancellationToken cancellationToken)
    {
        try
        {
            string json = JsonSerializer.Serialize(data);
            byte[] content = Encoding.UTF8.GetBytes(json);
            var conditions = new BlobRequestConditions { IfMatch = etag };
            await blobClient.UploadAsync(
                new BinaryData(content),
                new BlobUploadOptions { Conditions = conditions },
                cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            // Precondition failed - ETag mismatch
            return false;
        }
    }

    /// <summary>
    /// Internal class to represent semaphore data stored in blob.
    /// </summary>
    private sealed class SemaphoreData
    {
        public List<HolderData> Holders { get; set; } = [];
    }

    /// <summary>
    /// Internal class to represent holder data.
    /// </summary>
    private sealed class HolderData
    {
        public string HolderId { get; set; } = string.Empty;
        public DateTimeOffset AcquiredAt { get; set; }
        public DateTimeOffset LastHeartbeat { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}

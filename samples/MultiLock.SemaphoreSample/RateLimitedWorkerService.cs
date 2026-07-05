using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MultiLock.SemaphoreSample;

/// <summary>
/// Background service that demonstrates rate-limited work using distributed semaphores.
/// Simulates multiple concurrent API calls that are throttled by the semaphore.
/// </summary>
public sealed class RateLimitedWorkerService : BackgroundService
{
    private readonly ISemaphoreService semaphoreService;
    private readonly ILogger<RateLimitedWorkerService> logger;
    private int requestCounter;

    public RateLimitedWorkerService(
        ISemaphoreService semaphoreService,
        ILogger<RateLimitedWorkerService> logger)
    {
        this.semaphoreService = semaphoreService;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the service to start
        await Task.Delay(1000, stoppingToken);

        logger.LogInformation(
            "Worker started. Semaphore: {SemaphoreName}, Max concurrent: {MaxCount}",
            semaphoreService.SemaphoreName,
            semaphoreService.MaxCount);

        // Start monitoring semaphore status changes in background
        _ = MonitorStatusChangesAsync(stoppingToken);

        // Simulate continuous work requests
        while (!stoppingToken.IsCancellationRequested)
        {
            int requestId = Interlocked.Increment(ref requestCounter);

            // Try to acquire a semaphore slot
            logger.LogInformation("[Request {RequestId}] Attempting to acquire semaphore slot...", requestId);

            bool acquired = await semaphoreService.TryAcquireAsync(stoppingToken);

            if (acquired)
            {
                logger.LogInformation(
                    "[Request {RequestId}] ✓ Acquired slot! Currently holding: {IsHolding}",
                    requestId,
                    semaphoreService.IsHolding);

                // Simulate API call work
                await SimulateApiCallAsync(requestId, stoppingToken);

                // Release the slot
                await semaphoreService.ReleaseAsync(stoppingToken);
                logger.LogInformation("[Request {RequestId}] Released slot", requestId);
            }
            else
            {
                logger.LogWarning(
                    "[Request {RequestId}] ✗ Could not acquire slot (semaphore full)",
                    requestId);
            }

            // Wait before next request
            await Task.Delay(Random.Shared.Next(500, 2000), stoppingToken);
        }
    }

    private async Task SimulateApiCallAsync(int requestId, CancellationToken cancellationToken)
    {
        // Simulate variable API call duration
        int duration = Random.Shared.Next(1000, 5000);
        logger.LogInformation(
            "[Request {RequestId}] Processing API call (will take {Duration}ms)...",
            requestId,
            duration);

        await Task.Delay(duration, cancellationToken);

        logger.LogInformation("[Request {RequestId}] API call completed successfully", requestId);
    }

    private async Task MonitorStatusChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (SemaphoreChangedEventArgs change in
                semaphoreService.GetStatusChangesAsync(cancellationToken))
            {
                if (change.AcquiredSlot)
                {
                    logger.LogInformation(
                        "📈 Status: Acquired slot (now holding, {Available}/{Max} available)",
                        change.CurrentStatus.AvailableSlots,
                        change.CurrentStatus.MaxCount);
                }
                else if (change.LostSlot)
                {
                    logger.LogInformation(
                        "📉 Status: Lost slot (no longer holding, {Available}/{Max} available)",
                        change.CurrentStatus.AvailableSlots,
                        change.CurrentStatus.MaxCount);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
    }
}


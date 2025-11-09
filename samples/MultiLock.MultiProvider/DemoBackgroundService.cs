using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MultiLock.MultiProvider;

/// <summary>
/// Demo background service that shows leadership status using the AsyncEnumerable API.
/// </summary>
public class DemoBackgroundService(
    ILeaderElectionService leaderElection,
    ILogger<DemoBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start listening to leadership changes using AsyncEnumerable
        var leadershipTask = Task.Run(async () =>
        {
            await foreach (LeadershipChangedEventArgs change in leaderElection.GetLeadershipChangesAsync(stoppingToken))
            {
                if (change.BecameLeader)
                {
                    logger.LogInformation("🎉 [{ParticipantId}] Leadership acquired!",
                        leaderElection.ParticipantId);
                }
                else if (change.LostLeadership)
                {
                    logger.LogWarning("😞 [{ParticipantId}] Leadership lost!",
                        leaderElection.ParticipantId);
                }
            }
        }, stoppingToken);

        int workCounter = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (leaderElection.IsLeader)
            {
                // Perform leader-only work
                workCounter++;
                logger.LogInformation("🏆 [{ParticipantId}] Leader performing work #{WorkCounter}",
                    leaderElection.ParticipantId, workCounter);

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            else
            {
                // Follower behavior
                logger.LogInformation("👥 [{ParticipantId}] Follower waiting...",
                    leaderElection.ParticipantId);

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // Wait for leadership monitoring to complete
        try
        {
            await leadershipTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
    }
}

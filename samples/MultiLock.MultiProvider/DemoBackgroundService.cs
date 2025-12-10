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
        int workCounter = 0;

        // Listen to leadership changes and perform work accordingly
        await foreach (LeadershipChangedEventArgs change in leaderElection.GetLeadershipChangesAsync(stoppingToken))
        {
            if (change.BecameLeader)
            {
                logger.LogInformation("🎉 [{ParticipantId}] Leadership acquired!",
                    leaderElection.ParticipantId);

                // Perform leader-only work
                while (leaderElection.IsLeader && !stoppingToken.IsCancellationRequested)
                {
                    workCounter++;
                    logger.LogInformation("🏆 [{ParticipantId}] Leader performing work #{WorkCounter}",
                        leaderElection.ParticipantId, workCounter);

                    // Small delay to avoid busy-waiting during leader work
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            else if (change.LostLeadership)
            {
                logger.LogWarning("😞 [{ParticipantId}] Leadership lost!",
                    leaderElection.ParticipantId);
            }
        }
    }
}

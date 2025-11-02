using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiLock;

namespace MultiLock.Sample;

/// <summary>
/// Sample background service that demonstrates leader election usage with AsyncEnumerable API.
/// </summary>
public class SampleBackgroundService(
    ILeaderElectionService leaderElection,
    ILogger<SampleBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Sample service started. Participant ID: {ParticipantId}", leaderElection.ParticipantId);

        // Start listening to leadership changes in a background task
        var leadershipTask = Task.Run(async () =>
        {
            await foreach (var change in leaderElection.GetLeadershipChangesAsync(stoppingToken))
            {
                if (change.BecameLeader)
                {
                    logger.LogInformation("🎉 Leadership acquired! I am now the leader.");
                }
                else if (change.LostLeadership)
                {
                    logger.LogWarning("😞 Leadership lost! I am no longer the leader.");
                }

                logger.LogInformation("Leadership status changed from {PreviousIsLeader} to {CurrentIsLeader}",
                    change.PreviousStatus.IsLeader, change.CurrentStatus.IsLeader);
            }
        }, stoppingToken);

        int workCounter = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (leaderElection.IsLeader)
            {
                // Perform leader-only work
                workCounter++;
                logger.LogInformation("🏆 Leader performing work #{WorkCounter}", workCounter);

                // Simulate some work
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            else
            {
                // Wait for leadership or perform follower work
                logger.LogInformation("👥 Follower waiting for leadership...");

                // Check current leader
                LeaderInfo? currentLeader = await leaderElection.GetCurrentLeaderAsync(stoppingToken);
                if (currentLeader != null)
                {
                    logger.LogInformation("Current leader: {LeaderId} (acquired at {AcquiredAt})",
                        currentLeader.LeaderId, currentLeader.LeadershipAcquiredAt);
                }

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

        // Wait for the leadership monitoring task to complete
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

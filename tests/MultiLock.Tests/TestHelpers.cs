using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiLock.InMemory;

namespace MultiLock.Tests;

/// <summary>
/// Provides common helper methods for unit tests.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Waits for a condition to be met within a specified timeout period.
    /// </summary>
    /// <param name="condition">The condition to check.</param>
    /// <param name="timeout">The maximum time to wait for the condition.</param>
    /// <param name="cancellationToken">A token to cancel the wait operation.</param>
    /// <param name="lockObject">Optional lock object for thread-safe condition checking.</param>
    /// <exception cref="TimeoutException">Thrown when the condition is not met within the timeout period.</exception>
    public static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken, object? lockObject = null)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            while (true)
            {
                bool conditionMet;
                if (lockObject != null)
                {
                    lock (lockObject)
                    {
                        conditionMet = condition();
                    }
                }
                else
                {
                    conditionMet = condition();
                }

                if (conditionMet)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), linkedCts.Token);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Condition was not met within the timeout period of {timeout.TotalSeconds} seconds.");
        }
    }

    /// <summary>
    /// Waits for an async condition to be met within a specified timeout period.
    /// </summary>
    /// <param name="condition">The async condition to check.</param>
    /// <param name="timeout">The maximum time to wait for the condition.</param>
    /// <param name="cancellationToken">A token to cancel the wait operation.</param>
    /// <exception cref="TimeoutException">Thrown when the condition is not met within the timeout period.</exception>
    public static async Task WaitForConditionAsync(Func<Task<bool>> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            while (true)
            {
                bool conditionMet = await condition();

                if (conditionMet)
                    return;

                await Task.Delay(TimeSpan.FromMilliseconds(10), linkedCts.Token);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Condition was not met within the timeout period of {timeout.TotalSeconds} seconds.");
        }
    }

    /// <summary>
    /// Creates a ServiceProvider configured with a leader election service for testing.
    /// </summary>
    /// <param name="participantId">The participant ID for the leader election service.</param>
    /// <returns>A configured ServiceProvider.</returns>
    public static ServiceProvider CreateLeaderElectionService(string participantId)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection.AddSingleton<ILeaderElectionProvider>(sp =>
            new InMemoryLeaderElectionProvider(sp.GetRequiredService<ILogger<InMemoryLeaderElectionProvider>>()));
        serviceCollection.Configure<LeaderElectionOptions>(options =>
        {
            options.ParticipantId = participantId;
            options.ElectionGroup = "test-group";
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
            options.HeartbeatTimeout = TimeSpan.FromMilliseconds(300);
            options.ElectionInterval = TimeSpan.FromMilliseconds(50);
            options.AutoStart = false;
        });
        serviceCollection.AddSingleton<ILeaderElectionService, LeaderElectionService>();

        return serviceCollection.BuildServiceProvider();
    }
}


using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.InMemory;

/// <summary>
/// Extension methods for configuring In-Memory leader election and semaphore services.
/// </summary>
public static class InMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Adds In-Memory leader election services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryLeaderElection(
        this IServiceCollection services,
        Action<LeaderElectionOptions>? configureOptions = null)
    {
        return services.AddLeaderElection<InMemoryLeaderElectionProvider>(configureOptions);
    }

    /// <summary>
    /// Adds In-Memory semaphore services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the semaphore options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInMemorySemaphore(
        this IServiceCollection services,
        Action<SemaphoreOptions>? configureOptions = null)
    {
        return services.AddSemaphore<InMemorySemaphoreProvider>(configureOptions);
    }
}

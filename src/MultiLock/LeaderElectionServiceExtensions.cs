using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MultiLock;

/// <summary>
/// Extension methods for configuring leader election services.
/// </summary>
public static class LeaderElectionServiceExtensions
{
    /// <summary>
    /// Adds leader election services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddLeaderElection(
        this IServiceCollection services,
        Action<LeaderElectionOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<ILeaderElectionService, LeaderElectionService>();
        services.AddHostedService<LeaderElectionService>(provider =>
            (LeaderElectionService)provider.GetRequiredService<ILeaderElectionService>());

        return services;
    }

    /// <summary>
    /// Adds leader election services with a specific provider to the dependency injection container.
    /// </summary>
    /// <typeparam name="TProvider">The type of the leader election provider.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddLeaderElection<TProvider>(
        this IServiceCollection services,
        Action<LeaderElectionOptions>? configureOptions = null)
        where TProvider : class, ILeaderElectionProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ILeaderElectionProvider, TProvider>();
        return services.AddLeaderElection(configureOptions);
    }

    /// <summary>
    /// Adds leader election services with a provider factory to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="providerFactory">A factory function to create the provider.</param>
    /// <param name="configureOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="providerFactory"/> is null.</exception>
    public static IServiceCollection AddLeaderElection(
        this IServiceCollection services,
        Func<IServiceProvider, ILeaderElectionProvider> providerFactory,
        Action<LeaderElectionOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(providerFactory);

        services.TryAddSingleton(providerFactory);
        return services.AddLeaderElection(configureOptions);
    }
}

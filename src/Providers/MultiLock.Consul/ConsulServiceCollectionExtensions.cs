using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.Consul;

/// <summary>
/// Extension methods for configuring Consul leader election services.
/// </summary>
public static class ConsulServiceCollectionExtensions
{
    /// <summary>
    /// Adds Consul leader election services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the Consul options.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConsulLeaderElection(
        this IServiceCollection services,
        Action<ConsulLeaderElectionOptions> configureOptions,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        services.Configure(configureOptions);

        if (configureLeaderElectionOptions != null)
            services.Configure(configureLeaderElectionOptions);

        return services.AddLeaderElection<ConsulLeaderElectionProvider>();
    }

    /// <summary>
    /// Adds Consul leader election services to the dependency injection container with address.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="address">The Consul server address.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConsulLeaderElection(
        this IServiceCollection services,
        string address,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddConsulLeaderElection(
            options => options.Address = address,
            configureLeaderElectionOptions);
    }

    /// <summary>
    /// Adds Consul leader election services to the dependency injection container with address and token.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="address">The Consul server address.</param>
    /// <param name="token">The ACL token for authentication.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConsulLeaderElection(
        this IServiceCollection services,
        string address,
        string token,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddConsulLeaderElection(
            options =>
            {
                options.Address = address;
                options.Token = token;
            },
            configureLeaderElectionOptions);
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.Consul;

/// <summary>
/// Extension methods for configuring Consul leader election and semaphore services.
/// </summary>
public static class ConsulServiceCollectionExtensions
{
    // Leader Election Methods

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

    // Semaphore Methods

    /// <summary>
    /// Adds Consul semaphore services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the Consul options.</param>
    /// <param name="configureSemaphoreOptions">An action to configure the semaphore options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConsulSemaphore(
        this IServiceCollection services,
        Action<ConsulSemaphoreOptions> configureOptions,
        Action<SemaphoreOptions>? configureSemaphoreOptions = null)
    {
        services.Configure(configureOptions);

        if (configureSemaphoreOptions != null)
            services.Configure(configureSemaphoreOptions);

        return services.AddSemaphore<ConsulSemaphoreProvider>();
    }

    /// <summary>
    /// Adds Consul semaphore services to the dependency injection container with address.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="address">The Consul server address.</param>
    /// <param name="configureSemaphoreOptions">An action to configure the semaphore options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConsulSemaphore(
        this IServiceCollection services,
        string address,
        Action<SemaphoreOptions>? configureSemaphoreOptions = null)
    {
        return services.AddConsulSemaphore(
            options => options.Address = address,
            configureSemaphoreOptions);
    }

    /// <summary>
    /// Adds Consul semaphore services to the dependency injection container with address and token.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="address">The Consul server address.</param>
    /// <param name="token">The ACL token for authentication.</param>
    /// <param name="configureSemaphoreOptions">An action to configure the semaphore options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConsulSemaphore(
        this IServiceCollection services,
        string address,
        string token,
        Action<SemaphoreOptions>? configureSemaphoreOptions = null)
    {
        return services.AddConsulSemaphore(
            options =>
            {
                options.Address = address;
                options.Token = token;
            },
            configureSemaphoreOptions);
    }
}

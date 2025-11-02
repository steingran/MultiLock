using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.ZooKeeper;

/// <summary>
/// Extension methods for configuring ZooKeeper leader election services.
/// </summary>
public static class ZooKeeperServiceCollectionExtensions
{
    /// <summary>
    /// Adds ZooKeeper leader election services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the ZooKeeper options.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddZooKeeperLeaderElection(
        this IServiceCollection services,
        Action<ZooKeeperLeaderElectionOptions> configureOptions,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        services.Configure(configureOptions);
        
        if (configureLeaderElectionOptions != null)
        {
            services.Configure(configureLeaderElectionOptions);
        }

        return services.AddLeaderElection<ZooKeeperLeaderElectionProvider>();
    }

    /// <summary>
    /// Adds ZooKeeper leader election services to the dependency injection container with connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The ZooKeeper connection string.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddZooKeeperLeaderElection(
        this IServiceCollection services,
        string connectionString,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddZooKeeperLeaderElection(
            options => options.ConnectionString = connectionString,
            configureLeaderElectionOptions);
    }

    /// <summary>
    /// Adds ZooKeeper leader election services to the dependency injection container with connection string and root path.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The ZooKeeper connection string.</param>
    /// <param name="rootPath">The root path for leader election nodes.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddZooKeeperLeaderElection(
        this IServiceCollection services,
        string connectionString,
        string rootPath,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddZooKeeperLeaderElection(
            options =>
            {
                options.ConnectionString = connectionString;
                options.RootPath = rootPath;
            },
            configureLeaderElectionOptions);
    }

    /// <summary>
    /// Adds ZooKeeper leader election services to the dependency injection container with full configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The ZooKeeper connection string.</param>
    /// <param name="rootPath">The root path for leader election nodes.</param>
    /// <param name="sessionTimeout">The session timeout for ZooKeeper connections.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddZooKeeperLeaderElection(
        this IServiceCollection services,
        string connectionString,
        string rootPath,
        TimeSpan sessionTimeout,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddZooKeeperLeaderElection(
            options =>
            {
                options.ConnectionString = connectionString;
                options.RootPath = rootPath;
                options.SessionTimeout = sessionTimeout;
            },
            configureLeaderElectionOptions);
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.Redis;

/// <summary>
/// Extension methods for configuring Redis leader election services.
/// </summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Adds Redis leader election services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the Redis options.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisLeaderElection(
        this IServiceCollection services,
        Action<RedisLeaderElectionOptions> configureOptions,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        services.Configure(configureOptions);
        
        if (configureLeaderElectionOptions != null)
        {
            services.Configure(configureLeaderElectionOptions);
        }

        return services.AddLeaderElection<RedisLeaderElectionProvider>();
    }

    /// <summary>
    /// Adds Redis leader election services to the dependency injection container with connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Redis connection string.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisLeaderElection(
        this IServiceCollection services,
        string connectionString,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddRedisLeaderElection(
            options => options.ConnectionString = connectionString,
            configureLeaderElectionOptions);
    }

    /// <summary>
    /// Adds Redis leader election services to the dependency injection container with connection string and database.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Redis connection string.</param>
    /// <param name="database">The Redis database number.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisLeaderElection(
        this IServiceCollection services,
        string connectionString,
        int database,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddRedisLeaderElection(
            options =>
            {
                options.ConnectionString = connectionString;
                options.Database = database;
            },
            configureLeaderElectionOptions);
    }
}

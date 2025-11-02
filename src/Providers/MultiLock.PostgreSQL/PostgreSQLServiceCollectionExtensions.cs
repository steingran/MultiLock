using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.PostgreSQL;

/// <summary>
/// Extension methods for configuring PostgreSQL leader election services.
/// </summary>
public static class PostgreSqlServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL leader election services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the PostgreSQL options.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgreSqlLeaderElection(
        this IServiceCollection services,
        Action<PostgreSqlLeaderElectionOptions> configureOptions,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        services.Configure(configureOptions);

        if (configureLeaderElectionOptions != null)
            services.Configure(configureLeaderElectionOptions);

        return services.AddLeaderElection<PostgreSqlLeaderElectionProvider>();
    }

    /// <summary>
    /// Adds PostgreSQL leader election services to the dependency injection container with connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgreSqlLeaderElection(
        this IServiceCollection services,
        string connectionString,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddPostgreSqlLeaderElection(
            options => options.ConnectionString = connectionString,
            configureLeaderElectionOptions);
    }

    /// <summary>
    /// Adds PostgreSQL leader election services to the dependency injection container with connection string and table configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The name of the leader election table.</param>
    /// <param name="schemaName">The schema name for the leader election table.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgreSqlLeaderElection(
        this IServiceCollection services,
        string connectionString,
        string tableName,
        string schemaName = "public",
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddPostgreSqlLeaderElection(
            options =>
            {
                options.ConnectionString = connectionString;
                options.TableName = tableName;
                options.SchemaName = schemaName;
            },
            configureLeaderElectionOptions);
    }
}

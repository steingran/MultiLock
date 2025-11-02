using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.SqlServer;

/// <summary>
/// Extension methods for configuring SQL Server leader election services.
/// </summary>
public static class SqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL Server leader election services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the SQL Server options.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlServerLeaderElection(
        this IServiceCollection services,
        Action<SqlServerLeaderElectionOptions> configureOptions,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        services.Configure(configureOptions);
        
        if (configureLeaderElectionOptions != null)
        {
            services.Configure(configureLeaderElectionOptions);
        }

        return services.AddLeaderElection<SqlServerLeaderElectionProvider>();
    }

    /// <summary>
    /// Adds SQL Server leader election services to the dependency injection container with connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlServerLeaderElection(
        this IServiceCollection services,
        string connectionString,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddSqlServerLeaderElection(
            options => options.ConnectionString = connectionString,
            configureLeaderElectionOptions);
    }
}

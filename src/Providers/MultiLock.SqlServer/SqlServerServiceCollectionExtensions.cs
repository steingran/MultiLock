using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.SqlServer;

/// <summary>
/// Extension methods for configuring SQL Server leader election and semaphore services.
/// </summary>
public static class SqlServerServiceCollectionExtensions
{
    // Leader Election Methods
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

    // Semaphore Methods

    /// <summary>
    /// Adds SQL Server semaphore services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the SQL Server options.</param>
    /// <param name="configureSemaphoreOptions">An action to configure the semaphore options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlServerSemaphore(
        this IServiceCollection services,
        Action<SqlServerSemaphoreOptions> configureOptions,
        Action<SemaphoreOptions>? configureSemaphoreOptions = null)
    {
        services.Configure(configureOptions);

        if (configureSemaphoreOptions != null)
            services.Configure(configureSemaphoreOptions);

        return services.AddSemaphore<SqlServerSemaphoreProvider>();
    }

    /// <summary>
    /// Adds SQL Server semaphore services to the dependency injection container with connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="configureSemaphoreOptions">An action to configure the semaphore options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlServerSemaphore(
        this IServiceCollection services,
        string connectionString,
        Action<SemaphoreOptions>? configureSemaphoreOptions = null)
    {
        return services.AddSqlServerSemaphore(
            options => options.ConnectionString = connectionString,
            configureSemaphoreOptions);
    }
}

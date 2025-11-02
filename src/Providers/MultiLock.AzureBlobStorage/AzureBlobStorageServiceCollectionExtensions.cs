using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.AzureBlobStorage;

/// <summary>
/// Extension methods for configuring Azure Blob Storage leader election services.
/// </summary>
public static class AzureBlobStorageServiceCollectionExtensions
{
    /// <summary>
    /// Adds Azure Blob Storage leader election services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the Azure Blob Storage options.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAzureBlobStorageLeaderElection(
        this IServiceCollection services,
        Action<AzureBlobStorageLeaderElectionOptions> configureOptions,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        services.Configure(configureOptions);

        if (configureLeaderElectionOptions != null)
            services.Configure(configureLeaderElectionOptions);

        return services.AddLeaderElection<AzureBlobStorageLeaderElectionProvider>();
    }

    /// <summary>
    /// Adds Azure Blob Storage leader election services to the dependency injection container with connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Azure Storage connection string.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAzureBlobStorageLeaderElection(
        this IServiceCollection services,
        string connectionString,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddAzureBlobStorageLeaderElection(
            options => options.ConnectionString = connectionString,
            configureLeaderElectionOptions);
    }

    /// <summary>
    /// Adds Azure Blob Storage leader election services to the dependency injection container with connection string and container name.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Azure Storage connection string.</param>
    /// <param name="containerName">The container name for leader election blobs.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAzureBlobStorageLeaderElection(
        this IServiceCollection services,
        string connectionString,
        string containerName,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddAzureBlobStorageLeaderElection(
            options =>
            {
                options.ConnectionString = connectionString;
                options.ContainerName = containerName;
            },
            configureLeaderElectionOptions);
    }
}

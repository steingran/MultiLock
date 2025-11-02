using Microsoft.Extensions.DependencyInjection;

namespace MultiLock.FileSystem;

/// <summary>
/// Extension methods for configuring File System leader election services.
/// </summary>
public static class FileSystemServiceCollectionExtensions
{
    /// <summary>
    /// Adds File System leader election services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the file system options.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFileSystemLeaderElection(
        this IServiceCollection services,
        Action<FileSystemLeaderElectionOptions> configureOptions,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        services.Configure(configureOptions);

        if (configureLeaderElectionOptions != null)
            services.Configure(configureLeaderElectionOptions);

        return services.AddLeaderElection<FileSystemLeaderElectionProvider>();
    }

    /// <summary>
    /// Adds File System leader election services to the dependency injection container with directory path.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="directoryPath">The directory path for leader election files.</param>
    /// <param name="configureLeaderElectionOptions">An action to configure the leader election options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFileSystemLeaderElection(
        this IServiceCollection services,
        string directoryPath,
        Action<LeaderElectionOptions>? configureLeaderElectionOptions = null)
    {
        return services.AddFileSystemLeaderElection(
            options => options.DirectoryPath = directoryPath,
            configureLeaderElectionOptions);
    }
}

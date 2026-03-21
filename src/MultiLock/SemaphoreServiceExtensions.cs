using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MultiLock;

/// <summary>
/// Extension methods for configuring distributed semaphore services.
/// </summary>
public static class SemaphoreServiceExtensions
{
    /// <summary>
    /// Adds distributed semaphore services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the semaphore options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddSemaphore(
        this IServiceCollection services,
        Action<SemaphoreOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureOptions != null)
            services.Configure(configureOptions);

        services.TryAddSingleton<ISemaphoreService, SemaphoreService>();
        services.AddHostedService<SemaphoreService>(provider =>
            (SemaphoreService)provider.GetRequiredService<ISemaphoreService>());

        return services;
    }

    /// <summary>
    /// Adds distributed semaphore services with a specific provider to the dependency injection container.
    /// </summary>
    /// <typeparam name="TProvider">The type of the semaphore provider.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">An action to configure the semaphore options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddSemaphore<TProvider>(
        this IServiceCollection services,
        Action<SemaphoreOptions>? configureOptions = null)
        where TProvider : class, ISemaphoreProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISemaphoreProvider, TProvider>();
        return services.AddSemaphore(configureOptions);
    }

    /// <summary>
    /// Adds distributed semaphore services with a provider factory to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="providerFactory">A factory function to create the provider.</param>
    /// <param name="configureOptions">An action to configure the semaphore options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="providerFactory"/> is null.</exception>
    public static IServiceCollection AddSemaphore(
        this IServiceCollection services,
        Func<IServiceProvider, ISemaphoreProvider> providerFactory,
        Action<SemaphoreOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(providerFactory);

        services.TryAddSingleton(providerFactory);
        return services.AddSemaphore(configureOptions);
    }
}


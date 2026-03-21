using Microsoft.Extensions.DependencyInjection;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class InMemoryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemoryLeaderElection_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() =>
            services!.AddInMemoryLeaderElection());
        exception.ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddInMemoryLeaderElection_WithValidServices_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddInMemoryLeaderElection();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<InMemoryLeaderElectionProvider>();
    }

    [Fact]
    public void AddInMemoryLeaderElection_WithConfigureOptions_ShouldRegisterProviderAndConfigureOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddInMemoryLeaderElection(options =>
        {
            options.ElectionGroup = "test-group";
            options.ParticipantId = "test-participant";
            options.HeartbeatInterval = TimeSpan.FromSeconds(10);
        });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<InMemoryLeaderElectionProvider>();
    }

    [Fact]
    public void AddInMemoryLeaderElection_WithNullConfigureOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddInMemoryLeaderElection(configureOptions: null);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<InMemoryLeaderElectionProvider>();
    }

    [Fact]
    public void AddInMemoryLeaderElection_ShouldAllowMultipleCalls()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddInMemoryLeaderElection();
        services.AddInMemoryLeaderElection(options => options.ElectionGroup = "group1");

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<InMemoryLeaderElectionProvider>();
    }

    // AddInMemorySemaphore tests

    [Fact]
    public void AddInMemorySemaphore_WithNullServices_ShouldThrowArgumentNullException()
    {
        IServiceCollection? services = null;
        Should.Throw<ArgumentNullException>(() => services!.AddInMemorySemaphore())
            .ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddInMemorySemaphore_WithValidServices_ShouldRegisterProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemorySemaphore(o => o.SemaphoreName = "test-sem");

        ServiceProvider provider = services.BuildServiceProvider();
        ISemaphoreProvider? semaphoreProvider = provider.GetService<ISemaphoreProvider>();
        semaphoreProvider.ShouldNotBeNull();
        semaphoreProvider.ShouldBeOfType<InMemorySemaphoreProvider>();
        provider.GetService<ISemaphoreService>().ShouldNotBeNull();
    }

    [Fact]
    public void AddInMemorySemaphore_WithConfigureOptions_ShouldSucceed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemorySemaphore(o =>
        {
            o.SemaphoreName = "test-sem";
            o.MaxCount = 3;
        });

        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<ISemaphoreService>().ShouldNotBeNull();
    }

    [Fact]
    public void AddInMemorySemaphore_WithNullConfigureOptions_ShouldSucceed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Null configureOptions means SemaphoreName stays at default;
        // SemaphoreService validation is deferred until resolution.
        // Only verify the provider itself is registered eagerly.
        services.AddInMemorySemaphore(configureOptions: null);

        ServiceProvider provider = services.BuildServiceProvider();
        ISemaphoreProvider? semaphoreProvider = provider.GetService<ISemaphoreProvider>();
        semaphoreProvider.ShouldNotBeNull();
        semaphoreProvider.ShouldBeOfType<InMemorySemaphoreProvider>();
    }
}


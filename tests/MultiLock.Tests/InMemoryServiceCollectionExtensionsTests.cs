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
}


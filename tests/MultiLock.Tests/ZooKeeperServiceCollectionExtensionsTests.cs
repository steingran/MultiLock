using Microsoft.Extensions.DependencyInjection;
using MultiLock.ZooKeeper;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class ZooKeeperServiceCollectionExtensionsTests
{
    [Fact]
    public void AddZooKeeperLeaderElection_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() =>
            services!.AddZooKeeperLeaderElection(options => options.ConnectionString = "localhost:2181"));
        exception.ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddZooKeeperLeaderElection_WithConfigureOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddZooKeeperLeaderElection(options =>
        {
            options.ConnectionString = "localhost:2181";
            options.RootPath = "/test";
        });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }

    [Fact]
    public void AddZooKeeperLeaderElection_WithConfigureOptionsAndLeaderElectionOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddZooKeeperLeaderElection(
            options =>
            {
                options.ConnectionString = "localhost:2181";
                options.RootPath = "/test";
            },
            leaderElectionOptions =>
            {
                leaderElectionOptions.ElectionGroup = "test-group";
                leaderElectionOptions.ParticipantId = "test-participant";
            });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }

    [Fact]
    public void AddZooKeeperLeaderElection_WithConnectionString_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddZooKeeperLeaderElection("localhost:2181");

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }

    [Fact]
    public void AddZooKeeperLeaderElection_WithConnectionStringAndLeaderElectionOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddZooKeeperLeaderElection(
            "localhost:2181",
            leaderElectionOptions =>
            {
                leaderElectionOptions.ElectionGroup = "test-group";
                leaderElectionOptions.ParticipantId = "test-participant";
            });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }

    [Fact]
    public void AddZooKeeperLeaderElection_WithConnectionStringAndRootPath_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddZooKeeperLeaderElection("localhost:2181", "/test");

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }

    [Fact]
    public void AddZooKeeperLeaderElection_WithConnectionStringRootPathAndLeaderElectionOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddZooKeeperLeaderElection(
            "localhost:2181",
            "/test",
            leaderElectionOptions =>
            {
                leaderElectionOptions.ElectionGroup = "test-group";
            });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }

    [Fact]
    public void AddZooKeeperLeaderElection_WithFullConfiguration_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddZooKeeperLeaderElection(
            "localhost:2181",
            "/test",
            TimeSpan.FromSeconds(30));

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }

    [Fact]
    public void AddZooKeeperLeaderElection_WithFullConfigurationAndLeaderElectionOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddZooKeeperLeaderElection(
            "localhost:2181",
            "/test",
            TimeSpan.FromSeconds(30),
            leaderElectionOptions =>
            {
                leaderElectionOptions.ElectionGroup = "test-group";
                leaderElectionOptions.ParticipantId = "test-participant";
            });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }

    [Fact]
    public void AddZooKeeperLeaderElection_WithNullLeaderElectionOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddZooKeeperLeaderElection(
            "localhost:2181",
            configureLeaderElectionOptions: null);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }

    [Fact]
    public void AddZooKeeperLeaderElection_ShouldAllowMultipleCalls()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddZooKeeperLeaderElection("localhost:2181");
        services.AddZooKeeperLeaderElection("localhost:2182", "/test");

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<ZooKeeperLeaderElectionProvider>();
    }
}


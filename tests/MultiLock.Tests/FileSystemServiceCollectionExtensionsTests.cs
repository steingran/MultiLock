using Microsoft.Extensions.DependencyInjection;
using MultiLock.FileSystem;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class FileSystemServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFileSystemLeaderElection_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() =>
            services!.AddFileSystemLeaderElection(options => options.DirectoryPath = "test"));
        exception.ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddFileSystemLeaderElection_WithConfigureOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddFileSystemLeaderElection(options =>
        {
            options.DirectoryPath = Path.Combine(Path.GetTempPath(), "test-leader-election");
        });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<FileSystemLeaderElectionProvider>();
    }

    [Fact]
    public void AddFileSystemLeaderElection_WithConfigureOptionsAndLeaderElectionOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddFileSystemLeaderElection(
            options => options.DirectoryPath = Path.Combine(Path.GetTempPath(), "test-leader-election"),
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
        leaderElectionProvider.ShouldBeOfType<FileSystemLeaderElectionProvider>();
    }

    [Fact]
    public void AddFileSystemLeaderElection_WithDirectoryPath_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        string directoryPath = Path.Combine(Path.GetTempPath(), "test-leader-election");

        // Act
        IServiceCollection result = services.AddFileSystemLeaderElection(directoryPath);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<FileSystemLeaderElectionProvider>();
    }

    [Fact]
    public void AddFileSystemLeaderElection_WithDirectoryPathAndLeaderElectionOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        string directoryPath = Path.Combine(Path.GetTempPath(), "test-leader-election");

        // Act
        IServiceCollection result = services.AddFileSystemLeaderElection(
            directoryPath,
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
        leaderElectionProvider.ShouldBeOfType<FileSystemLeaderElectionProvider>();
    }

    [Fact]
    public void AddFileSystemLeaderElection_WithNullLeaderElectionOptions_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        string directoryPath = Path.Combine(Path.GetTempPath(), "test-leader-election");

        // Act
        IServiceCollection result = services.AddFileSystemLeaderElection(directoryPath, configureLeaderElectionOptions: null);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<FileSystemLeaderElectionProvider>();
    }

    [Fact]
    public void AddFileSystemLeaderElection_ShouldAllowMultipleCalls()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddFileSystemLeaderElection(Path.Combine(Path.GetTempPath(), "test1"));
        services.AddFileSystemLeaderElection(Path.Combine(Path.GetTempPath(), "test2"));

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        ILeaderElectionProvider? leaderElectionProvider = provider.GetService<ILeaderElectionProvider>();
        leaderElectionProvider.ShouldNotBeNull();
        leaderElectionProvider.ShouldBeOfType<FileSystemLeaderElectionProvider>();
    }
}


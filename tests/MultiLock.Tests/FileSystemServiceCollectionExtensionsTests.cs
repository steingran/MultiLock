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

    // AddFileSystemSemaphore tests

    [Fact]
    public void AddFileSystemSemaphore_WithNullServices_ShouldThrowArgumentNullException()
    {
        IServiceCollection? services = null;
        Should.Throw<ArgumentNullException>(() =>
            services!.AddFileSystemSemaphore(options => options.DirectoryPath = "test"))
            .ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddFileSystemSemaphore_WithConfigureOptions_ShouldRegisterProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        IServiceCollection result = services.AddFileSystemSemaphore(
            options => options.DirectoryPath = Path.Combine(Path.GetTempPath(), "test-semaphore"),
            semaphoreOptions => semaphoreOptions.SemaphoreName = "test-sem");

        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ISemaphoreProvider? semaphoreProvider = provider.GetService<ISemaphoreProvider>();
        semaphoreProvider.ShouldNotBeNull();
        semaphoreProvider.ShouldBeOfType<FileSystemSemaphoreProvider>();
        provider.GetService<ISemaphoreService>().ShouldNotBeNull();
    }

    [Fact]
    public void AddFileSystemSemaphore_WithConfigureOptionsAndSemaphoreOptions_ShouldSucceed()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        IServiceCollection result = services.AddFileSystemSemaphore(
            options => options.DirectoryPath = Path.Combine(Path.GetTempPath(), "test-semaphore"),
            semaphoreOptions =>
            {
                semaphoreOptions.SemaphoreName = "my-semaphore";
                semaphoreOptions.MaxCount = 5;
            });

        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<ISemaphoreService>().ShouldNotBeNull();
    }

    [Fact]
    public void AddFileSystemSemaphore_WithNullSemaphoreOptions_ShouldSucceed()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // When configureSemaphoreOptions is null, options stay at defaults;
        // the provider is registered but validation of SemaphoreName is deferred to first use.
        IServiceCollection result = services.AddFileSystemSemaphore(
            options => options.DirectoryPath = Path.Combine(Path.GetTempPath(), "test-semaphore"),
            configureSemaphoreOptions: null);

        result.ShouldBe(services);

        // Only verify the provider itself is registered (the service is lazy-validated)
        ServiceProvider provider = services.BuildServiceProvider();
        ISemaphoreProvider? semaphoreProvider = provider.GetService<ISemaphoreProvider>();
        semaphoreProvider.ShouldNotBeNull();
        semaphoreProvider.ShouldBeOfType<FileSystemSemaphoreProvider>();
    }

    [Fact]
    public void AddFileSystemSemaphore_WithDirectoryPath_ShouldRegisterProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        string directoryPath = Path.Combine(Path.GetTempPath(), "test-semaphore");

        IServiceCollection result = services.AddFileSystemSemaphore(directoryPath);

        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ISemaphoreProvider? semaphoreProvider = provider.GetService<ISemaphoreProvider>();
        semaphoreProvider.ShouldNotBeNull();
        semaphoreProvider.ShouldBeOfType<FileSystemSemaphoreProvider>();
    }

    [Fact]
    public void AddFileSystemSemaphore_WithDirectoryPathAndSemaphoreOptions_ShouldSucceed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        string directoryPath = Path.Combine(Path.GetTempPath(), "test-semaphore");

        IServiceCollection result = services.AddFileSystemSemaphore(
            directoryPath,
            semaphoreOptions =>
            {
                semaphoreOptions.SemaphoreName = "test-sem";
                semaphoreOptions.MaxCount = 3;
            });

        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<ISemaphoreService>().ShouldNotBeNull();
    }

    [Fact]
    public void AddFileSystemSemaphore_WithDirectoryPathAndNullSemaphoreOptions_ShouldSucceed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        string directoryPath = Path.Combine(Path.GetTempPath(), "test-semaphore");

        // configureSemaphoreOptions null → provider registered, SemaphoreName validated at first use
        IServiceCollection result = services.AddFileSystemSemaphore(directoryPath, configureSemaphoreOptions: null);

        result.ShouldBe(services);

        ServiceProvider provider = services.BuildServiceProvider();
        ISemaphoreProvider? semaphoreProvider = provider.GetService<ISemaphoreProvider>();
        semaphoreProvider.ShouldNotBeNull();
        semaphoreProvider.ShouldBeOfType<FileSystemSemaphoreProvider>();
    }
}


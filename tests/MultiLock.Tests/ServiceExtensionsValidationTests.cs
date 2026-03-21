using Microsoft.Extensions.DependencyInjection;
using MultiLock.InMemory;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Tests to verify that LeaderElectionServiceExtensions properly validate their input parameters.
/// </summary>
public class ServiceExtensionsValidationTests
{
    [Fact]
    public void AddLeaderElection_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() =>
            services!.AddLeaderElection());
        exception.ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddLeaderElection_WithValidServices_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        IServiceCollection result = services.AddLeaderElection();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);
    }

    [Fact]
    public void AddLeaderElection_WithConfigureOptions_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        IServiceCollection result = services.AddLeaderElection(options =>
        {
            options.ElectionGroup = "test-group";
            options.ParticipantId = "test-participant";
        });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);
    }

    [Fact]
    public void AddLeaderElection_WithNullConfigureOptions_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        IServiceCollection result = services.AddLeaderElection(configureOptions: null);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);
    }

    [Fact]
    public void AddLeaderElectionGeneric_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() =>
            services!.AddLeaderElection<InMemoryLeaderElectionProvider>());
        exception.ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddLeaderElectionGeneric_WithValidServices_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddLeaderElection<InMemoryLeaderElectionProvider>();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);
    }

    [Fact]
    public void AddLeaderElectionGeneric_WithConfigureOptions_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
        {
            options.ElectionGroup = "test-group";
            options.ParticipantId = "test-participant";
        });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);
    }

    [Fact]
    public void AddLeaderElectionWithFactory_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;
        Func<IServiceProvider, ILeaderElectionProvider> factory = sp =>
            new InMemoryLeaderElectionProvider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryLeaderElectionProvider>>());

        // Act & Assert
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() =>
            services!.AddLeaderElection(factory));
        exception.ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddLeaderElectionWithFactory_WithNullFactory_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Func<IServiceProvider, ILeaderElectionProvider>? factory = null;

        // Act & Assert
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() =>
            services.AddLeaderElection(factory!));
        exception.ParamName.ShouldBe("providerFactory");
    }

    [Fact]
    public void AddLeaderElectionWithFactory_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddLeaderElection((Func<IServiceProvider, ILeaderElectionProvider>)Factory);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);
        return;

        ILeaderElectionProvider Factory(IServiceProvider sp) => new InMemoryLeaderElectionProvider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryLeaderElectionProvider>>());
    }

    [Fact]
    public void AddLeaderElectionWithFactory_WithConfigureOptions_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddLeaderElection(Factory, options =>
        {
            options.ElectionGroup = "test-group";
            options.ParticipantId = "test-participant";
        });

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);
        return;

        ILeaderElectionProvider Factory(IServiceProvider sp) => new InMemoryLeaderElectionProvider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryLeaderElectionProvider>>());
    }

    [Fact]
    public void AddLeaderElectionWithFactory_WithNullConfigureOptions_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        IServiceCollection result = services.AddLeaderElection(Factory, configureOptions: null);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBe(services);
        return;

        ILeaderElectionProvider Factory(IServiceProvider sp) => new InMemoryLeaderElectionProvider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryLeaderElectionProvider>>());
    }

    // AddSemaphore tests

    [Fact]
    public void AddSemaphore_WithNullServices_ShouldThrowArgumentNullException()
    {
        IServiceCollection? services = null;
        Should.Throw<ArgumentNullException>(() => services!.AddSemaphore())
            .ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddSemaphore_WithValidServices_ShouldSucceed()
    {
        var services = new ServiceCollection();
        IServiceCollection result = services.AddSemaphore();
        result.ShouldBe(services);
    }

    [Fact]
    public void AddSemaphore_WithConfigureOptions_ShouldSucceed()
    {
        var services = new ServiceCollection();
        IServiceCollection result = services.AddSemaphore(o => o.SemaphoreName = "test");
        result.ShouldBe(services);
    }

    [Fact]
    public void AddSemaphoreGeneric_WithNullServices_ShouldThrowArgumentNullException()
    {
        IServiceCollection? services = null;
        Should.Throw<ArgumentNullException>(() => services!.AddSemaphore<InMemory.InMemorySemaphoreProvider>())
            .ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddSemaphoreGeneric_WithValidServices_ShouldRegisterProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSemaphore<InMemory.InMemorySemaphoreProvider>(o => o.SemaphoreName = "test-sem");

        ServiceProvider sp = services.BuildServiceProvider();
        sp.GetService<ISemaphoreProvider>().ShouldNotBeNull();
        sp.GetService<ISemaphoreService>().ShouldNotBeNull();
    }

    [Fact]
    public void AddSemaphoreWithFactory_WithNullServices_ShouldThrowArgumentNullException()
    {
        IServiceCollection? services = null;
        Should.Throw<ArgumentNullException>(
            () => services!.AddSemaphore(sp => new InMemory.InMemorySemaphoreProvider(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemory.InMemorySemaphoreProvider>>())))
            .ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddSemaphoreWithFactory_WithNullFactory_ShouldThrowArgumentNullException()
    {
        var services = new ServiceCollection();
        Func<IServiceProvider, ISemaphoreProvider>? factory = null;
        Should.Throw<ArgumentNullException>(() => services.AddSemaphore(factory!))
            .ParamName.ShouldBe("providerFactory");
    }

    [Fact]
    public void AddSemaphoreWithFactory_WithValidFactory_ShouldRegisterService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSemaphore(
            sp => new InMemory.InMemorySemaphoreProvider(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemory.InMemorySemaphoreProvider>>()),
            o => o.SemaphoreName = "test-sem");

        ServiceProvider sp2 = services.BuildServiceProvider();
        sp2.GetService<ISemaphoreProvider>().ShouldNotBeNull();
        sp2.GetService<ISemaphoreService>().ShouldNotBeNull();
    }
}


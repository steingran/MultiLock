using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLock.FileSystem;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class FileSystemSemaphoreProviderTests : IDisposable
{
    private readonly FileSystemSemaphoreProvider provider;
    private readonly ILoggerFactory loggerFactory;
    private readonly string directoryPath;

    public FileSystemSemaphoreProviderTests()
    {
        loggerFactory = new LoggerFactory();
        directoryPath = Path.Combine(Path.GetTempPath(), $"ml-test-{Guid.NewGuid():N}");
        provider = CreateProvider(directoryPath);
    }

    public void Dispose()
    {
        provider.Dispose();
        loggerFactory.Dispose();
        if (Directory.Exists(directoryPath))
            Directory.Delete(directoryPath, true);
    }

    private FileSystemSemaphoreProvider CreateProvider(string dir) =>
        new(Options.Create(new FileSystemSemaphoreOptions { DirectoryPath = dir }),
            loggerFactory.CreateLogger<FileSystemSemaphoreProvider>());

    private static readonly Dictionary<string, string> NoMeta = [];

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreEmpty_ShouldSucceed()
    {
        bool result = await provider.TryAcquireAsync("sem1", "h1", 3, NoMeta, TimeSpan.FromMinutes(5));
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSemaphoreFull_ShouldFail()
    {
        await provider.TryAcquireAsync("sem1", "h1", 1, NoMeta, TimeSpan.FromMinutes(5));
        bool result = await provider.TryAcquireAsync("sem1", "h2", 1, NoMeta, TimeSpan.FromMinutes(5));
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenHolderAlreadyHasSlot_ShouldRenewAndNotDoubleCount()
    {
        await provider.TryAcquireAsync("sem1", "h1", 3, NoMeta, TimeSpan.FromMinutes(5));
        bool result = await provider.TryAcquireAsync("sem1", "h1", 3, NoMeta, TimeSpan.FromMinutes(5));
        result.ShouldBeTrue();
        int count = await provider.GetCurrentCountAsync("sem1", TimeSpan.FromMinutes(5));
        count.ShouldBe(1);
    }

    [Fact]
    public async Task ReleaseAsync_WhenHolding_ShouldRelease()
    {
        await provider.TryAcquireAsync("sem1", "h1", 3, NoMeta, TimeSpan.FromMinutes(5));
        await provider.ReleaseAsync("sem1", "h1");
        bool holding = await provider.IsHoldingAsync("sem1", "h1");
        holding.ShouldBeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_WhenNotHolding_ShouldNotThrow()
    {
        await Should.NotThrowAsync(() => provider.ReleaseAsync("sem1", "h1"));
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenHolding_ShouldReturnTrue()
    {
        await provider.TryAcquireAsync("sem1", "h1", 3, NoMeta, TimeSpan.FromMinutes(5));
        bool result = await provider.UpdateHeartbeatAsync("sem1", "h1", new Dictionary<string, string> { { "k", "v" } });
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenNotHolding_ShouldReturnFalse()
    {
        bool result = await provider.UpdateHeartbeatAsync("sem1", "h1", NoMeta);
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCurrentCountAsync_ShouldReturnCorrectCount()
    {
        await provider.TryAcquireAsync("sem1", "h1", 5, NoMeta, TimeSpan.FromMinutes(5));
        await provider.TryAcquireAsync("sem1", "h2", 5, NoMeta, TimeSpan.FromMinutes(5));
        int count = await provider.GetCurrentCountAsync("sem1", TimeSpan.FromMinutes(5));
        count.ShouldBe(2);
    }

    [Fact]
    public async Task GetHoldersAsync_ShouldReturnAllActiveHolders()
    {
        await provider.TryAcquireAsync("sem1", "h1", 5, NoMeta, TimeSpan.FromMinutes(5));
        await provider.TryAcquireAsync("sem1", "h2", 5, NoMeta, TimeSpan.FromMinutes(5));
        IReadOnlyList<SemaphoreHolder> holders = await provider.GetHoldersAsync("sem1");
        holders.Count.ShouldBe(2);
        holders.ShouldContain(h => h.HolderId == "h1");
        holders.ShouldContain(h => h.HolderId == "h2");
    }

    [Fact]
    public async Task IsHoldingAsync_WhenHolding_ShouldReturnTrue()
    {
        await provider.TryAcquireAsync("sem1", "h1", 3, NoMeta, TimeSpan.FromMinutes(5));
        bool holding = await provider.IsHoldingAsync("sem1", "h1");
        holding.ShouldBeTrue();
    }

    [Fact]
    public async Task IsHoldingAsync_WhenNotHolding_ShouldReturnFalse()
    {
        bool holding = await provider.IsHoldingAsync("sem1", "h1");
        holding.ShouldBeFalse();
    }

    [Fact]
    public async Task HealthCheckAsync_ShouldReturnTrue()
    {
        bool healthy = await provider.HealthCheckAsync();
        healthy.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ml-d-{Guid.NewGuid():N}");
        var p = CreateProvider(tempDir);
        try
        {
            Should.NotThrow(() => p.Dispose());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task MethodsAfterDispose_ShouldThrowObjectDisposedException()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ml-d-{Guid.NewGuid():N}");
        var p = CreateProvider(tempDir);
        try
        {
            p.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => p.TryAcquireAsync("s", "h", 1, NoMeta, TimeSpan.FromMinutes(1)));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => p.ReleaseAsync("s", "h"));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => p.UpdateHeartbeatAsync("s", "h", NoMeta));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => p.GetCurrentCountAsync("s", TimeSpan.FromMinutes(1)));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => p.GetHoldersAsync("s"));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => p.IsHoldingAsync("s", "h"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task TryAcquireAsync_WithInvalidSemaphoreName_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync("invalid name!", "h1", 1, NoMeta, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_WithNullHolderId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync("sem1", null!, 1, NoMeta, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_WithZeroMaxCount_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync("sem1", "h1", 0, NoMeta, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_WithZeroSlotTimeout_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync("sem1", "h1", 1, NoMeta, TimeSpan.Zero));
    }
}


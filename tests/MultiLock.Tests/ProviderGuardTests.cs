using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MultiLock.AzureBlobStorage;
using MultiLock.Consul;
using MultiLock.PostgreSQL;
using MultiLock.Redis;
using MultiLock.SqlServer;
using MultiLock.ZooKeeper;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Shared guard-clause tests for every <see cref="ISemaphoreProvider"/> implementation. These cover
/// argument validation, disposal behaviour, and the health-check disposed-state path - all of which
/// run before any network access, so no live backend is required.
/// </summary>
public abstract class SemaphoreProviderGuardTestsBase
{
    private const string Sem = "sem1";
    private const string Holder = "holder-1";
    private const string BadName = "bad name!";
    private static readonly IReadOnlyDictionary<string, string> Meta = new Dictionary<string, string>();
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    /// <summary>Creates a provider configured with a valid but non-connecting backend.</summary>
    protected abstract ISemaphoreProvider CreateProvider();

    [Fact]
    public async Task TryAcquireAsync_WithInvalidSemaphoreName_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.TryAcquireAsync(BadName, Holder, 1, Meta, Timeout));
    }

    [Fact]
    public async Task TryAcquireAsync_WithInvalidHolderId_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.TryAcquireAsync(Sem, "  ", 1, Meta, Timeout));
    }

    [Fact]
    public async Task TryAcquireAsync_WithInvalidMaxCount_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.TryAcquireAsync(Sem, Holder, 0, Meta, Timeout));
    }

    [Fact]
    public async Task TryAcquireAsync_WithNullMetadata_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.TryAcquireAsync(Sem, Holder, 1, null!, Timeout));
    }

    [Fact]
    public async Task TryAcquireAsync_WithInvalidSlotTimeout_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.TryAcquireAsync(Sem, Holder, 1, Meta, TimeSpan.Zero));
    }

    [Fact]
    public async Task ReleaseAsync_WithInvalidSemaphoreName_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.ReleaseAsync(BadName, Holder));
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WithInvalidSemaphoreName_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.UpdateHeartbeatAsync(BadName, Holder, Meta));
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WithNullMetadata_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.UpdateHeartbeatAsync(Sem, Holder, null!));
    }

    [Fact]
    public async Task GetCurrentCountAsync_WithInvalidSemaphoreName_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.GetCurrentCountAsync(BadName, Timeout));
    }

    [Fact]
    public async Task GetHoldersAsync_WithInvalidSemaphoreName_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.GetHoldersAsync(BadName));
    }

    [Fact]
    public async Task IsHoldingAsync_WithInvalidHolderId_Throws()
    {
        using ISemaphoreProvider provider = CreateProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.IsHoldingAsync(Sem, ""));
    }

    [Fact]
    public async Task HealthCheckAsync_AfterDispose_ReturnsFalse()
    {
        ISemaphoreProvider provider = CreateProvider();
        provider.Dispose();
        (await provider.HealthCheckAsync()).ShouldBeFalse();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        ISemaphoreProvider provider = CreateProvider();
        Should.NotThrow(() =>
        {
            provider.Dispose();
            provider.Dispose();
        });
    }

    [Fact]
    public async Task Methods_AfterDispose_ThrowObjectDisposedException()
    {
        ISemaphoreProvider provider = CreateProvider();
        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.TryAcquireAsync(Sem, Holder, 1, Meta, Timeout));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.ReleaseAsync(Sem, Holder));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.UpdateHeartbeatAsync(Sem, Holder, Meta));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.GetCurrentCountAsync(Sem, Timeout));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.GetHoldersAsync(Sem));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.IsHoldingAsync(Sem, Holder));
    }
}

public class RedisSemaphoreProviderGuardTests : SemaphoreProviderGuardTestsBase
{
    protected override ISemaphoreProvider CreateProvider() =>
        new RedisSemaphoreProvider(
            Options.Create(new RedisSemaphoreOptions()),
            NullLogger<RedisSemaphoreProvider>.Instance);
}

public class AzureBlobStorageSemaphoreProviderGuardTests : SemaphoreProviderGuardTestsBase
{
    protected override ISemaphoreProvider CreateProvider() =>
        new AzureBlobStorageSemaphoreProvider(
            Options.Create(new AzureBlobStorageSemaphoreOptions { ConnectionString = "UseDevelopmentStorage=true" }),
            NullLogger<AzureBlobStorageSemaphoreProvider>.Instance);
}

public class ConsulSemaphoreProviderGuardTests : SemaphoreProviderGuardTestsBase
{
    protected override ISemaphoreProvider CreateProvider() =>
        new ConsulSemaphoreProvider(
            Options.Create(new ConsulSemaphoreOptions()),
            NullLogger<ConsulSemaphoreProvider>.Instance);
}

public class PostgreSqlSemaphoreProviderGuardTests : SemaphoreProviderGuardTestsBase
{
    protected override ISemaphoreProvider CreateProvider() =>
        new PostgreSqlSemaphoreProvider(
            Options.Create(new PostgreSqlSemaphoreOptions { ConnectionString = "Host=localhost;Database=db;Username=u;Password=p" }),
            NullLogger<PostgreSqlSemaphoreProvider>.Instance);
}

public class SqlServerSemaphoreProviderGuardTests : SemaphoreProviderGuardTestsBase
{
    protected override ISemaphoreProvider CreateProvider() =>
        new SqlServerSemaphoreProvider(
            Options.Create(new SqlServerSemaphoreOptions { ConnectionString = "Server=localhost;Database=db;Trusted_Connection=True" }),
            NullLogger<SqlServerSemaphoreProvider>.Instance);
}

public class ZooKeeperSemaphoreProviderGuardTests : SemaphoreProviderGuardTestsBase
{
    protected override ISemaphoreProvider CreateProvider() =>
        new ZooKeeperSemaphoreProvider(
            Options.Create(new ZooKeeperSemaphoreOptions()),
            NullLogger<ZooKeeperSemaphoreProvider>.Instance);
}

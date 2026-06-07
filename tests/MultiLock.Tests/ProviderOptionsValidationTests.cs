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
/// Validation tests for every provider's options type. These exercise the option <c>Validate()</c>
/// branches without requiring a live backend.
/// </summary>
public class ProviderOptionsValidationTests
{
    // ---- Redis ----

    [Fact]
    public void RedisOptions_Default_IsValid() =>
        Should.NotThrow(() => new RedisSemaphoreOptions().Validate());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RedisOptions_EmptyConnectionString_Throws(string connectionString) =>
        Should.Throw<ArgumentException>(() => new RedisSemaphoreOptions { ConnectionString = connectionString }.Validate());

    [Fact]
    public void RedisOptions_EmptyKeyPrefix_Throws() =>
        Should.Throw<ArgumentException>(() => new RedisSemaphoreOptions { KeyPrefix = "" }.Validate());

    [Fact]
    public void RedisOptions_NegativeDatabase_Throws() =>
        Should.Throw<ArgumentException>(() => new RedisSemaphoreOptions { Database = -1 }.Validate());

    // ---- Azure Blob Storage ----

    [Fact]
    public void AzureOptions_Valid_DoesNotThrow() =>
        Should.NotThrow(() => new AzureBlobStorageSemaphoreOptions { ConnectionString = "UseDevelopmentStorage=true" }.Validate());

    [Fact]
    public void AzureOptions_EmptyConnectionString_Throws() =>
        Should.Throw<ArgumentException>(() => new AzureBlobStorageSemaphoreOptions().Validate());

    [Fact]
    public void AzureOptions_EmptyContainerName_Throws() =>
        Should.Throw<ArgumentException>(() => new AzureBlobStorageSemaphoreOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = ""
        }.Validate());

    [Fact]
    public void AzureOptions_ZeroMaxRetryAttempts_Throws() =>
        Should.Throw<ArgumentException>(() => new AzureBlobStorageSemaphoreOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            MaxRetryAttempts = 0
        }.Validate());

    // ---- Consul ----

    [Fact]
    public void ConsulOptions_Default_IsValid() =>
        Should.NotThrow(() => new ConsulSemaphoreOptions().Validate());

    [Fact]
    public void ConsulOptions_EmptyAddress_Throws() =>
        Should.Throw<ArgumentException>(() => new ConsulSemaphoreOptions { Address = "" }.Validate());

    [Fact]
    public void ConsulOptions_EmptyKeyPrefix_Throws() =>
        Should.Throw<ArgumentException>(() => new ConsulSemaphoreOptions { KeyPrefix = "" }.Validate());

    [Theory]
    [InlineData(5)]      // below 10s minimum
    [InlineData(90000)]  // above 24h maximum
    public void ConsulOptions_SessionTtlOutOfRange_Throws(int seconds) =>
        Should.Throw<ArgumentException>(() => new ConsulSemaphoreOptions { SessionTtl = TimeSpan.FromSeconds(seconds) }.Validate());

    [Theory]
    [InlineData(-1)]   // negative
    [InlineData(120)]  // above 60s maximum
    public void ConsulOptions_SessionLockDelayOutOfRange_Throws(int seconds) =>
        Should.Throw<ArgumentException>(() => new ConsulSemaphoreOptions { SessionLockDelay = TimeSpan.FromSeconds(seconds) }.Validate());

    // ---- PostgreSQL ----

    [Fact]
    public void PostgreSqlOptions_Valid_DoesNotThrow() =>
        Should.NotThrow(() => new PostgreSqlSemaphoreOptions { ConnectionString = "Host=localhost;Database=db;Username=u;Password=p" }.Validate());

    [Fact]
    public void PostgreSqlOptions_EmptyConnectionString_Throws() =>
        Should.Throw<ArgumentException>(() => new PostgreSqlSemaphoreOptions().Validate());

    [Fact]
    public void PostgreSqlOptions_EmptyTableName_Throws() =>
        Should.Throw<ArgumentException>(() => new PostgreSqlSemaphoreOptions
        {
            ConnectionString = "Host=localhost",
            TableName = ""
        }.Validate());

    [Theory]
    [InlineData("bad-table")]      // hyphen not allowed in a SQL identifier
    [InlineData("1table")]         // cannot start with a digit
    [InlineData("table;drop")]     // injection attempt
    public void PostgreSqlOptions_InvalidIdentifier_Throws(string tableName) =>
        Should.Throw<ArgumentException>(() => new PostgreSqlSemaphoreOptions
        {
            ConnectionString = "Host=localhost",
            TableName = tableName
        }.Validate());

    [Fact]
    public void PostgreSqlOptions_NonPositiveCommandTimeout_Throws() =>
        Should.Throw<ArgumentException>(() => new PostgreSqlSemaphoreOptions
        {
            ConnectionString = "Host=localhost",
            CommandTimeoutSeconds = 0
        }.Validate());

    // ---- SQL Server ----

    [Fact]
    public void SqlServerOptions_Valid_DoesNotThrow() =>
        Should.NotThrow(() => new SqlServerSemaphoreOptions { ConnectionString = "Server=localhost;Database=db;Trusted_Connection=True" }.Validate());

    [Fact]
    public void SqlServerOptions_EmptyConnectionString_Throws() =>
        Should.Throw<ArgumentException>(() => new SqlServerSemaphoreOptions().Validate());

    [Fact]
    public void SqlServerOptions_EmptySchemaName_Throws() =>
        Should.Throw<ArgumentException>(() => new SqlServerSemaphoreOptions
        {
            ConnectionString = "Server=localhost",
            SchemaName = ""
        }.Validate());

    [Theory]
    [InlineData("dbo;drop")]
    [InlineData("9schema")]
    public void SqlServerOptions_InvalidIdentifier_Throws(string schemaName) =>
        Should.Throw<ArgumentException>(() => new SqlServerSemaphoreOptions
        {
            ConnectionString = "Server=localhost",
            SchemaName = schemaName
        }.Validate());

    [Fact]
    public void SqlServerOptions_NegativeCommandTimeout_Throws() =>
        Should.Throw<ArgumentException>(() => new SqlServerSemaphoreOptions
        {
            ConnectionString = "Server=localhost",
            CommandTimeoutSeconds = -5
        }.Validate());

    // ---- ZooKeeper ----

    [Fact]
    public void ZooKeeperOptions_Default_IsValid() =>
        Should.NotThrow(() => new ZooKeeperSemaphoreOptions().Validate());

    [Fact]
    public void ZooKeeperOptions_EmptyConnectionString_Throws() =>
        Should.Throw<ArgumentException>(() => new ZooKeeperSemaphoreOptions { ConnectionString = "" }.Validate());

    [Fact]
    public void ZooKeeperOptions_RootPathWithoutLeadingSlash_Throws() =>
        Should.Throw<ArgumentException>(() => new ZooKeeperSemaphoreOptions { RootPath = "semaphores" }.Validate());

    [Theory]
    [InlineData(0)]      // non-positive
    [InlineData(4000)]   // above 60 minute maximum
    public void ZooKeeperOptions_SessionTimeoutOutOfRange_Throws(int seconds) =>
        Should.Throw<ArgumentException>(() => new ZooKeeperSemaphoreOptions { SessionTimeout = TimeSpan.FromSeconds(seconds) }.Validate());

    [Theory]
    [InlineData(0)]      // non-positive
    [InlineData(1200)]   // above 10 minute maximum
    public void ZooKeeperOptions_ConnectionTimeoutOutOfRange_Throws(int seconds) =>
        Should.Throw<ArgumentException>(() => new ZooKeeperSemaphoreOptions { ConnectionTimeout = TimeSpan.FromSeconds(seconds) }.Validate());
}

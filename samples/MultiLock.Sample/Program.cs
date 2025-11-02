using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiLock.AzureBlobStorage;
using MultiLock.Consul;
using MultiLock.FileSystem;
using MultiLock.InMemory;
using MultiLock.PostgreSQL;
using MultiLock.Redis;
using MultiLock.SqlServer;
using MultiLock.ZooKeeper;
using MultiLock.Sample;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Choose provider based on command line argument
string providerType = args.Length > 0 ? args[0].ToLower() : "inmemory";

switch (providerType)
{
    case "inmemory":
        builder.Services.AddInMemoryLeaderElection(options =>
        {
            options.ElectionGroup = "sample-service";
            options.HeartbeatInterval = TimeSpan.FromSeconds(10);
            options.HeartbeatTimeout = TimeSpan.FromSeconds(30);
            options.EnableDetailedLogging = true;
        });
        break;

    case "filesystem":
        builder.Services.AddFileSystemLeaderElection(
            Path.Combine(Path.GetTempPath(), "LeaderElectionSample"),
            options =>
            {
                options.ElectionGroup = "sample-service";
                options.HeartbeatInterval = TimeSpan.FromSeconds(10);
                options.HeartbeatTimeout = TimeSpan.FromSeconds(30);
                options.EnableDetailedLogging = true;
            });
        break;

    case "sqlserver":
        string connectionString = args.Length > 1 ? args[1] :
            "Server=localhost;Database=LeaderElectionSample;Trusted_Connection=true;TrustServerCertificate=true;";
        builder.Services.AddSqlServerLeaderElection(connectionString, options =>
        {
            options.ElectionGroup = "sample-service";
            options.HeartbeatInterval = TimeSpan.FromSeconds(10);
            options.HeartbeatTimeout = TimeSpan.FromSeconds(30);
            options.EnableDetailedLogging = true;
        });
        break;

    case "azureblob":
        string storageConnectionString = args.Length > 1 ? args[1] : "UseDevelopmentStorage=true";
        builder.Services.AddAzureBlobStorageLeaderElection(storageConnectionString, options =>
        {
            options.ElectionGroup = "sample-service";
            options.HeartbeatInterval = TimeSpan.FromSeconds(10);
            options.HeartbeatTimeout = TimeSpan.FromSeconds(30);
            options.EnableDetailedLogging = true;
        });
        break;

    case "redis":
        string redisConnectionString = args.Length > 1 ? args[1] : "localhost:6379";
        builder.Services.AddRedisLeaderElection(redisConnectionString, options =>
        {
            options.ElectionGroup = "sample-service";
            options.HeartbeatInterval = TimeSpan.FromSeconds(10);
            options.HeartbeatTimeout = TimeSpan.FromSeconds(30);
            options.EnableDetailedLogging = true;
        });
        break;

    case "consul":
        string consulAddress = args.Length > 1 ? args[1] : "http://localhost:8500";
        builder.Services.AddConsulLeaderElection(consulAddress, options =>
        {
            options.ElectionGroup = "sample-service";
            options.HeartbeatInterval = TimeSpan.FromSeconds(10);
            options.HeartbeatTimeout = TimeSpan.FromSeconds(30);
            options.EnableDetailedLogging = true;
        });
        break;

    case "postgresql":
        string postgresConnectionString = args.Length > 1 ? args[1] :
            "Host=localhost;Database=leaderelection;Username=leaderelection;Password=leaderelection123";
        builder.Services.AddPostgreSqlLeaderElection(postgresConnectionString, options =>
        {
            options.ElectionGroup = "sample-service";
            options.HeartbeatInterval = TimeSpan.FromSeconds(10);
            options.HeartbeatTimeout = TimeSpan.FromSeconds(30);
            options.EnableDetailedLogging = true;
        });
        break;

    case "zookeeper":
        string zookeeperConnectionString = args.Length > 1 ? args[1] : "localhost:2181";
        builder.Services.AddZooKeeperLeaderElection(zookeeperConnectionString, options =>
        {
            options.ElectionGroup = "sample-service";
            options.HeartbeatInterval = TimeSpan.FromSeconds(10);
            options.HeartbeatTimeout = TimeSpan.FromSeconds(30);
            options.EnableDetailedLogging = true;
        });
        break;

    default:
        Console.WriteLine("Available providers:");
        Console.WriteLine("  inmemory    - In-memory provider (default)");
        Console.WriteLine("  filesystem  - File system provider");
        Console.WriteLine("  sqlserver   - SQL Server provider [connection_string]");
        Console.WriteLine("  postgresql  - PostgreSQL provider [connection_string]");
        Console.WriteLine("  azureblob   - Azure Blob Storage provider [connection_string]");
        Console.WriteLine("  redis       - Redis provider [connection_string]");
        Console.WriteLine("  consul      - Consul provider [address]");
        Console.WriteLine("  zookeeper   - ZooKeeper provider [connection_string]");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run [provider] [connection_string_or_address]");
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run inmemory");
        Console.WriteLine("  dotnet run filesystem");
        Console.WriteLine("  dotnet run sqlserver \"Server=localhost;Database=Test;Trusted_Connection=true;\"");
        Console.WriteLine("  dotnet run postgresql \"Host=localhost;Database=leaderelection;Username=user;Password=pass\"");
        Console.WriteLine("  dotnet run azureblob \"UseDevelopmentStorage=true\"");
        Console.WriteLine("  dotnet run redis \"localhost:6379\"");
        Console.WriteLine("  dotnet run consul \"http://localhost:8500\"");
        Console.WriteLine("  dotnet run zookeeper \"localhost:2181\"");
        return;
}

// Add the sample background service
builder.Services.AddHostedService<SampleBackgroundService>();

IHost host = builder.Build();

Console.WriteLine($"Starting Leader Election Sample with {providerType} provider...");
Console.WriteLine("Press Ctrl+C to exit");
Console.WriteLine();

await host.RunAsync();

namespace MultiLock.Sample
{
}

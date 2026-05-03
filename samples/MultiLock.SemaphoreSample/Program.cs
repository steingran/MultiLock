using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiLock;
using MultiLock.AzureBlobStorage;
using MultiLock.Consul;
using MultiLock.FileSystem;
using MultiLock.InMemory;
using MultiLock.PostgreSQL;
using MultiLock.Redis;
using MultiLock.SemaphoreSample;
using MultiLock.SqlServer;
using MultiLock.ZooKeeper;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Choose provider based on command line argument
string providerType = args.Length > 0 ? args[0].ToLowerInvariant() : "inmemory";
int maxConcurrent = args.Length > 1 && int.TryParse(args[1], out int max) && max > 0 ? max : 3;

Action<SemaphoreOptions> configureSemaphore = options =>
{
    options.SemaphoreName = "rate-limiter";
    options.MaxCount = maxConcurrent;
    options.HeartbeatInterval = TimeSpan.FromSeconds(10);
    options.HeartbeatTimeout = TimeSpan.FromSeconds(30);
    options.EnableDetailedLogging = true;
};

switch (providerType)
{
    case "inmemory":
        builder.Services.AddInMemorySemaphore(configureSemaphore);
        break;

    case "filesystem":
        builder.Services.AddFileSystemSemaphore(
            Path.Combine(Path.GetTempPath(), "SemaphoreSample"),
            configureSemaphore);
        break;

    case "sqlserver":
        string sqlConnectionString = args.Length > 2 ? args[2] :
            "Server=localhost;Database=SemaphoreSample;Trusted_Connection=true;TrustServerCertificate=true;";
        builder.Services.AddSqlServerSemaphore(sqlConnectionString, configureSemaphore);
        break;

    case "azureblob":
        string storageConnectionString = args.Length > 2 ? args[2] : "UseDevelopmentStorage=true";
        builder.Services.AddAzureBlobStorageSemaphore(storageConnectionString, configureSemaphore);
        break;

    case "redis":
        string redisConnectionString = args.Length > 2 ? args[2] : "localhost:6379";
        builder.Services.AddRedisSemaphore(redisConnectionString, configureSemaphore);
        break;

    case "consul":
        string consulAddress = args.Length > 2 ? args[2] : "http://localhost:8500";
        builder.Services.AddConsulSemaphore(consulAddress, configureSemaphore);
        break;

    case "postgresql":
        string postgresConnectionString = args.Length > 2 ? args[2] :
            "Host=localhost;Database=semaphore;Username=postgres;Password=YOUR_PASSWORD_HERE";
        builder.Services.AddPostgreSqlSemaphore(postgresConnectionString, configureSemaphore);
        break;

    case "zookeeper":
        string zookeeperConnectionString = args.Length > 2 ? args[2] : "localhost:2181";
        builder.Services.AddZooKeeperSemaphore(zookeeperConnectionString, configureSemaphore);
        break;

    default:
        Console.WriteLine("Distributed Semaphore Sample - Rate Limiting Demo");
        Console.WriteLine();
        Console.WriteLine("Available providers:");
        Console.WriteLine("  inmemory    - In-memory provider (default)");
        Console.WriteLine("  filesystem  - File system provider");
        Console.WriteLine("  sqlserver   - SQL Server provider");
        Console.WriteLine("  postgresql  - PostgreSQL provider");
        Console.WriteLine("  azureblob   - Azure Blob Storage provider");
        Console.WriteLine("  redis       - Redis provider");
        Console.WriteLine("  consul      - Consul provider");
        Console.WriteLine("  zookeeper   - ZooKeeper provider");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run [provider] [max_concurrent] [connection_string]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run inmemory 5");
        Console.WriteLine("  dotnet run redis 3 localhost:6379");
        Console.WriteLine("  dotnet run postgresql 5 \"Host=localhost;Database=test;...\"");
        return;
}

// Add the sample background service that simulates rate-limited work
builder.Services.AddHostedService<RateLimitedWorkerService>();

IHost host = builder.Build();

Console.WriteLine($"Starting Semaphore Sample with {providerType} provider (max {maxConcurrent} concurrent)...");
Console.WriteLine("This demo simulates rate-limited API calls using distributed semaphores.");
Console.WriteLine("Press Ctrl+C to exit");
Console.WriteLine();

await host.RunAsync();



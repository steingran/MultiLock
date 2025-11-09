# MultiLock

[![codecov](https://codecov.io/gh/steingran/MultiLock/branch/main/graph/badge.svg)](https://codecov.io/gh/steingran/MultiLock)

A comprehensive .NET framework for implementing the Leader Election pattern with support for multiple providers including Azure Blob Storage, SQL Server, Redis, File System, In-Memory, Consul, and ZooKeeper.

## Architecture Overview

```mermaid
graph TB
    subgraph "Core Framework"
        ILE[ILeaderElectionService]
        LES[LeaderElectionService]
        ILP[ILeaderElectionProvider]
        LI[LeaderInfo]
        EX[Exceptions]
    end

    subgraph "Providers"
        IM[InMemory]
        FS[FileSystem]
        SQL[SQL Server]
        ABS[Azure Blob]
        RD[Redis]
        CS[Consul]
        ZK[ZooKeeper]
        PG[PostgreSQL]
    end

    subgraph "Applications"
        APP1[Sample App]
        APP2[Multi-Provider Demo]
        APP3[Your Application]
    end

    ILE --> LES
    LES --> ILP
    ILP --> IM
    ILP --> FS
    ILP --> SQL
    ILP --> ABS
    ILP --> RD
    ILP --> CS
    ILP --> ZK
    ILP --> PG

    APP1 --> ILE
    APP2 --> ILE
    APP3 --> ILE

    style ILE fill:#e1f5fe
    style LES fill:#e8f5e8
    style ILP fill:#fff3e0
    style IM fill:#f3e5f5
    style FS fill:#f3e5f5
    style SQL fill:#f3e5f5
    style ABS fill:#f3e5f5
    style RD fill:#f3e5f5
    style CS fill:#f3e5f5
    style ZK fill:#ffebee
    style PG fill:#ffebee
```

## Features

- **Multiple Providers**: Support for various storage backends
- **Distributed Mutex**: First-to-acquire becomes leader strategy
- **Heartbeat Monitoring**: Automatic leader health monitoring and failover
- **Resilient Design**: Handles transient and persistent failures gracefully
- **Thread-Safe**: All operations are thread-safe
- **Configurable**: Extensive configuration options for timeouts, retry policies, etc.
- **Dependency Injection**: Full support for .NET dependency injection
- **Comprehensive Logging**: Built-in logging and telemetry support

## Supported Providers

| Provider | Package | Description |
|----------|---------|-------------|
| Azure Blob Storage | `MultiLock.AzureBlobStorage` | Uses Azure Blob Storage for coordination |
| SQL Server | `MultiLock.SqlServer` | Uses SQL Server database for coordination |
| PostgreSQL | `MultiLock.PostgreSQL` | Uses PostgreSQL database for coordination |
| Redis | `MultiLock.Redis` | Uses Redis for coordination |
| File System | `MultiLock.FileSystem` | Uses local file system (single machine) |
| In-Memory | `MultiLock.InMemory` | In-memory provider for testing |
| Consul | `MultiLock.Consul` | Uses HashiCorp Consul for coordination |
| ZooKeeper | `MultiLock.ZooKeeper` | Uses Apache ZooKeeper for coordination |

## Quick Start

### 1. Install a Provider Package

```bash
# For SQL Server
dotnet add package MultiLock.SqlServer

# For Redis
dotnet add package MultiLock.Redis

# For PostgreSQL
dotnet add package MultiLock.PostgreSQL

# For In-Memory (testing)
dotnet add package MultiLock.InMemory
```

### 2. Configure Services

```csharp
using MultiLock;
using MultiLock.InMemory;

var builder = WebApplication.CreateBuilder(args);

// Add leader election with In-Memory provider
builder.Services.AddLeaderElection<InMemoryLeaderElectionProvider>(options =>
{
    options.ElectionGroup = "my-service";
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
    options.HeartbeatTimeout = TimeSpan.FromSeconds(90);
});

var app = builder.Build();
```

### 3. Use in Your Service

```csharp
public class MyBackgroundService : BackgroundService
{
    private readonly ILeaderElectionService _leaderElection;
    private readonly ILogger<MyBackgroundService> _logger;

    public MyBackgroundService(
        ILeaderElectionService leaderElection,
        ILogger<MyBackgroundService> logger)
    {
        _leaderElection = leaderElection;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start listening to leadership changes using AsyncEnumerable
        var leadershipTask = Task.Run(async () =>
        {
            await foreach (var change in _leaderElection.GetLeadershipChangesAsync(stoppingToken))
            {
                if (change.BecameLeader)
                {
                    _logger.LogInformation("🎉 Leadership acquired!");
                }
                else if (change.LostLeadership)
                {
                    _logger.LogWarning("😞 Leadership lost!");
                }
            }
        }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_leaderElection.IsLeader)
            {
                // Perform leader-only work
                _logger.LogInformation("🏆 Performing leader work...");
                await DoLeaderWork(stoppingToken);
            }
            else
            {
                // Perform follower work or wait
                _logger.LogInformation("👥 Waiting as follower...");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        // Wait for leadership monitoring to complete
        try
        {
            await leadershipTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
    }

    private async Task DoLeaderWork(CancellationToken cancellationToken)
    {
        // Your leader-specific logic here
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }
}
```

## AsyncEnumerable API

The leader election service provides a modern AsyncEnumerable API for consuming leadership change events:

### Basic Usage

```csharp
// Subscribe to all leadership changes
await foreach (var change in leaderElection.GetLeadershipChangesAsync(cancellationToken))
{
    if (change.BecameLeader)
    {
        Console.WriteLine("I am now the leader!");
    }
    else if (change.LostLeadership)
    {
        Console.WriteLine("I lost leadership!");
    }
}
```

### Filtering Events

```csharp
// Only receive leadership acquired events
await foreach (var change in leaderElection.GetLeadershipChangesAsync(
    LeadershipEventType.Acquired, cancellationToken))
{
    Console.WriteLine("Leadership acquired!");
}

// Receive both acquired and lost events
await foreach (var change in leaderElection.GetLeadershipChangesAsync(
    LeadershipEventType.Acquired | LeadershipEventType.Lost, cancellationToken))
{
    // Handle events
}
```

### Extension Methods

The framework provides powerful LINQ-style extension methods:

```csharp
// Take only the first leadership acquisition
await foreach (var change in leaderElection.GetLeadershipChangesAsync(cancellationToken)
    .TakeUntilLeader(cancellationToken))
{
    Console.WriteLine("Waiting for leadership...");
}

// Process events while we are the leader
await foreach (var change in leaderElection.GetLeadershipChangesAsync(cancellationToken)
    .WhileLeader(cancellationToken))
{
    Console.WriteLine("Still the leader!");
}

// Execute callbacks on leadership transitions
await leaderElection.GetLeadershipChangesAsync(cancellationToken)
    .OnLeadershipTransition(
        onAcquired: e => Console.WriteLine("Acquired!"),
        onLost: e => Console.WriteLine("Lost!"),
        cancellationToken);

// Combine multiple extension methods
await foreach (var change in leaderElection.GetLeadershipChangesAsync(cancellationToken)
    .Where(e => e.BecameLeader || e.LostLeadership, cancellationToken)
    .DistinctUntilChanged(cancellationToken)
    .Take(5, cancellationToken))
{
    // Process filtered events
}
```

## Concurrency and Thread-Safety

### Thread-Safety Guarantees

The MultiLock framework is designed to be fully thread-safe and handle concurrent operations gracefully:

#### Core Service Thread-Safety

- **All public methods are thread-safe**: You can safely call `StartAsync()`, `StopAsync()`, `IsLeaderAsync()`, and other methods from multiple threads concurrently.
- **Internal state protection**: All internal state modifications are protected by appropriate synchronization mechanisms (locks, atomic operations, etc.).
- **Safe disposal**: The service implements both `IDisposable` and `IAsyncDisposable` with proper coordination to prevent race conditions during shutdown.
- **Callback coordination**: Timer callbacks (heartbeat and election timers) are coordinated with disposal to prevent `ObjectDisposedException` and ensure clean shutdown.

#### Provider Thread-Safety

All provider implementations are thread-safe and support concurrent operations:

- **Concurrent leadership acquisition**: Multiple participants can safely attempt to acquire leadership simultaneously - only one will succeed.
- **Concurrent heartbeat updates**: Multiple threads can safely update heartbeats for the same participant.
- **Concurrent metadata updates**: Metadata can be safely updated from multiple threads.
- **Concurrent queries**: Methods like `GetCurrentLeaderAsync()` and `IsLeaderAsync()` can be called concurrently without issues.

### Concurrency Scenarios

The framework has been extensively tested for various concurrent scenarios:

#### Multiple Participants Competing

When multiple participants attempt to acquire leadership simultaneously:

```csharp
// Safe to run from multiple instances/threads
var tasks = Enumerable.Range(0, 10).Select(async i =>
{
    var service = serviceProvider.GetRequiredService<ILeaderElectionService>();
    await service.StartAsync();
    // Only one will become leader
}).ToArray();

await Task.WhenAll(tasks);
```

Exactly one participant will become the leader. All others will fail gracefully and continue monitoring for leadership opportunities.

#### Rapid Acquire/Release Cycles

The framework handles rapid leadership transitions without race conditions:

```csharp
// Safe to rapidly acquire and release leadership
for (int i = 0; i < 100; i++)
{
    await service.StartAsync();
    await Task.Delay(10);
    await service.StopAsync();
}
```

Each cycle completes cleanly without resource leaks or state corruption.

#### Concurrent Heartbeat Updates

Multiple threads can safely update heartbeats:

```csharp
// Safe concurrent heartbeat updates
var tasks = Enumerable.Range(0, 50).Select(async i =>
{
    await provider.UpdateHeartbeatAsync("election-group", "participant-id");
}).ToArray();

await Task.WhenAll(tasks);
```

All heartbeat updates are processed atomically. The last update wins.

#### Leader Failover Race Conditions

When a leader fails and multiple participants compete to take over:

```csharp
// Current leader releases leadership
await currentLeader.StopAsync();

// Multiple waiting participants compete
// Only one will successfully acquire leadership
```

Clean failover without split-brain scenarios. Exactly one new leader is elected.

### Best Practices for Concurrent Usage

1. **Use dependency injection**: Register the service as a singleton to ensure a single instance per application.

```csharp
services.AddSingleton<ILeaderElectionService, LeaderElectionService>();
```

2. **Avoid manual synchronization**: The framework handles all necessary synchronization internally. Don't wrap calls in your own locks.

3. **Use async disposal**: When shutting down, prefer `DisposeAsync()` over `Dispose()` for providers that support it (Consul, ZooKeeper).

```csharp
await using var provider = new ConsulLeaderElectionProvider(options, logger);
// Provider will be properly disposed asynchronously
```

4. **Handle cancellation properly**: Always pass `CancellationToken` to async methods and handle `OperationCanceledException`.

```csharp
try
{
    await service.StartAsync(cancellationToken);
}
catch (OperationCanceledException)
{
    // Clean shutdown requested
}
```

5. **Monitor leadership changes**: Use the `GetLeadershipChangesAsync()` API to react to leadership transitions rather than polling.

```csharp
await foreach (var change in service.GetLeadershipChangesAsync(cancellationToken))
{
    if (change.BecameLeader)
    {
        // Start leader-only work
    }
    else if (change.LostLeadership)
    {
        // Stop leader-only work
    }
}
```

### Provider-Specific Concurrency Considerations

#### Database Providers (PostgreSQL, SQL Server)

- Use atomic database operations (INSERT ... ON CONFLICT, MERGE) to prevent race conditions
- Connection pooling is handled automatically by the database client libraries
- No risk of deadlocks - all operations use optimistic concurrency control

#### Redis Provider

- Uses atomic Redis commands (SET with NX and EX options)
- Connection multiplexing is handled by StackExchange.Redis
- No connection pool exhaustion under high concurrency

#### File System Provider

- Uses file locking for mutual exclusion
- **Note**: Only suitable for single-machine scenarios
- File locks are automatically released on process termination

#### Consul Provider

- Uses Consul sessions for distributed locking
- Session TTL ensures automatic cleanup on failure
- Supports high concurrency across distributed systems

#### ZooKeeper Provider

- Uses ZooKeeper ephemeral sequential nodes
- Automatic cleanup when client disconnects
- Handles network partitions gracefully

## Provider-Specific Configuration

### SQL Server Provider

```csharp
builder.Services.AddSqlServerLeaderElection(
    connectionString: "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    options =>
    {
        options.ElectionGroup = "my-service";
        options.HeartbeatInterval = TimeSpan.FromSeconds(30);
        options.HeartbeatTimeout = TimeSpan.FromSeconds(90);
    });
```

### PostgreSQL Provider

```csharp
builder.Services.AddPostgreSQLLeaderElection(
    connectionString: "Host=localhost;Database=MyApp;Username=user;Password=password;",
    options =>
    {
        options.ElectionGroup = "my-service";
        options.HeartbeatInterval = TimeSpan.FromSeconds(30);
        options.HeartbeatTimeout = TimeSpan.FromSeconds(90);
    });
```

### Redis Provider

```csharp
builder.Services.AddRedisLeaderElection(
    connectionString: "localhost:6379",
    options =>
    {
        options.ElectionGroup = "my-service";
        options.HeartbeatInterval = TimeSpan.FromSeconds(30);
        options.HeartbeatTimeout = TimeSpan.FromSeconds(90);
    });
```

### Azure Blob Storage Provider

```csharp
builder.Services.AddAzureBlobStorageLeaderElection(
    connectionString: "DefaultEndpointsProtocol=https;AccountName=...",
    options =>
    {
        options.ElectionGroup = "my-service";
        options.HeartbeatInterval = TimeSpan.FromSeconds(30);
        options.HeartbeatTimeout = TimeSpan.FromSeconds(90);
    });
```

### Consul Provider

```csharp
builder.Services.AddConsulLeaderElection(
    address: "http://localhost:8500",
    options =>
    {
        options.ElectionGroup = "my-service";
        options.HeartbeatInterval = TimeSpan.FromSeconds(30);
        options.HeartbeatTimeout = TimeSpan.FromSeconds(90);
    });
```

### ZooKeeper Provider

```csharp
builder.Services.AddZooKeeperLeaderElection(
    connectionString: "localhost:2181",
    options =>
    {
        options.ElectionGroup = "my-service";
        options.HeartbeatInterval = TimeSpan.FromSeconds(30);
        options.HeartbeatTimeout = TimeSpan.FromSeconds(90);
    });
```

#### Advanced Configuration Examples

**PostgreSQL with Custom Schema and Table:**
```csharp
builder.Services.AddPostgreSQLLeaderElection(options =>
{
    options.ConnectionString = "Host=localhost;Database=myapp;Username=user;Password=pass";
    options.TableName = "custom_leader_election";
    options.SchemaName = "leader_election";
    options.AutoCreateTable = true;
    options.CommandTimeoutSeconds = 60;
}, leaderElectionOptions =>
{
    leaderElectionOptions.ElectionGroup = "my-service";
    leaderElectionOptions.ParticipantId = Environment.MachineName;
    leaderElectionOptions.HeartbeatInterval = TimeSpan.FromSeconds(15);
    leaderElectionOptions.HeartbeatTimeout = TimeSpan.FromSeconds(45);
    leaderElectionOptions.EnableDetailedLogging = true;
});
```

**ZooKeeper with Custom Session Settings:**
```csharp
builder.Services.AddZooKeeperLeaderElection(options =>
{
    options.ConnectionString = "zk1:2181,zk2:2181,zk3:2181";
    options.RootPath = "/my-app/leader-election";
    options.SessionTimeout = TimeSpan.FromSeconds(30);
    options.ConnectionTimeout = TimeSpan.FromSeconds(10);
    options.MaxRetries = 5;
    options.RetryDelay = TimeSpan.FromSeconds(2);
    options.AutoCreateRootPath = true;
}, leaderElectionOptions =>
{
    leaderElectionOptions.ElectionGroup = "my-service";
    leaderElectionOptions.ParticipantId = $"{Environment.MachineName}-{Environment.ProcessId}";
    leaderElectionOptions.HeartbeatInterval = TimeSpan.FromSeconds(10);
    leaderElectionOptions.HeartbeatTimeout = TimeSpan.FromSeconds(30);
});
```

## Key Concepts

### Leader Election Process

1. **Election**: Multiple instances compete to become the leader
2. **Leadership**: One instance becomes the leader and performs exclusive work
3. **Heartbeat**: The leader sends periodic heartbeats to maintain leadership
4. **Failover**: If the leader fails, a new election occurs automatically

### Leadership Change Events

The service provides an AsyncEnumerable API for observing leadership changes:

- **Acquired**: Emitted when the current instance becomes the leader
- **Lost**: Emitted when the current instance loses leadership
- **Changed**: Emitted on any leadership status change (including when another participant becomes leader)

Use `GetLeadershipChangesAsync()` to subscribe to these events and optionally filter by event type using `LeadershipEventType` flags.

### Configuration Options

- **ElectionGroup**: Logical group name for the election (multiple services can have different groups)
- **ParticipantId**: Unique identifier for this instance (auto-generated if not specified)
- **HeartbeatInterval**: How often the leader sends heartbeats
- **HeartbeatTimeout**: How long to wait before considering a leader dead
- **ElectionInterval**: How often followers attempt to become leader
- **LockTimeout**: Maximum time to hold a lock during election
- **AutoStart**: Whether to automatically start the election process

## Testing

The framework includes comprehensive test coverage:

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/MultiLock.Tests/
dotnet test tests/MultiLock.IntegrationTests/
```

## Samples

Check out the sample applications:

- **Basic Sample**: `samples/MultiLock.Sample/` - Single provider demonstration
- **Multi-Provider Demo**: `samples/MultiLock.MultiProvider/` - Multiple instances competing

```bash
# Run the basic sample with different providers
cd samples/MultiLock.Sample
dotnet run inmemory
dotnet run filesystem
dotnet run sqlserver "Server=localhost;Database=Test;Trusted_Connection=true;"
dotnet run postgresql "Host=localhost;Database=leaderelection;Username=user;Password=pass"
dotnet run redis "localhost:6379"
dotnet run consul "http://localhost:8500"
dotnet run zookeeper "localhost:2181"

# Run the multi-provider demo
cd samples/MultiLock.MultiProvider
dotnet run
```

## Testing

The project includes comprehensive unit tests and integration tests. Integration tests use Docker containers to test against real services.

```bash
# Run unit tests
dotnet test tests/MultiLock.Tests/

# Run integration tests (requires Docker)
docker-compose up -d
dotnet test tests/MultiLock.IntegrationTests/
docker-compose down
```

### Testing with Docker Compose

The included `docker-compose.yml` file provides all the necessary services for testing:

- **PostgreSQL** (port 5432) - For PostgreSQL provider testing
- **ZooKeeper** (port 2181) - For ZooKeeper provider testing
- **Redis** (port 6379) - For Redis provider testing
- **Consul** (port 8500) - For Consul provider testing
- **Azurite** (ports 10000-10002) - For Azure Blob Storage provider testing
- **SQL Server** (port 1433) - For SQL Server provider testing

```bash
# Start all services
docker-compose up -d

# Check service health
docker-compose ps

# View logs for a specific service
docker-compose logs postgres
docker-compose logs zookeeper

# Stop all services
docker-compose down

# Stop and remove volumes (clean slate)
docker-compose down -v
```

## Docker Support

The framework works seamlessly in containerized environments. See `docker-compose.yml` for examples of running with various backing services.

## Contributing

Contributions are welcome! Please see our contributing guidelines and code of conduct.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
```

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiLock.FileSystem;
using MultiLock.InMemory;
using MultiLock.MultiProvider;

Console.WriteLine("=== MultiLock Multi-Provider Demo ===");
Console.WriteLine();
Console.WriteLine("This demo shows multiple instances competing for leadership");
Console.WriteLine("using different providers simultaneously.");
Console.WriteLine();

// Create multiple instances with different providers
var tasks = new List<Task>();
var cancellationTokenSource = new CancellationTokenSource();

// Instance 1: In-Memory Provider
tasks.Add(RunInstanceAsync("InMemory-1", CreateInMemoryHost(), cancellationTokenSource.Token));

// Instance 2: In-Memory Provider (same provider, different instance)
tasks.Add(RunInstanceAsync("InMemory-2", CreateInMemoryHost(), cancellationTokenSource.Token));

// Instance 3: File System Provider
tasks.Add(RunInstanceAsync("FileSystem-1", CreateFileSystemHost(), cancellationTokenSource.Token));

// Instance 4: File System Provider (same provider, different instance)
tasks.Add(RunInstanceAsync("FileSystem-2", CreateFileSystemHost(), cancellationTokenSource.Token));

Console.WriteLine("Starting 4 instances...");
Console.WriteLine("Press any key to stop all instances");
Console.WriteLine();

// Start all instances
var allTasks = Task.WhenAll(tasks);

// Wait for user input
await Task.Run(Console.ReadKey);

Console.WriteLine();
Console.WriteLine("Stopping all instances...");

// Cancel all instances
cancellationTokenSource.Cancel();

try
{
    await allTasks;
}
catch (OperationCanceledException)
{
    // Expected when cancelling
}

Console.WriteLine("All instances stopped.");
return;

static IHost CreateInMemoryHost()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    builder.Services.AddInMemoryLeaderElection(options =>
    {
        options.ElectionGroup = "multi-provider-demo";
        options.HeartbeatInterval = TimeSpan.FromSeconds(5);
        options.HeartbeatTimeout = TimeSpan.FromSeconds(15);
        options.ElectionInterval = TimeSpan.FromSeconds(2);
        options.EnableDetailedLogging = false;
    });

    builder.Services.AddHostedService<DemoBackgroundService>();

    return builder.Build();
}

static IHost CreateFileSystemHost()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    builder.Services.AddFileSystemLeaderElection(
        Path.Combine(Path.GetTempPath(), "MultiLock-MultiProviderDemo"),
        options =>
        {
            options.ElectionGroup = "multi-provider-demo-fs";
            options.HeartbeatInterval = TimeSpan.FromSeconds(5);
            options.HeartbeatTimeout = TimeSpan.FromSeconds(15);
            options.ElectionInterval = TimeSpan.FromSeconds(2);
            options.EnableDetailedLogging = false;
        });

    builder.Services.AddHostedService<DemoBackgroundService>();

    return builder.Build();
}

static async Task RunInstanceAsync(string instanceName, IHost host, CancellationToken cancellationToken)
{
    try
    {
        Console.WriteLine($"[{instanceName}] Starting...");
        await host.RunAsync(cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // Expected when stopping
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{instanceName}] Error: {ex.Message}");
    }
    finally
    {
        Console.WriteLine($"[{instanceName}] Stopped.");
        host.Dispose();
    }
}

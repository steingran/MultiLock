using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiLock.Net;
using MultiLock.Net.InMemory;
using MultiLock.Net.FileSystem;

Console.WriteLine("=== LeaderElection.Net Multi-Provider Demo ===");
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
await Task.Run(() => Console.ReadKey());

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

static IHost CreateInMemoryHost()
{
    var builder = Host.CreateApplicationBuilder();
    
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
    var builder = Host.CreateApplicationBuilder();
    
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    builder.Services.AddFileSystemLeaderElection(
        Path.Combine(Path.GetTempPath(), "LeaderElectionMultiProviderDemo"),
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

/// <summary>
/// Demo background service that shows leadership status using the AsyncEnumerable API.
/// </summary>
public class DemoBackgroundService : BackgroundService
{
    private readonly ILeaderElectionService _leaderElection;
    private readonly ILogger<DemoBackgroundService> _logger;

    public DemoBackgroundService(
        ILeaderElectionService leaderElection,
        ILogger<DemoBackgroundService> logger)
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
                    _logger.LogInformation("🎉 [{ParticipantId}] Leadership acquired!",
                        _leaderElection.ParticipantId);
                }
                else if (change.LostLeadership)
                {
                    _logger.LogWarning("😞 [{ParticipantId}] Leadership lost!",
                        _leaderElection.ParticipantId);
                }
            }
        }, stoppingToken);

        var workCounter = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_leaderElection.IsLeader)
            {
                // Perform leader-only work
                workCounter++;
                _logger.LogInformation("🏆 [{ParticipantId}] Leader performing work #{WorkCounter}",
                    _leaderElection.ParticipantId, workCounter);

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            else
            {
                // Follower behavior
                _logger.LogInformation("👥 [{ParticipantId}] Follower waiting...",
                    _leaderElection.ParticipantId);

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
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
}

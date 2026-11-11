using Microsoft.Extensions.Hosting;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

/// <summary>
/// Runs rated, deck-strength-aware matchmaking without requiring an administrator
/// to manually press "Run". Expands eligibility based on queue wait time and
/// only backfills with bots after the configured threshold.
/// </summary>
public sealed class MatchmakingWorker(AppState state, OperationsService operations,
    UnitySimulationBridge unity) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One solo player vs 1–4 opponents; no symmetrical team queues.
        foreach (var opponents in new[] { 1, 2, 3, 4 })
        {
            var seats = opponents + 1;
            var id = opponents == 1 ? "casual" : $"one-v-{opponents}";
            state.MatchmakingQueues[id] = new MatchmakingQueueConfiguration(
                id, $"1v{opponents}", "skill-and-deck-power", seats, seats,
                120, 5, 30, true, true, DateTimeOffset.UtcNow);
        }
        // Remove obsolete symmetric queue definitions from earlier revisions.
        foreach (var old in state.MatchmakingQueues.Keys.Where(key =>
            key.StartsWith("team-", StringComparison.OrdinalIgnoreCase)).ToList())
            state.MatchmakingQueues.TryRemove(old, out _);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var queue in state.MatchmakingQueues.Values.Where(queue => queue.Enabled))
            {
                if (!state.MatchmakingEntries.Values.Any(entry => entry.QueueId == queue.Id)) continue;
                try
                {
                    if (unity.Enabled)
                        await operations.RunUnityMatchmakingAsync(queue.Id, "automated-matchmaker", unity,
                            stoppingToken);
                    else operations.RunMatchmaking(queue.Id, "automated-matchmaker");
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine("Matchmaking failed: " + exception.Message);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}

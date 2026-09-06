using EZBuddy.Core.Diagnostics;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Diagnostics;
using EZBuddy.UI.Models;
using EZBuddy.UI.ViewModels;
using ff14bot;
using ff14bot.Managers;

namespace EZBuddy.Plugin;

public sealed class RebornBuddyTelemetryProvider : IHostTelemetryProvider
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly ConflictGuard _conflictGuard = new(new RebornBuddyRuntimeComponentSource());

    public async Task<TelemetryUpdate> GetTelemetryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var queue = EZBuddyRuntime.Queue;
        var routineName = RoutineManager.Current?.Name ?? "None";
        var routineActive = string.Equals(routineName, "Magitek", StringComparison.OrdinalIgnoreCase);
        var statuses = await EZBuddyRuntime.Adapters.GetStatusesAsync(cancellationToken).ConfigureAwait(false);
        var findings = await _conflictGuard.ScanAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var player = ff14bot.Core.Player;
        var characterName = player?.Name ?? "Not connected";
        var locationLabel = player is null ? "—" : $"Zone {WorldManager.ZoneId}";

        var applicationStatus = queue.IsEmergencyStopped
            ? "Emergency Stopped"
            : queue.IsPaused
                ? "Paused"
                : queue.IsRunning
                    ? $"Running - {queue.CurrentActivity?.Name ?? "Queue"}"
                    : "Idle";

        return new TelemetryUpdate(
            applicationStatus,
            queue.CurrentActivity?.Name ?? "No active activity",
            characterName,
            locationLabel,
            routineName,
            routineActive,
            GilEarned: 0,
            SessionRuntime: DateTimeOffset.UtcNow - _startedAt,
            ActiveHooks: statuses.Count(status => status.Health is EZBuddy.Core.Adapters.AdapterHealth.Ready or EZBuddy.Core.Adapters.AdapterHealth.Busy),
            WarningCount: findings.Count(finding => finding.Severity is ConflictSeverity.Warning or ConflictSeverity.Critical));
    }
}

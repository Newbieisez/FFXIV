using EZBuddy.Core.Diagnostics;
using EZBuddy.Core.Settings;

namespace EZBuddy.RebornBuddy.Diagnostics;

public sealed class RebornBuddyConflictPreflight
{
    private readonly ConflictGuard _guard;

    public RebornBuddyConflictPreflight(IRuntimeComponentSource? source = null)
    {
        _guard = new ConflictGuard(source ?? new RebornBuddyRuntimeComponentSource());
    }

    public async Task<IReadOnlyList<ConflictFinding>> ScanAsync(
        FirstPlayableLoopSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (settings.RunDutyLoop)
        {
            modules.Add("Duty");
            modules.Add("Farming");
        }

        if (settings.RunRetainers)
        {
            modules.Add("Retainers");
        }

        if (settings.RunMaintenance && settings.AutoRepairGear)
        {
            modules.Add("Repair");
        }

        if (settings.RunDailyProgression)
        {
            modules.Add("MSQ");
        }

        return await _guard.ScanAsync(modules, cancellationToken).ConfigureAwait(false);
    }

    public static string Summarize(IReadOnlyCollection<ConflictFinding> findings)
    {
        if (findings.Count == 0)
        {
            return "No cooperative runtime conflicts detected.";
        }

        return string.Join(
            " | ",
            findings.Select(finding =>
                $"[{finding.Severity}] {finding.Title}: {finding.Detail}"));
    }
}

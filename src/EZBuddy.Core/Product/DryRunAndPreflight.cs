namespace EZBuddy.Core.Product;

public enum PreflightSeverity
{
    Ready,
    Warning,
    Blocked
}

public sealed record AdapterCompatibilityStamp(
    string AdapterKey,
    string ValidatedHostVersion,
    string? ValidatedGameBuild = null,
    DateTimeOffset? ValidatedAtUtc = null,
    bool PatchSensitive = true);

public sealed record AdapterRuntimeSnapshot(
    string AdapterKey,
    bool Available,
    string? HostVersion = null,
    string? GameBuild = null,
    string? Message = null);

public sealed record PreflightSnapshot(
    bool CharacterAvailable,
    bool LicenseAllowsExecution,
    bool MagitekRequired,
    bool MagitekReady,
    int FreeInventorySlots,
    int MinimumFreeInventorySlots,
    bool RouteRequired,
    bool RouteVerified,
    IReadOnlyList<AdapterRuntimeSnapshot> Adapters,
    IReadOnlyList<AdapterCompatibilityStamp> CompatibilityStamps);

public sealed record PreflightCheck(
    string Key,
    string Title,
    PreflightSeverity Severity,
    string Message);

public sealed record PreflightReport(IReadOnlyList<PreflightCheck> Checks)
{
    public bool IsBlocked => Checks.Any(check => check.Severity == PreflightSeverity.Blocked);
    public bool HasWarnings => Checks.Any(check => check.Severity == PreflightSeverity.Warning);
    public string OverallState => IsBlocked ? "BLOCKED" : HasWarnings ? "WARNING" : "READY";
}

public static class PreflightEvaluator
{
    public static PreflightReport Evaluate(PreflightSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.MinimumFreeInventorySlots is < 0 or > 140)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot.MinimumFreeInventorySlots));
        }

        var checks = new List<PreflightCheck>
        {
            Check("character", "Character", snapshot.CharacterAvailable, "Character is available.", "Character is not available."),
            Check("license", "License", snapshot.LicenseAllowsExecution, "Entitlement permits execution.", "Current entitlement does not permit execution."),
            Check("inventory", "Inventory", snapshot.FreeInventorySlots >= snapshot.MinimumFreeInventorySlots,
                $"{snapshot.FreeInventorySlots} free slots available.",
                $"Only {snapshot.FreeInventorySlots} free slots are available; {snapshot.MinimumFreeInventorySlots} required.")
        };

        if (snapshot.MagitekRequired)
        {
            checks.Add(Check("magitek", "Magitek", snapshot.MagitekReady, "Magitek is ready.", "Magitek is required but unavailable or inactive."));
        }

        if (snapshot.RouteRequired)
        {
            checks.Add(Check("route", "Duty Route", snapshot.RouteVerified, "A verified EZBuddy route/profile is available.", "No verified route/profile is available for the requested duty."));
        }

        var stampByKey = snapshot.CompatibilityStamps
            .GroupBy(stamp => stamp.AdapterKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in snapshot.Adapters.OrderBy(adapter => adapter.AdapterKey, StringComparer.OrdinalIgnoreCase))
        {
            if (!adapter.Available)
            {
                checks.Add(new PreflightCheck(
                    $"adapter:{adapter.AdapterKey}",
                    adapter.AdapterKey,
                    PreflightSeverity.Warning,
                    adapter.Message ?? "Adapter is unavailable."));
                continue;
            }

            if (!stampByKey.TryGetValue(adapter.AdapterKey, out var stamp))
            {
                checks.Add(new PreflightCheck(
                    $"compat:{adapter.AdapterKey}",
                    $"{adapter.AdapterKey} compatibility",
                    PreflightSeverity.Warning,
                    "Adapter is available but has no validation stamp for this EZBuddy build."));
                continue;
            }

            var hostMismatch = !string.IsNullOrWhiteSpace(stamp.ValidatedHostVersion) &&
                               !string.IsNullOrWhiteSpace(adapter.HostVersion) &&
                               !string.Equals(stamp.ValidatedHostVersion, adapter.HostVersion, StringComparison.OrdinalIgnoreCase);
            var gameMismatch = !string.IsNullOrWhiteSpace(stamp.ValidatedGameBuild) &&
                               !string.IsNullOrWhiteSpace(adapter.GameBuild) &&
                               !string.Equals(stamp.ValidatedGameBuild, adapter.GameBuild, StringComparison.OrdinalIgnoreCase);

            if (hostMismatch || gameMismatch)
            {
                checks.Add(new PreflightCheck(
                    $"compat:{adapter.AdapterKey}",
                    $"{adapter.AdapterKey} compatibility",
                    stamp.PatchSensitive ? PreflightSeverity.Blocked : PreflightSeverity.Warning,
                    $"Runtime version differs from the last validated stamp (host {adapter.HostVersion ?? "unknown"}, game {adapter.GameBuild ?? "unknown"})."));
            }
            else
            {
                checks.Add(new PreflightCheck(
                    $"compat:{adapter.AdapterKey}",
                    $"{adapter.AdapterKey} compatibility",
                    PreflightSeverity.Ready,
                    "Runtime matches the recorded compatibility stamp."));
            }
        }

        return new PreflightReport(checks);
    }

    private static PreflightCheck Check(string key, string title, bool ready, string readyMessage, string blockedMessage)
        => new(key, title, ready ? PreflightSeverity.Ready : PreflightSeverity.Blocked, ready ? readyMessage : blockedMessage);
}

public sealed record DryRunCandidate(
    string Key,
    string Title,
    string Category,
    int Priority,
    bool Enabled,
    bool CanExecute,
    bool IsDestructive = false,
    bool DestructiveOptIn = false,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<string>? Blockers = null,
    string? Reason = null);

public enum DryRunDisposition
{
    WouldRun,
    Disabled,
    Blocked,
    RequiresOptIn
}

public sealed record DryRunAction(
    string Key,
    string Title,
    string Category,
    int Priority,
    DryRunDisposition Disposition,
    string Explanation,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers);

public sealed record DryRunPlan(IReadOnlyList<DryRunAction> Actions)
{
    public int RunnableCount => Actions.Count(action => action.Disposition == DryRunDisposition.WouldRun);
    public int BlockedCount => Actions.Count(action => action.Disposition is DryRunDisposition.Blocked or DryRunDisposition.RequiresOptIn);
}

public static class DryRunPlanner
{
    public static DryRunPlan Build(IEnumerable<DryRunCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var actions = candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
            .Select(BuildAction)
            .ToArray();
        return new DryRunPlan(actions);
    }

    private static DryRunAction BuildAction(DryRunCandidate candidate)
    {
        var warnings = candidate.Warnings ?? Array.Empty<string>();
        var blockers = candidate.Blockers ?? Array.Empty<string>();

        if (!candidate.Enabled)
        {
            return new DryRunAction(candidate.Key, candidate.Title, candidate.Category, candidate.Priority,
                DryRunDisposition.Disabled, candidate.Reason ?? "Feature is disabled.", warnings, blockers);
        }

        if (candidate.IsDestructive && !candidate.DestructiveOptIn)
        {
            return new DryRunAction(candidate.Key, candidate.Title, candidate.Category, candidate.Priority,
                DryRunDisposition.RequiresOptIn, "Destructive action requires explicit opt-in.", warnings, blockers);
        }

        if (!candidate.CanExecute || blockers.Count > 0)
        {
            return new DryRunAction(candidate.Key, candidate.Title, candidate.Category, candidate.Priority,
                DryRunDisposition.Blocked, candidate.Reason ?? "One or more execution requirements are not satisfied.", warnings, blockers);
        }

        return new DryRunAction(candidate.Key, candidate.Title, candidate.Category, candidate.Priority,
            DryRunDisposition.WouldRun, candidate.Reason ?? "All known requirements are satisfied.", warnings, blockers);
    }
}

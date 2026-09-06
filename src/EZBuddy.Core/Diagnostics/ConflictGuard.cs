namespace EZBuddy.Core.Diagnostics;

public enum ConflictSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record RuntimeComponent(
    string Name,
    string Kind,
    bool IsEnabled,
    bool IsCurrent,
    Version? Version = null);

public sealed record ConflictRule(
    string Id,
    string Description,
    ConflictSeverity Severity,
    IReadOnlyCollection<string> ComponentNamePatterns,
    IReadOnlyCollection<string> ModuleKeys,
    bool PauseAffectedActivities = true);

public sealed record ConflictFinding(
    string RuleId,
    ConflictSeverity Severity,
    string Title,
    string Detail,
    IReadOnlyCollection<string> Components,
    IReadOnlyCollection<string> AffectedModules,
    bool ShouldPause);

public interface IRuntimeComponentSource
{
    Task<IReadOnlyList<RuntimeComponent>> GetComponentsAsync(CancellationToken cancellationToken = default);
}

public sealed class ConflictGuard
{
    private readonly IRuntimeComponentSource _source;
    private readonly IReadOnlyList<ConflictRule> _rules;

    public ConflictGuard(IRuntimeComponentSource source, IEnumerable<ConflictRule>? rules = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _rules = (rules ?? DefaultRules()).ToArray();
    }

    public async Task<IReadOnlyList<ConflictFinding>> ScanAsync(
        IReadOnlyCollection<string>? enabledEzModules = null,
        CancellationToken cancellationToken = default)
    {
        var components = await _source.GetComponentsAsync(cancellationToken).ConfigureAwait(false);
        var enabledModules = enabledEzModules is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(enabledEzModules, StringComparer.OrdinalIgnoreCase);

        var findings = new List<ConflictFinding>();
        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rule.ModuleKeys.Count > 0 && enabledModules.Count > 0 && !rule.ModuleKeys.Any(enabledModules.Contains))
            {
                continue;
            }

            var matches = components
                .Where(component => component.IsEnabled || component.IsCurrent)
                .Where(component => rule.ComponentNamePatterns.Any(pattern => Contains(component.Name, pattern)))
                .ToArray();

            if (matches.Length == 0)
            {
                continue;
            }

            findings.Add(new ConflictFinding(
                rule.Id,
                rule.Severity,
                rule.Description,
                $"Detected: {string.Join(", ", matches.Select(m => $"{m.Name} ({m.Kind})"))}",
                matches.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                rule.ModuleKeys,
                rule.PauseAffectedActivities && rule.Severity == ConflictSeverity.Critical));
        }

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool Contains(string source, string pattern)
        => source.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ConflictRule> DefaultRules() =>
    [
        new(
            "combat.magitek-required",
            "Combat-dependent EZBuddy activities require Magitek to be the current combat routine.",
            ConflictSeverity.Critical,
            ["Kupo", "Ultima", "RebornCo", "ATB"],
            ["Duty", "Farming", "MSQ", "TripleTriadDuty"],
            true),
        new(
            "repair.overlap",
            "Another auto-repair provider may race EZBuddy Repair.",
            ConflictSeverity.Warning,
            ["Cyril", "AutoRepair", "Platypus"],
            ["Repair"],
            false),
        new(
            "duty.overlap",
            "Another duty automation provider is active; only one duty controller should own movement/mechanics.",
            ConflictSeverity.Critical,
            ["Duty Mechanic", "DutyMechanic", "Trust", "RBTrust", "PandaFarmer"],
            ["Duty", "Farming", "MSQ"],
            true),
        new(
            "retainer.overlap",
            "Another retainer automation provider is active; bell actions must be coordinated through the shared adapter path.",
            ConflictSeverity.Warning,
            ["RetainerMaid", "Platypus", "LlamaMarket"],
            ["Retainers", "Marketboard"],
            false),
        new(
            "gathercraft.external",
            "Crafting/gathering provider detected. EZBuddy should use the adapter rather than competing for control.",
            ConflictSeverity.Info,
            ["Lisbeth"],
            ["Crafting", "Gathering"],
            false)
    ];
}

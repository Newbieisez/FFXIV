using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EZBuddy.Core.Research;

public sealed record DutyCoverageMatrix(
    IReadOnlyList<DutyCoverageRow> Duties,
    DateTimeOffset GeneratedUtc,
    int SchemaVersion = 1)
{
    public int DutyCount => Duties.Count;
    public int WithMultipleSources => Duties.Count(duty => duty.AllSources.Count > 1);
    public int WithMechanicEvidence => Duties.Count(duty => duty.MechanicSources.Count > 0);
}

public sealed record DutyCoverageRow(
    string DutyKey,
    string DisplayName,
    RouteReferenceCategory Category,
    IReadOnlyList<string> RouteSources,
    IReadOnlyList<string> MechanicSources,
    IReadOnlyList<uint> QueueDutyIds,
    IReadOnlyList<uint> TerritoryIds,
    IReadOnlyList<uint> NpcIds,
    IReadOnlyList<uint> ObjectIds,
    IReadOnlyList<uint> ActionIds,
    IReadOnlyList<uint> StatusIds,
    IReadOnlyList<uint> TetherIds,
    IReadOnlyList<uint> IconIds,
    IReadOnlyList<uint> MapEffectIds,
    IReadOnlyList<string> InteractionKinds,
    IReadOnlyList<string> MechanicKinds,
    int RouteLikeNodeCount,
    int ReferenceFileCount,
    string NextAction)
{
    public IReadOnlyList<string> AllSources => RouteSources
        .Concat(MechanicSources)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

/// <summary>
/// Merges metadata-only route and mechanic manifests into an actionable duty research matrix.
/// This type never consumes or emits waypoint coordinates, vectors, destinations, raw source
/// bodies, or executable third-party mechanic logic.
/// </summary>
public sealed partial class DutyCoverageMatrixBuilder
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "abstract dungeon",
        "dungeon",
        "dungeons",
        "duty",
        "instance",
        "constants",
        "helpers",
        "plugin helpers",
        "extensions",
        "settings",
        "config",
        "configuration",
        "common",
        "utility",
        "utilities",
        "base"
    };

    public DutyCoverageMatrix Build(
        IEnumerable<RouteReferenceManifest> routeManifests,
        IEnumerable<PluginMechanicReferenceManifest> mechanicManifests)
    {
        ArgumentNullException.ThrowIfNull(routeManifests);
        ArgumentNullException.ThrowIfNull(mechanicManifests);

        var routeList = routeManifests.ToArray();
        var mechanicList = mechanicManifests.ToArray();
        var mechanicIndex = BuildMechanicIndex(mechanicList);

        var groups = new Dictionary<string, MutableDutyCoverage>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in routeList)
        {
            foreach (var entry in manifest.Entries)
            {
                if (entry.Category == RouteReferenceCategory.Unknown)
                {
                    continue;
                }

                var displayName = CleanDisplayName(Path.GetFileNameWithoutExtension(entry.FileName));
                if (string.IsNullOrWhiteSpace(displayName) || GenericNames.Contains(displayName))
                {
                    continue;
                }

                var dutyKey = NormalizeDutyKey(displayName);
                if (string.IsNullOrWhiteSpace(dutyKey))
                {
                    continue;
                }

                if (!groups.TryGetValue(dutyKey, out var group))
                {
                    group = new MutableDutyCoverage(dutyKey, displayName, entry.Category);
                    groups[dutyKey] = group;
                }

                group.ObserveRoute(manifest.SourceLabel, entry);

                if (mechanicIndex.TryGetValue(dutyKey, out var matchingMechanics))
                {
                    foreach (var mechanic in matchingMechanics)
                    {
                        group.ObserveMechanic(mechanic.SourceLabel, mechanic.Entry);
                    }
                }
            }
        }

        return new DutyCoverageMatrix(
            groups.Values
                .Select(group => group.ToRow())
                .OrderBy(row => CategoryRank(row.Category))
                .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DateTimeOffset.UtcNow);
    }

    public static string SerializeJson(DutyCoverageMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        return JsonSerializer.Serialize(matrix, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
    }

    public static string SerializeCsv(DutyCoverageMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var builder = new StringBuilder();
        builder.AppendLine("Duty,Category,QueueDutyIds,TerritoryIds,RouteSources,MechanicSources,RouteLikeNodes,ReferenceFiles,MechanicKinds,NextAction");
        foreach (var duty in matrix.Duties)
        {
            builder.Append(Csv(duty.DisplayName)).Append(',')
                .Append(Csv(duty.Category.ToString())).Append(',')
                .Append(Csv(string.Join("|", duty.QueueDutyIds))).Append(',')
                .Append(Csv(string.Join("|", duty.TerritoryIds))).Append(',')
                .Append(Csv(string.Join("|", duty.RouteSources))).Append(',')
                .Append(Csv(string.Join("|", duty.MechanicSources))).Append(',')
                .Append(duty.RouteLikeNodeCount).Append(',')
                .Append(duty.ReferenceFileCount).Append(',')
                .Append(Csv(string.Join("|", duty.MechanicKinds))).Append(',')
                .Append(Csv(duty.NextAction))
                .AppendLine();
        }

        return builder.ToString();
    }

    public static string NormalizeDutyKey(string value)
    {
        var clean = CleanDisplayName(value).ToLowerInvariant();
        return string.Join('-', Regex.Split(clean, "[^a-z0-9]+", RegexOptions.CultureInvariant)
            .Where(token => !string.IsNullOrWhiteSpace(token)));
    }

    private static Dictionary<string, List<MechanicEvidence>> BuildMechanicIndex(
        IEnumerable<PluginMechanicReferenceManifest> manifests)
    {
        var index = new Dictionary<string, List<MechanicEvidence>>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests)
        {
            foreach (var entry in manifest.Entries)
            {
                var displayName = CleanDisplayName(Path.GetFileNameWithoutExtension(entry.FileName));
                if (string.IsNullOrWhiteSpace(displayName) || GenericNames.Contains(displayName))
                {
                    continue;
                }

                var key = NormalizeDutyKey(displayName);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!index.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    index[key] = bucket;
                }

                bucket.Add(new MechanicEvidence(manifest.SourceLabel, entry));
            }
        }

        return index;
    }

    private static string CleanDisplayName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = BracketPrefixRegex().Replace(raw, " ");
        value = PascalBoundaryRegex().Replace(value, "$1 $2");
        value = SeparatorRegex().Replace(value, " ");
        value = CommonPrefixRegex().Replace(value, " ");
        value = WhitespaceRegex().Replace(value, " ").Trim(' ', '-', '_', '.');
        return value;
    }

    private static int CategoryRank(RouteReferenceCategory category) => category switch
    {
        RouteReferenceCategory.Dungeon => 0,
        RouteReferenceCategory.Trial => 1,
        RouteReferenceCategory.Raid => 2,
        RouteReferenceCategory.AllianceRaid => 3,
        RouteReferenceCategory.Duty => 4,
        _ => 5
    };

    private static RouteReferenceCategory PreferCategory(RouteReferenceCategory left, RouteReferenceCategory right)
        => CategoryRank(right) < CategoryRank(left) ? right : left;

    private static string Csv(string value)
        => '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    [GeneratedRegex(@"\[[^\]]+\]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketPrefixRegex();

    [GeneratedRegex(@"([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex PascalBoundaryRegex();

    [GeneratedRegex(@"[_./\\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"^(?:orderbot|trust|duty support|profile|dungeon)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommonPrefixRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record MechanicEvidence(string SourceLabel, PluginMechanicReferenceEntry Entry);

    private sealed class MutableDutyCoverage
    {
        private readonly HashSet<string> _routeSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _mechanicSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<uint> _queueDutyIds = [];
        private readonly HashSet<uint> _territoryIds = [];
        private readonly HashSet<uint> _npcIds = [];
        private readonly HashSet<uint> _objectIds = [];
        private readonly HashSet<uint> _actionIds = [];
        private readonly HashSet<uint> _statusIds = [];
        private readonly HashSet<uint> _tetherIds = [];
        private readonly HashSet<uint> _iconIds = [];
        private readonly HashSet<uint> _mapEffectIds = [];
        private readonly HashSet<string> _interactionKinds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _mechanicKinds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _referenceFiles = new(StringComparer.OrdinalIgnoreCase);

        public MutableDutyCoverage(string dutyKey, string displayName, RouteReferenceCategory category)
        {
            DutyKey = dutyKey;
            DisplayName = displayName;
            Category = category;
        }

        public string DutyKey { get; }
        public string DisplayName { get; private set; }
        public RouteReferenceCategory Category { get; private set; }
        public int RouteLikeNodeCount { get; private set; }

        public void ObserveRoute(string sourceLabel, RouteReferenceEntry entry)
        {
            _routeSources.Add(sourceLabel);
            _referenceFiles.Add(sourceLabel + ":" + entry.RelativePath);
            Category = PreferCategory(Category, entry.Category);
            if (DisplayName.Length > 80 && entry.FileName.Length < DisplayName.Length)
            {
                DisplayName = CleanDisplayName(Path.GetFileNameWithoutExtension(entry.FileName));
            }

            RouteLikeNodeCount += entry.WaypointLikeNodeCount;
            _queueDutyIds.UnionWith(entry.QueueDutyIds);
            _territoryIds.UnionWith(entry.TerritoryIds);
            _npcIds.UnionWith(entry.NpcIds);
            _objectIds.UnionWith(entry.ObjectIds);
            _actionIds.UnionWith(entry.ActionIds);
            _interactionKinds.UnionWith(entry.InteractionKinds);
        }

        public void ObserveMechanic(string sourceLabel, PluginMechanicReferenceEntry entry)
        {
            _mechanicSources.Add(sourceLabel);
            _referenceFiles.Add(sourceLabel + ":" + entry.RelativePath);
            _actionIds.UnionWith(entry.ActionIds);
            _statusIds.UnionWith(entry.StatusIds);
            _tetherIds.UnionWith(entry.TetherIds);
            _iconIds.UnionWith(entry.IconIds);
            _mapEffectIds.UnionWith(entry.MapEffectIds);
            _npcIds.UnionWith(entry.NpcIds);
            _objectIds.UnionWith(entry.ObjectIds);
            _mechanicKinds.UnionWith(entry.MechanicKinds);
        }

        public DutyCoverageRow ToRow()
        {
            var nextAction = BuildNextAction();
            return new DutyCoverageRow(
                DutyKey,
                DisplayName,
                Category,
                _routeSources.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                _mechanicSources.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                _queueDutyIds.Order().ToArray(),
                _territoryIds.Order().ToArray(),
                _npcIds.Order().ToArray(),
                _objectIds.Order().ToArray(),
                _actionIds.Order().ToArray(),
                _statusIds.Order().ToArray(),
                _tetherIds.Order().ToArray(),
                _iconIds.Order().ToArray(),
                _mapEffectIds.Order().ToArray(),
                _interactionKinds.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                _mechanicKinds.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                RouteLikeNodeCount,
                _referenceFiles.Count,
                nextAction);
        }

        private string BuildNextAction()
        {
            if (_queueDutyIds.Count == 0 || _territoryIds.Count == 0)
            {
                return _mechanicSources.Count == 0
                    ? "Verify queue/territory IDs, then record an original EZBuddy route."
                    : "Verify queue/territory IDs, record an original EZBuddy route, and validate mechanic metadata.";
            }

            if (_mechanicSources.Count == 0)
            {
                return "Record an original EZBuddy route and capture/validate encounter mechanics.";
            }

            return "Record an original EZBuddy route and validate referenced mechanic IDs before enabling automation.";
        }
    }
}

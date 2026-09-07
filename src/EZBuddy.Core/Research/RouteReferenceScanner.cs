using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace EZBuddy.Core.Research;

public enum RouteReferenceCategory
{
    Unknown,
    Dungeon,
    Trial,
    Raid,
    AllianceRaid,
    Duty
}

public sealed record RouteReferenceManifest(
    string SourceLabel,
    IReadOnlyList<RouteReferenceEntry> Entries,
    int SchemaVersion = 1)
{
    public int FileCount => Entries.Count;
    public int RouteLikeFileCount => Entries.Count(entry => entry.WaypointLikeNodeCount > 0);
}

public sealed record RouteReferenceEntry(
    string RelativePath,
    string FileName,
    string Sha256,
    RouteReferenceCategory Category,
    int WaypointLikeNodeCount,
    IReadOnlyList<uint> QueueDutyIds,
    IReadOnlyList<uint> TerritoryIds,
    IReadOnlyList<uint> NpcIds,
    IReadOnlyList<uint> ObjectIds,
    IReadOnlyList<uint> ActionIds,
    IReadOnlyList<string> InteractionKinds);

public sealed record RouteReferenceScanOptions(
    long MaximumFileBytes = 5 * 1024 * 1024,
    int MaximumFiles = 50_000)
{
    public void Validate()
    {
        if (MaximumFileBytes is < 1_024 or > 50 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumFileBytes));
        }

        if (MaximumFiles is < 1 or > 500_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumFiles));
        }
    }
}

/// <summary>
/// Produces a non-executable reference inventory from RebornBuddy-style profile/source folders.
/// The output schema intentionally has no position/vector/coordinate fields. Source waypoint
/// coordinates and raw profile bodies are never copied into the manifest.
/// </summary>
public sealed partial class RouteReferenceScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".json", ".cs"
    };

    private static readonly HashSet<string> QueueDutyKeys = CreateKeySet(
        "dutyid", "duty_id", "queuedutyid", "queueduty_id", "instancecontentid", "instancecontent_id",
        "instanceid", "instance_id", "contentid", "content_id");

    private static readonly HashSet<string> TerritoryKeys = CreateKeySet(
        "territoryid", "territory_id", "zoneid", "zone_id", "mapid", "map_id", "territorytypeid",
        "territorytype_id");

    private static readonly HashSet<string> NpcKeys = CreateKeySet(
        "npcid", "npc_id", "bossid", "boss_id", "battlenpcid", "battlenpc_id", "enemynpcid", "enemynpc_id");

    private static readonly HashSet<string> ObjectKeys = CreateKeySet(
        "objectid", "object_id", "gameobjectid", "gameobject_id", "dataid", "data_id", "interactid", "interact_id");

    private static readonly HashSet<string> ActionKeys = CreateKeySet(
        "actionid", "action_id", "spellid", "spell_id", "castid", "cast_id", "abilityid", "ability_id");

    private static readonly string[] WaypointKeywords =
    [
        "waypoint", "hotspot", "moveto", "movetolocation", "movealong", "navigation", "path", "vector3"
    ];

    private static readonly (string Label, string[] Keywords)[] InteractionKeywords =
    [
        ("Door", ["door", "gate"]),
        ("Switch", ["switch", "lever", "coral"]),
        ("Lift", ["lift", "elevator"]),
        ("Chest", ["chest", "coffer", "treasure"]),
        ("Boss", ["boss"]),
        ("Interact", ["interact", "target", "talkto"]),
        ("Loot", ["loot"])
    ];

    private readonly RouteReferenceScanOptions _options;

    public RouteReferenceScanner(RouteReferenceScanOptions? options = null)
    {
        _options = options ?? new RouteReferenceScanOptions();
        _options.Validate();
    }

    public RouteReferenceManifest Scan(string sourceLabel, IEnumerable<string> roots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLabel);
        ArgumentNullException.ThrowIfNull(roots);

        var normalizedRoots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedRoots.Length == 0)
        {
            throw new ArgumentException("At least one scan root is required.", nameof(roots));
        }

        var entries = new List<RouteReferenceEntry>();
        foreach (var root in normalizedRoots)
        {
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Route reference scan root does not exist: {root}");
            }

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (entries.Count >= _options.MaximumFiles)
                {
                    throw new InvalidDataException($"Route reference scan exceeded the {_options.MaximumFiles:N0}-file safety limit.");
                }

                if (!SupportedExtensions.Contains(Path.GetExtension(path)))
                {
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length > _options.MaximumFileBytes)
                {
                    continue;
                }

                var relative = normalizedRoots.Length == 1
                    ? Path.GetRelativePath(root, path)
                    : Path.Combine(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), Path.GetRelativePath(root, path));

                try
                {
                    entries.Add(ScanFile(path, NormalizeRelativePath(relative)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or JsonException or DecoderFallbackException)
                {
                    // Reference packs can contain malformed/partial files. One bad source file must
                    // not abort the inventory or tempt callers to fall back to raw copying.
                }
            }
        }

        return new RouteReferenceManifest(
            sourceLabel.Trim(),
            entries
                .OrderBy(entry => entry.Category)
                .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public RouteReferenceEntry ScanFile(string path, string? relativePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Route reference source file was not found.", fullPath);
        }

        if (info.Length > _options.MaximumFileBytes)
        {
            throw new InvalidDataException($"Source file exceeds the {_options.MaximumFileBytes:N0}-byte safety limit.");
        }

        if (!SupportedExtensions.Contains(info.Extension))
        {
            throw new InvalidDataException($"Unsupported route reference extension '{info.Extension}'.");
        }

        var bytes = File.ReadAllBytes(fullPath);
        var text = new UTF8Encoding(false, true).GetString(bytes);
        var accumulator = new ScanAccumulator();

        switch (info.Extension.ToLowerInvariant())
        {
            case ".xml":
                ScanXml(text, accumulator);
                break;
            case ".json":
                ScanJson(text, accumulator);
                break;
            case ".cs":
                ScanCode(text, accumulator);
                break;
        }

        var safeRelativePath = NormalizeRelativePath(relativePath ?? info.Name);
        accumulator.InferFromText(safeRelativePath);

        return new RouteReferenceEntry(
            safeRelativePath,
            info.Name,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Classify(safeRelativePath),
            accumulator.WaypointLikeNodeCount,
            accumulator.QueueDutyIds.Order().ToArray(),
            accumulator.TerritoryIds.Order().ToArray(),
            accumulator.NpcIds.Order().ToArray(),
            accumulator.ObjectIds.Order().ToArray(),
            accumulator.ActionIds.Order().ToArray(),
            accumulator.InteractionKinds.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static string Serialize(RouteReferenceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
    }

    private static void ScanXml(string text, ScanAccumulator accumulator)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = Math.Max(1, text.Length + 1L)
        };

        using var stringReader = new StringReader(text);
        using var reader = XmlReader.Create(stringReader, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            accumulator.ObserveToken(reader.Name);
            if (!reader.HasAttributes)
            {
                continue;
            }

            while (reader.MoveToNextAttribute())
            {
                accumulator.ObserveToken(reader.Name);
                accumulator.ObserveNamedValue(reader.Name, reader.Value);
            }

            reader.MoveToElement();
        }
    }

    private static void ScanJson(string text, ScanAccumulator accumulator)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 128
        });
        ScanJsonElement(document.RootElement, accumulator);
    }

    private static void ScanJsonElement(JsonElement element, ScanAccumulator accumulator)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    accumulator.ObserveToken(property.Name);
                    if (property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String)
                    {
                        accumulator.ObserveNamedValue(property.Name, property.Value.ToString());
                    }
                    ScanJsonElement(property.Value, accumulator);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ScanJsonElement(item, accumulator);
                }
                break;
        }
    }

    private static void ScanCode(string text, ScanAccumulator accumulator)
    {
        foreach (Match match in NamedNumericAssignmentRegex().Matches(text))
        {
            accumulator.ObserveNamedValue(match.Groups["name"].Value, match.Groups["value"].Value);
        }

        foreach (Match match in NamedNumericCallRegex().Matches(text))
        {
            accumulator.ObserveNamedValue(match.Groups["name"].Value, match.Groups["value"].Value);
        }

        foreach (var keyword in WaypointKeywords)
        {
            accumulator.WaypointLikeNodeCount += Regex.Matches(text, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
        }

        foreach (var (label, keywords) in InteractionKeywords)
        {
            if (keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                accumulator.InteractionKinds.Add(label);
            }
        }
    }

    private static RouteReferenceCategory Classify(string relativePath)
    {
        if (ContainsAny(relativePath, "alliance raid", "alliance_raid", "allianceraid", "24man", "24-man"))
        {
            return RouteReferenceCategory.AllianceRaid;
        }

        if (ContainsAny(relativePath, "raid", "coil", "alexander", "omega", "eden", "pandaemonium", "arcadion"))
        {
            return RouteReferenceCategory.Raid;
        }

        if (ContainsAny(relativePath, "trial", "extreme", "minstrel", "unreal"))
        {
            return RouteReferenceCategory.Trial;
        }

        if (ContainsAny(relativePath, "dungeon", "dungeons", "sastasha", "tam-tara", "tamtara", "copperbell"))
        {
            return RouteReferenceCategory.Dungeon;
        }

        if (ContainsAny(relativePath, "duty", "instance"))
        {
            return RouteReferenceCategory.Duty;
        }

        return RouteReferenceCategory.Unknown;
    }

    private static HashSet<string> CreateKeySet(params string[] values)
        => new(values.Select(NormalizeKey), StringComparer.Ordinal);

    private static string NormalizeKey(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeRelativePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    [GeneratedRegex(@"\b(?:(?:const|static|readonly)\s+)*(?:(?:u?int|long|short|var)\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*(?:Id|ID))\s*=\s*(?<value>\d+)(?:[uUlL]*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NamedNumericAssignmentRegex();

    [GeneratedRegex(@"\b(?<name>(?:Duty|QueueDuty|Territory|Zone|Map|Npc|NPC|Boss|Object|GameObject|Data|Interact|Action|Spell|Cast|Ability)Id)\s*:\s*(?<value>\d+)(?:[uUlL]*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedNumericCallRegex();

    private sealed class ScanAccumulator
    {
        public HashSet<uint> QueueDutyIds { get; } = [];
        public HashSet<uint> TerritoryIds { get; } = [];
        public HashSet<uint> NpcIds { get; } = [];
        public HashSet<uint> ObjectIds { get; } = [];
        public HashSet<uint> ActionIds { get; } = [];
        public HashSet<string> InteractionKinds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int WaypointLikeNodeCount { get; set; }

        public void ObserveToken(string token)
        {
            if (WaypointKeywords.Any(keyword => token.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                WaypointLikeNodeCount++;
            }

            foreach (var (label, keywords) in InteractionKeywords)
            {
                if (keywords.Any(keyword => token.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    InteractionKinds.Add(label);
                }
            }
        }

        public void ObserveNamedValue(string name, string value)
        {
            var normalized = NormalizeKey(name);
            if (!uint.TryParse(value.Trim().TrimEnd('u', 'U', 'l', 'L'), out var parsed) || parsed == 0)
            {
                return;
            }

            if (QueueDutyKeys.Contains(normalized))
            {
                QueueDutyIds.Add(parsed);
            }
            else if (TerritoryKeys.Contains(normalized))
            {
                TerritoryIds.Add(parsed);
            }
            else if (NpcKeys.Contains(normalized))
            {
                NpcIds.Add(parsed);
            }
            else if (ObjectKeys.Contains(normalized))
            {
                ObjectIds.Add(parsed);
            }
            else if (ActionKeys.Contains(normalized))
            {
                ActionIds.Add(parsed);
            }
        }

        public void InferFromText(string text)
        {
            foreach (var (label, keywords) in InteractionKeywords)
            {
                if (keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    InteractionKinds.Add(label);
                }
            }
        }
    }
}

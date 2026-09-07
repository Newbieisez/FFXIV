using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EZBuddy.Core.Research;

public sealed record PluginMechanicReferenceManifest(
    string SourceLabel,
    IReadOnlyList<PluginMechanicReferenceEntry> Entries,
    int SchemaVersion = 1)
{
    public int FileCount => Entries.Count;
    public int MechanicLikeFileCount => Entries.Count(entry => entry.MechanicKinds.Count > 0 || entry.TotalReferencedIds > 0);
}

public sealed record PluginMechanicReferenceEntry(
    string RelativePath,
    string FileName,
    string Sha256,
    IReadOnlyList<uint> ActionIds,
    IReadOnlyList<uint> StatusIds,
    IReadOnlyList<uint> TetherIds,
    IReadOnlyList<uint> IconIds,
    IReadOnlyList<uint> MapEffectIds,
    IReadOnlyList<uint> NpcIds,
    IReadOnlyList<uint> ObjectIds,
    IReadOnlyList<string> MechanicKinds)
{
    public int TotalReferencedIds =>
        ActionIds.Count + StatusIds.Count + TetherIds.Count + IconIds.Count + MapEffectIds.Count + NpcIds.Count + ObjectIds.Count;
}

public sealed record PluginMechanicReferenceScanOptions(
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
/// Inventories mechanic metadata from RebornBuddy plugin/profile source without exporting source
/// bodies, coordinates, vectors, destinations, or executable mechanic implementations.
/// </summary>
public sealed partial class PluginMechanicReferenceScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xml", ".json"
    };

    private static readonly (string Label, string[] Keywords)[] MechanicKeywords =
    [
        ("Avoidance", ["avoidancemanager", "addavoid", "avoidobjectinfo", "avoidlocation", "sidestep"]),
        ("GazeFacing", ["gaze", "lookaway", "look away", "turnaway", "turn away", "facing"]),
        ("Stack", ["stackmarker", "stack marker", "party stack", "stack mechanic"]),
        ("Spread", ["spreadmarker", "spread marker", "spread mechanic"]),
        ("Knockback", ["knockback", "knock back"]),
        ("LineOfSight", ["lineofsight", "line of sight", "loscheck", "los check"]),
        ("Tether", ["tether"]),
        ("Status", ["statusid", "status id", "debuff", "buffid", "buff id"]),
        ("IconMarker", ["iconid", "icon id", "headmarker", "head marker", "targeticon", "target icon"]),
        ("MapEffect", ["mapeffect", "map effect", "envcontrol", "environmentcontrol", "environment control"]),
        ("Interrupt", ["interrupt"]),
        ("Targeting", ["targetswap", "target swap", "targetpriority", "target priority"]),
        ("DoorSwitch", ["door", "gate", "switch", "lever"]),
        ("ChestLoot", ["chest", "coffer", "loot"]),
        ("DutyEntry", ["dawnstory", "dutysupport", "duty support", "trust", "gcarmy", "squadron"])
    ];

    private readonly PluginMechanicReferenceScanOptions _options;

    public PluginMechanicReferenceScanner(PluginMechanicReferenceScanOptions? options = null)
    {
        _options = options ?? new PluginMechanicReferenceScanOptions();
        _options.Validate();
    }

    public PluginMechanicReferenceManifest Scan(string sourceLabel, IEnumerable<string> roots)
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

        var entries = new List<PluginMechanicReferenceEntry>();
        foreach (var root in normalizedRoots)
        {
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Plugin mechanic reference scan root does not exist: {root}");
            }

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (entries.Count >= _options.MaximumFiles)
                {
                    throw new InvalidDataException($"Plugin mechanic reference scan exceeded the {_options.MaximumFiles:N0}-file safety limit.");
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
                    : Path.Combine(
                        Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                        Path.GetRelativePath(root, path));

                try
                {
                    entries.Add(ScanFile(path, NormalizeRelativePath(relative)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
                {
                    // One malformed/unreadable source should not abort the metadata inventory.
                }
            }
        }

        return new PluginMechanicReferenceManifest(
            sourceLabel.Trim(),
            entries
                .Where(entry => entry.MechanicKinds.Count > 0 || entry.TotalReferencedIds > 0)
                .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public PluginMechanicReferenceEntry ScanFile(string path, string? relativePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Plugin mechanic reference source file was not found.", fullPath);
        }

        if (info.Length > _options.MaximumFileBytes)
        {
            throw new InvalidDataException($"Source file exceeds the {_options.MaximumFileBytes:N0}-byte safety limit.");
        }

        if (!SupportedExtensions.Contains(info.Extension))
        {
            throw new InvalidDataException($"Unsupported plugin mechanic reference extension '{info.Extension}'.");
        }

        var bytes = File.ReadAllBytes(fullPath);
        var text = new UTF8Encoding(false, true).GetString(bytes);
        var ids = new ReferenceIdAccumulator();

        foreach (Match match in NamedNumericRegex().Matches(text))
        {
            ids.Observe(match.Groups["name"].Value, match.Groups["value"].Value);
        }

        foreach (Match match in DeclaredNumericRegex().Matches(text))
        {
            ids.Observe(match.Groups["name"].Value, match.Groups["value"].Value);
        }

        var mechanicKinds = MechanicKeywords
            .Where(entry => entry.Keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PluginMechanicReferenceEntry(
            NormalizeRelativePath(relativePath ?? info.Name),
            info.Name,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ids.ActionIds.Order().ToArray(),
            ids.StatusIds.Order().ToArray(),
            ids.TetherIds.Order().ToArray(),
            ids.IconIds.Order().ToArray(),
            ids.MapEffectIds.Order().ToArray(),
            ids.NpcIds.Order().ToArray(),
            ids.ObjectIds.Order().ToArray(),
            mechanicKinds);
    }

    public static string Serialize(PluginMechanicReferenceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string NormalizeKey(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static uint? ParseUnsigned(string raw)
    {
        var value = raw.Trim().TrimEnd('u', 'U', 'l', 'L');
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var parsedHex)
                ? parsedHex
                : null;
        }

        return uint.TryParse(value, out var parsed) ? parsed : null;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<name>[A-Za-z_][A-Za-z0-9_]{1,80})\s*(?:=|:)\s*(?<value>(?:0x[0-9A-Fa-f]+)|(?:\d+))(?:[uUlL]*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NamedNumericRegex();

    [GeneratedRegex(@"\b(?:const|static|readonly|private|public|internal|protected|uint|int|long|short|ushort|ulong|var|byte)\s+(?:(?:const|static|readonly|private|public|internal|protected|uint|int|long|short|ushort|ulong|var|byte)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]{1,80})\s*=\s*(?<value>(?:0x[0-9A-Fa-f]+)|(?:\d+))(?:[uUlL]*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DeclaredNumericRegex();

    private sealed class ReferenceIdAccumulator
    {
        public HashSet<uint> ActionIds { get; } = [];
        public HashSet<uint> StatusIds { get; } = [];
        public HashSet<uint> TetherIds { get; } = [];
        public HashSet<uint> IconIds { get; } = [];
        public HashSet<uint> MapEffectIds { get; } = [];
        public HashSet<uint> NpcIds { get; } = [];
        public HashSet<uint> ObjectIds { get; } = [];

        public void Observe(string name, string rawValue)
        {
            var parsed = ParseUnsigned(rawValue);
            if (parsed is null or 0)
            {
                return;
            }

            var key = NormalizeKey(name);
            if (ContainsAny(key, "status", "debuff", "buff", "aura"))
            {
                StatusIds.Add(parsed.Value);
            }
            else if (ContainsAny(key, "tether"))
            {
                TetherIds.Add(parsed.Value);
            }
            else if (ContainsAny(key, "headmarker", "targeticon", "icon", "markerid"))
            {
                IconIds.Add(parsed.Value);
            }
            else if (ContainsAny(key, "mapeffect", "envcontrol", "environmentcontrol"))
            {
                MapEffectIds.Add(parsed.Value);
            }
            else if (ContainsAny(key, "action", "spell", "cast", "ability", "weaponskill"))
            {
                ActionIds.Add(parsed.Value);
            }
            else if (ContainsAny(key, "npc", "boss", "enemy", "battlecharacter", "actorid"))
            {
                NpcIds.Add(parsed.Value);
            }
            else if (ContainsAny(key, "object", "dataid", "door", "gate", "switch", "lever", "lift", "chest", "coffer", "interact"))
            {
                ObjectIds.Add(parsed.Value);
            }
        }

        private static bool ContainsAny(string value, params string[] terms)
            => terms.Any(value.Contains);
    }
}

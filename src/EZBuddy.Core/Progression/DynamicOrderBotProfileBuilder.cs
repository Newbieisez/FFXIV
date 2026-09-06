using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EZBuddy.Core.Progression;

public sealed record GeneratedOrderBotProfile(
    string ProfileName,
    string Xml,
    IReadOnlyList<string> IncludedNodeIds,
    IReadOnlyList<string> OmittedNodeIds);

public sealed class DynamicOrderBotProfileBuilder
{
    public GeneratedOrderBotProfile Build(
        string profileName,
        IEnumerable<ProgressionNodeInspection> pendingNodes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        var safeName = SanitizeProfileName(profileName);
        var pending = pendingNodes?.ToArray() ?? throw new ArgumentNullException(nameof(pendingNodes));

        var order = new XElement("Order");
        var included = new List<string>();
        var omitted = new List<string>();

        foreach (var inspection in pending)
        {
            var node = inspection.Node;
            if (inspection.State == ProgressionCompletionState.Completed)
            {
                continue;
            }

            var behavior = BuildNodeBehavior(node);
            if (behavior is null)
            {
                omitted.Add(node.Id);
                continue;
            }

            order.Add(behavior);
            included.Add(node.Id);
        }

        if (included.Count == 0)
        {
            order.Add(new XElement("LogMessage", new XAttribute("Message", "EZBuddy generated no automatable progression steps for this checklist.")));
        }

        var profile = new XElement(
            "Profile",
            new XElement("Name", safeName),
            new XElement("KillRadius", "50"),
            new XElement("BehaviorDirectory", "Quest Behaviors"),
            order);

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), profile);
        return new GeneratedOrderBotProfile(
            safeName,
            Serialize(document),
            included,
            omitted);
    }

    public async Task<string> SaveTempAsync(
        GeneratedOrderBotProfile generated,
        string? rootDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generated);

        var root = rootDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Path.GetTempPath(), "EZBuddy", "GeneratedProfiles");
        }

        Directory.CreateDirectory(root);
        var fileName = $"{SanitizeFileName(generated.ProfileName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.xml";
        var path = Path.Combine(root, fileName);

        await File.WriteAllTextAsync(path, generated.Xml, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static XElement? BuildNodeBehavior(ProgressionNode node)
    {
        return node.AutomationKind switch
        {
            ProgressionAutomationKind.QuestPickup => BuildQuestPickup(node),
            ProgressionAutomationKind.AetherCurrentInteract => BuildAetherCurrent(node),
            _ => null
        };
    }

    private static XElement BuildQuestPickup(ProgressionNode node)
    {
        if (node.SheetId == 0 || node.NpcId == 0)
        {
            throw new InvalidDataException($"Quest node '{node.Id}' is missing QuestId or NpcId.");
        }

        var condition = new XElement(
            "If",
            new XAttribute("Condition", $"not IsQuestCompleted({node.SheetId})"));

        AppendTeleportIfAvailable(condition, node);

        var pickupCondition = new XElement(
            "If",
            new XAttribute("Condition", $"not QuestLogManager.HasQuest({node.SheetId}) and IsQuestAcceptQualified({node.SheetId})"),
            new XElement(
                "PickupQuest",
                new XAttribute("NpcId", node.NpcId),
                new XAttribute("QuestId", node.SheetId),
                new XAttribute("XYZ", FormatXYZ(node))));

        condition.Add(pickupCondition);
        condition.Add(new XElement(
            "LogMessage",
            new XAttribute("Message", $"EZBuddy loaded quest '{EscapeLogText(node.Name)}'. Completion is delegated to catalog-approved quest/profile logic.")));

        return condition;
    }

    private static XElement BuildAetherCurrent(ProgressionNode node)
    {
        if (node.NpcId == 0)
        {
            throw new InvalidDataException($"Aether current node '{node.Id}' is missing its interactable object ID.");
        }

        var wrapper = new XElement("If", new XAttribute("Condition", "True"));
        AppendTeleportIfAvailable(wrapper, node);
        wrapper.Add(new XElement("MoveTo", new XAttribute("XYZ", FormatXYZ(node))));
        wrapper.Add(new XElement(
            "InteractWith",
            new XAttribute("NpcId", node.NpcId),
            new XAttribute("XYZ", FormatXYZ(node))));
        return wrapper;
    }

    private static void AppendTeleportIfAvailable(XElement parent, ProgressionNode node)
    {
        if (node.AetheryteId == 0)
        {
            return;
        }

        parent.Add(new XElement(
            "If",
            new XAttribute("Condition", $"Managers.WorldManager.HasAetheryteId({node.AetheryteId})"),
            new XElement(
                "TeleportTo",
                new XAttribute("Name", node.Name),
                new XAttribute("AetheryteId", node.AetheryteId))));
    }

    private static string FormatXYZ(ProgressionNode node)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{node.X:0.###}, {node.Y:0.###}, {node.Z:0.###}");

    private static string Serialize(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(false),
            NewLineHandling = NewLineHandling.Entitize
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string SanitizeProfileName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Profile name must contain 1-100 characters.");
        }

        return new string(trimmed.Where(ch => !char.IsControl(ch)).ToArray());
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "EZBuddyProgression" : safe;
    }

    private static string EscapeLogText(string value)
        => value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

using EZBuddy.Core.Engine;
using EZBuddy.Core.Progression;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Adapters;

namespace EZBuddy.RebornBuddy.Progression;

public sealed record ProgressionPlanResult(
    CompletionReport Report,
    ProgressionChecklist Checklist,
    GeneratedOrderBotProfile? GeneratedProfile,
    string? GeneratedProfilePath,
    Guid? QueuedActivityId,
    IReadOnlyList<string> ManualOrUnsupportedNodeIds);

public sealed class ProgressionPlannerService
{
    private readonly RebornBuddyProgressionScanner _scanner;
    private readonly DynamicOrderBotProfileBuilder _profileBuilder;
    private readonly OrderBotAdapter _orderBotAdapter;

    public ProgressionPlannerService(
        RebornBuddyProgressionScanner? scanner = null,
        DynamicOrderBotProfileBuilder? profileBuilder = null,
        OrderBotAdapter? orderBotAdapter = null)
    {
        _scanner = scanner ?? new RebornBuddyProgressionScanner();
        _profileBuilder = profileBuilder ?? new DynamicOrderBotProfileBuilder();
        _orderBotAdapter = orderBotAdapter ?? new OrderBotAdapter();
    }

    public async Task<ProgressionPlanResult> ScanBuildAndQueueAsync(
        string profileName,
        IEnumerable<ProgressionNode>? definitions = null,
        int priority = 50,
        CancellationToken cancellationToken = default)
    {
        var catalog = (definitions ?? ProgressionCatalog.All).ToArray();
        var inspections = new List<ProgressionNodeInspection>(catalog.Length);

        foreach (var node in catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inspections.Add(await _scanner.InspectAsync(node, cancellationToken).ConfigureAwait(false));
        }

        var evaluator = new ProgressionChecklistEvaluator();
        var report = evaluator.BuildReport(inspections);
        var checklist = evaluator.BuildChecklist(inspections);

        var dynamicNodes = checklist.AutomatableNodes
            .Where(x => x.Node.AutomationKind is ProgressionAutomationKind.QuestPickup or ProgressionAutomationKind.AetherCurrentInteract)
            .ToArray();

        GeneratedOrderBotProfile? generated = null;
        string? generatedPath = null;
        Guid? queuedActivityId = null;

        if (dynamicNodes.Length > 0)
        {
            generated = _profileBuilder.Build(profileName, dynamicNodes);
            generatedPath = await _profileBuilder.SaveTempAsync(generated, cancellationToken: cancellationToken).ConfigureAwait(false);

            var activity = new OrderBotProfileActivity(
                _orderBotAdapter,
                generatedPath,
                $"Progression: {generated.ProfileName}");

            EZBuddyRuntime.Queue.Enqueue(new ActivityQueueItem(
                activity,
                Priority: priority,
                MaxRetries: 2,
                ContinueOnFailure: false,
                StopConditionLabel: "Stop when generated progression profile completes"));

            queuedActivityId = activity.Id;
        }

        foreach (var existingProfileNode in checklist.AutomatableNodes.Where(x => x.Node.AutomationKind == ProgressionAutomationKind.ExistingProfile))
        {
            var path = existingProfileNode.Node.ExistingProfilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            var activity = new OrderBotProfileActivity(
                _orderBotAdapter,
                path,
                $"Progression: {existingProfileNode.Node.Name}");

            EZBuddyRuntime.Queue.Enqueue(new ActivityQueueItem(
                activity,
                Priority: priority - 1,
                MaxRetries: 2,
                ContinueOnFailure: false,
                StopConditionLabel: $"Complete {existingProfileNode.Node.Name}"));
        }

        var unsupported = checklist.ManualNodes
            .Concat(checklist.BlockedNodes)
            .Select(x => x.Node.Id)
            .Concat(generated?.OmittedNodeIds ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProgressionPlanResult(
            report,
            checklist,
            generated,
            generatedPath,
            queuedActivityId,
            unsupported);
    }
}

namespace EZBuddy.Core.Routines;

public enum WondrousTailsPlanAction
{
    PickUpJournal,
    RunObjective,
    UseRetry,
    TurnInJournal,
    ManualReview
}

public sealed record WondrousTailsObjectiveSnapshot(
    string Key,
    string DisplayName,
    bool Completed,
    IReadOnlyList<uint> EligibleQueueDutyIds,
    bool HasVerifiedEzBuddyRoute,
    bool HasVerifiedOrderBotProfile = false,
    int EstimatedMinutes = 30,
    int Priority = 0)
{
    public bool IsAutomatable =>
        EligibleQueueDutyIds.Count > 0 &&
        (HasVerifiedEzBuddyRoute || HasVerifiedOrderBotProfile);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentNullException.ThrowIfNull(EligibleQueueDutyIds);
        if (EligibleQueueDutyIds.Any(id => id == 0))
        {
            throw new InvalidDataException($"Wondrous Tails objective '{DisplayName}' contains a zero duty ID.");
        }

        if (EstimatedMinutes is < 1 or > 600)
        {
            throw new InvalidDataException($"Wondrous Tails objective '{DisplayName}' has an invalid duration estimate.");
        }
    }
}

public sealed record WondrousTailsJournalSnapshot(
    bool HasJournal,
    int SealsPlaced,
    int SecondChancePoints,
    IReadOnlyList<WondrousTailsObjectiveSnapshot> Objectives,
    IReadOnlyList<bool>? SealGrid = null,
    DateTimeOffset? ExpiresUtc = null)
{
    public void Validate()
    {
        if (SealsPlaced is < 0 or > WondrousTailsPlanner.MaximumSeals)
        {
            throw new InvalidDataException($"Wondrous Tails seals must be between 0 and {WondrousTailsPlanner.MaximumSeals}.");
        }

        if (SecondChancePoints is < 0 or > WondrousTailsPlanner.MaximumSecondChancePoints)
        {
            throw new InvalidDataException($"Wondrous Tails Second Chance points must be between 0 and {WondrousTailsPlanner.MaximumSecondChancePoints}.");
        }

        ArgumentNullException.ThrowIfNull(Objectives);
        if (HasJournal && Objectives.Count != WondrousTailsPlanner.ObjectiveCount)
        {
            throw new InvalidDataException($"An active Wondrous Tails journal must contain exactly {WondrousTailsPlanner.ObjectiveCount} objective squares.");
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var objective in Objectives)
        {
            objective.Validate();
            if (!keys.Add(objective.Key))
            {
                throw new InvalidDataException($"Duplicate Wondrous Tails objective key '{objective.Key}'.");
            }
        }

        if (SealGrid is not null)
        {
            if (SealGrid.Count != 16)
            {
                throw new InvalidDataException("Wondrous Tails seal grid must contain exactly 16 cells.");
            }

            if (SealGrid.Count(value => value) != SealsPlaced)
            {
                throw new InvalidDataException("Wondrous Tails seal-grid count does not match SealsPlaced.");
            }
        }
    }
}

public sealed record WondrousTailsPlannerOptions(
    int TargetSeals = WondrousTailsPlanner.MaximumSeals,
    int MaximumDutiesThisRun = WondrousTailsPlanner.MaximumSeals,
    bool AllowSecondChanceRetry = false,
    IReadOnlySet<string>? EnabledObjectiveKeys = null)
{
    public IReadOnlySet<string> EffectiveEnabledObjectiveKeys =>
        EnabledObjectiveKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        if (TargetSeals is < 1 or > WondrousTailsPlanner.MaximumSeals)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetSeals));
        }

        if (MaximumDutiesThisRun is < 1 or > WondrousTailsPlanner.MaximumSeals)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumDutiesThisRun));
        }
    }
}

public sealed record WondrousTailsPlanStep(
    WondrousTailsPlanAction Action,
    string Message,
    string? ObjectiveKey = null,
    IReadOnlyList<uint>? EligibleQueueDutyIds = null);

public sealed record WondrousTailsPlan(
    IReadOnlyList<WondrousTailsPlanStep> Steps,
    int CurrentSeals,
    int TargetSeals,
    int CurrentLines,
    bool ShuffleIsAvailable,
    IReadOnlyList<string> UnautomatableObjectives,
    string Summary);

/// <summary>
/// Plans Wondrous Tails work only from the live journal snapshot supplied by the host. Objective
/// IDs and duty mappings are never guessed. Only objectives with verified EZBuddy routes or
/// explicitly verified OrderBot profiles are eligible for automatic duty execution.
/// </summary>
public static class WondrousTailsPlanner
{
    public const int ObjectiveCount = 16;
    public const int MaximumSeals = 9;
    public const int MaximumSecondChancePoints = 9;
    public const int RetryCost = 1;
    public const int ShuffleCost = 2;

    private static readonly int[][] SealLines =
    [
        [0, 1, 2, 3], [4, 5, 6, 7], [8, 9, 10, 11], [12, 13, 14, 15],
        [0, 4, 8, 12], [1, 5, 9, 13], [2, 6, 10, 14], [3, 7, 11, 15],
        [0, 5, 10, 15], [3, 6, 9, 12]
    ];

    public static WondrousTailsPlan Build(
        WondrousTailsJournalSnapshot snapshot,
        WondrousTailsPlannerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        options ??= new WondrousTailsPlannerOptions();
        options.Validate();

        if (!snapshot.HasJournal)
        {
            return new WondrousTailsPlan(
                [new WondrousTailsPlanStep(WondrousTailsPlanAction.PickUpJournal, "Pick up the current Wondrous Tails journal from Khloe before planning duties.")],
                0,
                options.TargetSeals,
                0,
                false,
                [],
                "No active Wondrous Tails journal is present.");
        }

        var currentLines = CountLines(snapshot.SealGrid);
        var shuffleAvailable = snapshot.SealsPlaced is >= 3 and <= 7 && snapshot.SecondChancePoints >= ShuffleCost;

        if (snapshot.SealsPlaced >= options.TargetSeals)
        {
            var action = snapshot.SealsPlaced >= MaximumSeals
                ? WondrousTailsPlanAction.TurnInJournal
                : WondrousTailsPlanAction.ManualReview;
            var message = snapshot.SealsPlaced >= MaximumSeals
                ? "Nine seals are present; the journal is ready for turn-in."
                : $"Target of {options.TargetSeals} seals is already satisfied.";
            return new WondrousTailsPlan(
                [new WondrousTailsPlanStep(action, message)],
                snapshot.SealsPlaced,
                options.TargetSeals,
                currentLines,
                shuffleAvailable,
                [],
                message);
        }

        var needed = Math.Min(options.TargetSeals - snapshot.SealsPlaced, options.MaximumDutiesThisRun);
        var incomplete = snapshot.Objectives.Where(objective => !objective.Completed).ToArray();
        var enabled = incomplete
            .Where(objective => options.EffectiveEnabledObjectiveKeys.Count == 0 || options.EffectiveEnabledObjectiveKeys.Contains(objective.Key))
            .ToArray();
        var automatable = enabled
            .Where(objective => objective.IsAutomatable)
            .OrderByDescending(objective => objective.Priority)
            .ThenBy(objective => objective.EstimatedMinutes)
            .ThenBy(objective => objective.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(needed)
            .ToArray();

        var steps = automatable
            .Select(objective => new WondrousTailsPlanStep(
                WondrousTailsPlanAction.RunObjective,
                $"Run '{objective.DisplayName}' using a verified automation path.",
                objective.Key,
                objective.EligibleQueueDutyIds))
            .ToList();

        var unautomatable = enabled
            .Where(objective => !objective.IsAutomatable)
            .Select(objective => objective.DisplayName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (steps.Count < needed &&
            options.AllowSecondChanceRetry &&
            snapshot.SecondChancePoints >= RetryCost)
        {
            var retryCandidate = snapshot.Objectives
                .Where(objective => objective.Completed && objective.IsAutomatable)
                .Where(objective => options.EffectiveEnabledObjectiveKeys.Count == 0 || options.EffectiveEnabledObjectiveKeys.Contains(objective.Key))
                .OrderByDescending(objective => objective.Priority)
                .ThenBy(objective => objective.EstimatedMinutes)
                .FirstOrDefault();

            if (retryCandidate is not null)
            {
                steps.Add(new WondrousTailsPlanStep(
                    WondrousTailsPlanAction.UseRetry,
                    $"Use one Second Chance Retry on verified objective '{retryCandidate.DisplayName}', then rescan the journal before scheduling another duty because Retry changes another objective's completion state.",
                    retryCandidate.Key,
                    retryCandidate.EligibleQueueDutyIds));
            }
        }

        if (steps.Count == 0)
        {
            steps.Add(new WondrousTailsPlanStep(
                WondrousTailsPlanAction.ManualReview,
                "No incomplete objective has a verified EZBuddy route/profile. Record or verify a route before automatic execution."));
        }

        var summary = $"Wondrous Tails has {snapshot.SealsPlaced}/{options.TargetSeals} target seals; planned {steps.Count(step => step.Action == WondrousTailsPlanAction.RunObjective)} verified duty run(s).";
        if (shuffleAvailable)
        {
            summary += " Shuffle is currently available, but EZBuddy leaves Shuffle as a review decision rather than spending Second Chance points automatically.";
        }

        return new WondrousTailsPlan(
            steps,
            snapshot.SealsPlaced,
            options.TargetSeals,
            currentLines,
            shuffleAvailable,
            unautomatable,
            summary);
    }

    public static int CountLines(IReadOnlyList<bool>? sealGrid)
    {
        if (sealGrid is null)
        {
            return 0;
        }

        if (sealGrid.Count != 16)
        {
            throw new ArgumentException("Wondrous Tails seal grid must contain 16 cells.", nameof(sealGrid));
        }

        return SealLines.Count(line => line.All(index => sealGrid[index]));
    }
}

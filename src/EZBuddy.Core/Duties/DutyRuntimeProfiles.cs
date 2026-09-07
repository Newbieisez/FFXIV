namespace EZBuddy.Core.Duties;

public readonly record struct DutyPoint(float X, float Y, float Z);

public enum DutyObjectiveKind
{
    Waypoint,
    Interact,
    Door,
    Switch,
    Lift,
    Chest,
    BossBoundary
}

public sealed record DutyObjectiveNode(
    string Id,
    DutyObjectiveKind Kind,
    DutyPoint Position,
    float Radius = 3f,
    uint? ObjectId = null,
    bool Required = true,
    string? Notes = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
        {
            throw new InvalidDataException($"Duty objective '{Id}' contains a non-finite coordinate.");
        }

        if (Radius <= 0 || Radius > 100)
        {
            throw new InvalidDataException($"Duty objective '{Id}' has an invalid radius of {Radius}.");
        }

        if (Kind is DutyObjectiveKind.Interact or DutyObjectiveKind.Door or DutyObjectiveKind.Switch or DutyObjectiveKind.Lift && ObjectId is null or 0)
        {
            throw new InvalidDataException($"Duty objective '{Id}' requires a non-zero object ID.");
        }
    }
}

public enum BossMechanicKind
{
    AvoidRadius,
    SafeSpot,
    Gaze,
    Stack,
    Spread,
    Proximity,
    Tether,
    InterruptibleCast,
    CustomProvider
}

public sealed record BossMechanicRule(
    string Id,
    BossMechanicKind Kind,
    uint? ActionId = null,
    uint? ObjectId = null,
    float Radius = 0,
    DutyPoint? SafePoint = null,
    string? ProviderKey = null,
    string? Notes = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);

        if (Radius < 0 || Radius > 100)
        {
            throw new InvalidDataException($"Boss mechanic '{Id}' has an invalid radius of {Radius}.");
        }

        if (Kind == BossMechanicKind.SafeSpot && SafePoint is null)
        {
            throw new InvalidDataException($"Boss mechanic '{Id}' requires a safe coordinate.");
        }

        if (Kind == BossMechanicKind.CustomProvider && string.IsNullOrWhiteSpace(ProviderKey))
        {
            throw new InvalidDataException($"Boss mechanic '{Id}' requires an allowlisted provider key.");
        }
    }
}

public sealed record BossMechanicProfile(
    uint BossNpcId,
    string Name,
    IReadOnlyList<BossMechanicRule> Rules)
{
    public void Validate()
    {
        if (BossNpcId == 0)
        {
            throw new InvalidDataException("Boss mechanic profiles require a non-zero NPC ID.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        foreach (var rule in Rules)
        {
            rule.Validate();
        }
    }
}

public sealed record DutyNavigationProfile(
    uint DutyId,
    string Name,
    int MinimumLevel,
    int MaximumLevel,
    IReadOnlyList<DutyObjectiveNode> Objectives,
    IReadOnlyList<BossMechanicProfile> Bosses,
    string? SourceLabel = null)
{
    public void Validate()
    {
        if (DutyId == 0)
        {
            throw new InvalidDataException("Duty navigation profiles require a non-zero duty ID.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (MinimumLevel <= 0 || MaximumLevel < MinimumLevel || MaximumLevel > 100)
        {
            throw new InvalidDataException($"Duty profile '{Name}' has an invalid level range {MinimumLevel}-{MaximumLevel}.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var objective in Objectives)
        {
            objective.Validate();
            if (!ids.Add(objective.Id))
            {
                throw new InvalidDataException($"Duty profile '{Name}' contains duplicate objective ID '{objective.Id}'.");
            }
        }

        foreach (var boss in Bosses)
        {
            boss.Validate();
        }
    }
}

public enum DutyLootAction
{
    LeaveUnchanged,
    Greed,
    Pass
}

public sealed record DutyLootPolicy(
    DutyLootAction NormalAction = DutyLootAction.Greed,
    int PassAtOrBelowFreeSlots = 3)
{
    public void Validate()
    {
        if (PassAtOrBelowFreeSlots is < 0 or > 140)
        {
            throw new ArgumentOutOfRangeException(nameof(PassAtOrBelowFreeSlots));
        }
    }

    public DutyLootAction SelectAction(int freeInventorySlots)
    {
        if (freeInventorySlots < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(freeInventorySlots));
        }

        return freeInventorySlots <= PassAtOrBelowFreeSlots
            ? DutyLootAction.Pass
            : NormalAction;
    }
}

public sealed record DutyPostRunStatus(
    bool IsDutyComplete,
    bool IsLootWindowOpen,
    bool IsLeaveRequested,
    string Message);

public interface IDutyPostRunAdapter
{
    Task<DutyPostRunStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> ProcessLootAsync(
        DutyLootPolicy policy,
        int freeInventorySlots,
        CancellationToken cancellationToken = default);

    Task<bool> RequestLeaveAsync(CancellationToken cancellationToken = default);
}

public interface IDutyNavigationProfileCatalog
{
    IReadOnlyCollection<DutyNavigationProfile> All { get; }
    bool TryGet(uint dutyId, out DutyNavigationProfile? profile);
}

public sealed class DutyNavigationProfileCatalog : IDutyNavigationProfileCatalog
{
    private readonly IReadOnlyDictionary<uint, DutyNavigationProfile> _profiles;

    public DutyNavigationProfileCatalog(IEnumerable<DutyNavigationProfile>? profiles = null)
    {
        var list = (profiles ?? Array.Empty<DutyNavigationProfile>()).ToArray();
        foreach (var profile in list)
        {
            profile.Validate();
        }

        _profiles = list.ToDictionary(profile => profile.DutyId);
    }

    public IReadOnlyCollection<DutyNavigationProfile> All => _profiles.Values.ToArray();

    public bool TryGet(uint dutyId, out DutyNavigationProfile? profile)
        => _profiles.TryGetValue(dutyId, out profile);
}

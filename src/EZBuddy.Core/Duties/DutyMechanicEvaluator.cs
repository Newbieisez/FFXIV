namespace EZBuddy.Core.Duties;

public sealed record DutyMechanicObservation(
    uint BossNpcId,
    uint? CastActionId = null,
    uint? ObjectId = null);

public sealed record DutyMechanicDirective(
    BossMechanicRule Rule,
    bool RequiresMovementOverride,
    string Message);

public sealed class DutyMechanicEvaluator
{
    public DutyMechanicDirective? Evaluate(
        DutyNavigationProfile profile,
        DutyMechanicObservation observation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(observation);
        profile.Validate();

        var boss = profile.Bosses.FirstOrDefault(candidate => candidate.BossNpcId == observation.BossNpcId);
        if (boss is null)
        {
            return null;
        }

        var rule = boss.Rules.FirstOrDefault(candidate => Matches(candidate, observation));
        if (rule is null)
        {
            return null;
        }

        var movement = rule.Kind is
            BossMechanicKind.AvoidRadius or
            BossMechanicKind.SafeSpot or
            BossMechanicKind.Stack or
            BossMechanicKind.Spread or
            BossMechanicKind.Proximity or
            BossMechanicKind.Tether;

        return new DutyMechanicDirective(
            rule,
            movement,
            BuildMessage(boss, rule, movement));
    }

    private static bool Matches(BossMechanicRule rule, DutyMechanicObservation observation)
    {
        if (rule.ActionId is { } actionId && observation.CastActionId != actionId)
        {
            return false;
        }

        if (rule.ObjectId is { } objectId && observation.ObjectId != objectId)
        {
            return false;
        }

        return rule.ActionId.HasValue || rule.ObjectId.HasValue || rule.Kind == BossMechanicKind.CustomProvider;
    }

    private static string BuildMessage(
        BossMechanicProfile boss,
        BossMechanicRule rule,
        bool movement)
        => movement
            ? $"{boss.Name}: mechanic '{rule.Id}' requires a movement-priority directive ({rule.Kind}); combat rotation remains delegated to Magitek."
            : $"{boss.Name}: mechanic '{rule.Id}' matched ({rule.Kind}); no EZBuddy movement override is required by this rule.";
}

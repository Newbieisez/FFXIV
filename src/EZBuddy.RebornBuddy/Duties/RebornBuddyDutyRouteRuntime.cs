using Clio.Utilities;
using EZBuddy.Core.Duties;
using ff14bot.Managers;
using ff14bot.Navigation;
using ff14bot.Objects;
using ff14bot.Pathing;

namespace EZBuddy.RebornBuddy.Duties;

public sealed class RebornBuddyDutyRouteRecorderSource : IDutyRouteRecorderSource
{
    private const float InteractionInferenceRadius = 4f;
    private uint _previousNonCombatTargetNpcId;
    private DutyPoint _previousNonCombatTargetPosition;
    private string _previousNonCombatTargetName = string.Empty;
    private bool _previousNonCombatTargetWasTargetable;
    private bool _previousNonCombatTargetWasNear;

    public DutyRecorderFrame CaptureFrame(bool captureInteractionRequested)
    {
        var player = ff14bot.Core.Me ?? throw new InvalidOperationException("Player is unavailable.");
        var position = ToDutyPoint(player.Location);
        var currentTarget = player.CurrentTarget;
        var interaction = CaptureInteraction(currentTarget, position, captureInteractionRequested);
        var casts = CaptureCasts(player.InCombat, currentTarget, position);

        return new DutyRecorderFrame(
            TerritoryId: WorldManager.ZoneId,
            PlayerPosition: position,
            HeadingRadians: player.Heading,
            InCombat: player.InCombat,
            Timestamp: DateTimeOffset.UtcNow,
            Interaction: interaction,
            ActiveCasts: casts);
    }

    private DutyInteractionSnapshot? CaptureInteraction(GameObject? target, DutyPoint playerPosition, bool captureInteractionRequested)
    {
        DutyInteractionSnapshot? result = null;
        if (captureInteractionRequested && target is not null && target is not BattleCharacter)
        {
            result = ToInteraction(target, playerPosition);
        }
        else if (target is null && _previousNonCombatTargetNpcId != 0 && _previousNonCombatTargetWasTargetable && _previousNonCombatTargetWasNear)
        {
            result = new DutyInteractionSnapshot(
                _previousNonCombatTargetNpcId,
                _previousNonCombatTargetName,
                Classify(_previousNonCombatTargetName),
                _previousNonCombatTargetPosition,
                false,
                Distance(playerPosition, _previousNonCombatTargetPosition));
        }
        else if (target is not null && target is not BattleCharacter &&
                 target.NpcId == _previousNonCombatTargetNpcId &&
                 _previousNonCombatTargetWasTargetable && !target.IsTargetable && _previousNonCombatTargetWasNear)
        {
            result = ToInteraction(target, playerPosition);
        }

        if (target is not null && target is not BattleCharacter)
        {
            _previousNonCombatTargetNpcId = target.NpcId;
            _previousNonCombatTargetPosition = ToDutyPoint(target.Location);
            _previousNonCombatTargetName = target.Name ?? string.Empty;
            _previousNonCombatTargetWasTargetable = target.IsTargetable;
            _previousNonCombatTargetWasNear = Distance(playerPosition, _previousNonCombatTargetPosition) <= InteractionInferenceRadius;
        }
        else if (result is not null)
        {
            ResetPreviousTarget();
        }

        return result;
    }

    private static IReadOnlyList<DutyCastSnapshot> CaptureCasts(bool inCombat, GameObject? currentTarget, DutyPoint playerPosition)
    {
        if (!inCombat || currentTarget is not BattleCharacter target || !target.IsCasting)
        {
            return Array.Empty<DutyCastSnapshot>();
        }

        var actionId = target.SpellCastInfo.ActionId;
        if (target.NpcId == 0 || actionId == 0)
        {
            return Array.Empty<DutyCastSnapshot>();
        }

        var caster = ToDutyPoint(target.Location);
        var offset = new DutyPoint(
            playerPosition.X - caster.X,
            playerPosition.Y - caster.Y,
            playerPosition.Z - caster.Z);
        var remaining = (float)Math.Max(0, target.SpellCastInfo.RemainingCastTime.TotalSeconds);

        return
        [
            new DutyCastSnapshot(
                BossNpcId: target.NpcId,
                BossName: target.Name ?? $"NPC {target.NpcId}",
                ActionId: actionId,
                RemainingCastSeconds: remaining,
                CasterPosition: caster,
                PlayerRelativeOffset: offset)
        ];
    }

    private static DutyInteractionSnapshot ToInteraction(GameObject target, DutyPoint playerPosition)
    {
        var position = ToDutyPoint(target.Location);
        return new DutyInteractionSnapshot(
            target.NpcId,
            target.Name ?? string.Empty,
            Classify(target.Name ?? string.Empty),
            position,
            target.IsTargetable,
            Distance(playerPosition, position));
    }

    private static DutyObjectiveKind Classify(string name)
    {
        if (name.Contains("coffer", StringComparison.OrdinalIgnoreCase) || name.Contains("chest", StringComparison.OrdinalIgnoreCase)) return DutyObjectiveKind.Chest;
        if (name.Contains("lift", StringComparison.OrdinalIgnoreCase) || name.Contains("elevator", StringComparison.OrdinalIgnoreCase)) return DutyObjectiveKind.Lift;
        if (name.Contains("switch", StringComparison.OrdinalIgnoreCase) || name.Contains("lever", StringComparison.OrdinalIgnoreCase) || name.Contains("coral", StringComparison.OrdinalIgnoreCase)) return DutyObjectiveKind.Switch;
        if (name.Contains("door", StringComparison.OrdinalIgnoreCase) || name.Contains("gate", StringComparison.OrdinalIgnoreCase)) return DutyObjectiveKind.Door;
        return DutyObjectiveKind.Interact;
    }

    private void ResetPreviousTarget()
    {
        _previousNonCombatTargetNpcId = 0;
        _previousNonCombatTargetPosition = default;
        _previousNonCombatTargetName = string.Empty;
        _previousNonCombatTargetWasTargetable = false;
        _previousNonCombatTargetWasNear = false;
    }

    private static DutyPoint ToDutyPoint(Vector3 point) => new(point.X, point.Y, point.Z);

    private static float Distance(DutyPoint left, DutyPoint right)
    {
        var x = right.X - left.X;
        var y = right.Y - left.Y;
        var z = right.Z - left.Z;
        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }
}

public sealed class RebornBuddyDutyObjectiveNodeHost : IDutyObjectiveNodeHost
{
    public DutyNodeHostSnapshot ReadState()
    {
        var player = ff14bot.Core.Me ?? throw new InvalidOperationException("Player is unavailable.");
        return new DutyNodeHostSnapshot(
            WorldManager.ZoneId,
            new DutyPoint(player.Location.X, player.Location.Y, player.Location.Z),
            checked((int)InventoryManager.FreeSlots),
            player.InCombat);
    }

    public Task<bool> MoveTowardAsync(DutyPoint position, float arrivalRadius, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Navigator.MoveTo(new MoveToParameters(
            new Vector3(position.X, position.Y, position.Z),
            "EZBuddy duty objective")
        {
            UseMount = false
        });
        return Task.FromResult(true);
    }

    public Task<DutyInteractableState?> FindInteractableAsync(uint objectId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var player = ff14bot.Core.Me;
        if (player is null)
        {
            return Task.FromResult<DutyInteractableState?>(null);
        }

        var target = GameObjectManager.GetObjectsByNPCId<GameObject>(objectId)
            .OrderBy(candidate => Distance(player.Location, candidate.Location))
            .FirstOrDefault();
        if (target is null)
        {
            return Task.FromResult<DutyInteractableState?>(null);
        }

        return Task.FromResult<DutyInteractableState?>(new DutyInteractableState(
            objectId,
            new DutyPoint(target.Location.X, target.Location.Y, target.Location.Z),
            target.IsTargetable,
            target.IsVisible,
            Distance(player.Location, target.Location)));
    }

    public Task<bool> StopMovementAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Navigator.PlayerMover.MoveStop();
        return Task.FromResult(true);
    }

    public Task<bool> FaceAndInteractAsync(uint objectId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = GameObjectManager.GetObjectsByNPCId<GameObject>(objectId)
            .Where(candidate => candidate.IsVisible && candidate.IsTargetable)
            .OrderBy(candidate => candidate.Distance2D())
            .FirstOrDefault();
        if (target is null)
        {
            return Task.FromResult(false);
        }

        Navigator.PlayerMover.MoveStop();
        ff14bot.Core.Me.Face(target);
        target.Target();
        target.Interact();
        return Task.FromResult(true);
    }

    private static float Distance(Vector3 left, Vector3 right)
    {
        var x = right.X - left.X;
        var y = right.Y - left.Y;
        var z = right.Z - left.Z;
        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }
}

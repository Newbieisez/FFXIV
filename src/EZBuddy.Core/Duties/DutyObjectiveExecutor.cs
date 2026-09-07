namespace EZBuddy.Core.Duties;

public enum NodeExecutionResult
{
    InProgress,
    Completed,
    FailedRetryable,
    FailedFatal
}

public interface IDutyInInstanceRunner
{
    bool IsComplete { get; }
    Task<NodeExecutionResult> TickAsync(CancellationToken cancellationToken = default);
}

public sealed record DutyNodeHostSnapshot(
    uint TerritoryId,
    DutyPoint PlayerPosition,
    int FreeInventorySlots,
    bool InCombat);

public sealed record DutyInteractableState(
    uint ObjectId,
    DutyPoint Position,
    bool IsTargetable,
    bool IsVisible,
    float DistanceMeters);

public interface IDutyObjectiveNodeHost
{
    DutyNodeHostSnapshot ReadState();
    Task<bool> MoveTowardAsync(DutyPoint position, float arrivalRadius, CancellationToken cancellationToken = default);
    Task<DutyInteractableState?> FindInteractableAsync(uint objectId, CancellationToken cancellationToken = default);
    Task<bool> StopMovementAsync(CancellationToken cancellationToken = default);
    Task<bool> FaceAndInteractAsync(uint objectId, CancellationToken cancellationToken = default);
}

public sealed record DutyObjectiveExecutorOptions(
    int MinimumDutyFreeSlots = 5,
    int InteractionTimeoutSeconds = 8)
{
    public void Validate()
    {
        if (MinimumDutyFreeSlots is < 0 or > 140)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumDutyFreeSlots));
        }

        if (InteractionTimeoutSeconds is < 2 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(InteractionTimeoutSeconds));
        }
    }
}

public sealed class DutyObjectiveNodeExecutor
{
    private readonly IDutyObjectiveNodeHost _host;
    private readonly DutyObjectiveExecutorOptions _options;
    private string? _interactionNodeId;
    private DateTimeOffset? _interactionStartedAt;
    private bool _interactionIssued;
    private string? _bossBoundaryNodeId;
    private bool _bossCombatObserved;

    public DutyObjectiveNodeExecutor(
        IDutyObjectiveNodeHost host,
        DutyObjectiveExecutorOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new DutyObjectiveExecutorOptions();
        _options.Validate();
    }

    public DutyNodeHostSnapshot ReadState() => _host.ReadState();

    public async Task<NodeExecutionResult> ExecuteAsync(
        DutyObjectiveNode node,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        cancellationToken.ThrowIfCancellationRequested();
        node.Validate();

        var state = _host.ReadState();
        return node.Kind switch
        {
            DutyObjectiveKind.Waypoint => await ExecuteWaypointAsync(node, state, cancellationToken),
            DutyObjectiveKind.Chest => await ExecuteChestAsync(node, state, cancellationToken),
            DutyObjectiveKind.BossBoundary => await ExecuteBossBoundaryAsync(node, state, cancellationToken),
            DutyObjectiveKind.Interact or DutyObjectiveKind.Door or DutyObjectiveKind.Switch or DutyObjectiveKind.Lift
                => await ExecuteInteractionAsync(node, state, cancellationToken),
            _ => NodeExecutionResult.FailedFatal
        };
    }

    private async Task<NodeExecutionResult> ExecuteWaypointAsync(
        DutyObjectiveNode node,
        DutyNodeHostSnapshot state,
        CancellationToken cancellationToken)
    {
        ResetInteractionState();
        ResetBossBoundaryState();
        if (Distance(state.PlayerPosition, node.Position) <= node.Radius)
        {
            await _host.StopMovementAsync(cancellationToken);
            return NodeExecutionResult.Completed;
        }

        return await _host.MoveTowardAsync(node.Position, node.Radius, cancellationToken)
            ? NodeExecutionResult.InProgress
            : NodeExecutionResult.FailedRetryable;
    }

    private async Task<NodeExecutionResult> ExecuteChestAsync(
        DutyObjectiveNode node,
        DutyNodeHostSnapshot state,
        CancellationToken cancellationToken)
    {
        ResetBossBoundaryState();
        if (state.FreeInventorySlots <= _options.MinimumDutyFreeSlots)
        {
            ResetInteractionState();
            return NodeExecutionResult.Completed;
        }

        if (node.ObjectId is null or 0)
        {
            return NodeExecutionResult.Completed;
        }

        return await ExecuteInteractionAsync(node, state, cancellationToken);
    }

    private async Task<NodeExecutionResult> ExecuteBossBoundaryAsync(
        DutyObjectiveNode node,
        DutyNodeHostSnapshot state,
        CancellationToken cancellationToken)
    {
        ResetInteractionState();
        if (!string.Equals(_bossBoundaryNodeId, node.Id, StringComparison.Ordinal))
        {
            _bossBoundaryNodeId = node.Id;
            _bossCombatObserved = false;
        }

        if (state.InCombat)
        {
            _bossCombatObserved = true;
            return NodeExecutionResult.InProgress;
        }

        if (_bossCombatObserved)
        {
            ResetBossBoundaryState();
            return NodeExecutionResult.Completed;
        }

        if (Distance(state.PlayerPosition, node.Position) > node.Radius)
        {
            return await _host.MoveTowardAsync(node.Position, node.Radius, cancellationToken)
                ? NodeExecutionResult.InProgress
                : NodeExecutionResult.FailedRetryable;
        }

        await _host.StopMovementAsync(cancellationToken);
        return NodeExecutionResult.InProgress;
    }

    private async Task<NodeExecutionResult> ExecuteInteractionAsync(
        DutyObjectiveNode node,
        DutyNodeHostSnapshot state,
        CancellationToken cancellationToken)
    {
        ResetBossBoundaryState();
        if (node.ObjectId is null or 0)
        {
            return NodeExecutionResult.FailedFatal;
        }

        if (!string.Equals(_interactionNodeId, node.Id, StringComparison.Ordinal))
        {
            _interactionNodeId = node.Id;
            _interactionStartedAt = DateTimeOffset.UtcNow;
            _interactionIssued = false;
        }

        var target = await _host.FindInteractableAsync(node.ObjectId.Value, cancellationToken);
        if (target is null || !target.IsVisible || !target.IsTargetable)
        {
            ResetInteractionState();
            return NodeExecutionResult.Completed;
        }

        if (TimedOut())
        {
            ResetInteractionState();
            return NodeExecutionResult.FailedRetryable;
        }

        var distanceToNode = Distance(state.PlayerPosition, node.Position);
        if (distanceToNode > node.Radius || target.DistanceMeters > node.Radius)
        {
            return await _host.MoveTowardAsync(node.Position, node.Radius, cancellationToken)
                ? NodeExecutionResult.InProgress
                : NodeExecutionResult.FailedRetryable;
        }

        await _host.StopMovementAsync(cancellationToken);
        if (!_interactionIssued)
        {
            _interactionIssued = await _host.FaceAndInteractAsync(node.ObjectId.Value, cancellationToken);
            if (!_interactionIssued)
            {
                return NodeExecutionResult.FailedRetryable;
            }
        }

        return NodeExecutionResult.InProgress;
    }

    private bool TimedOut()
        => _interactionStartedAt is { } started &&
           DateTimeOffset.UtcNow - started > TimeSpan.FromSeconds(_options.InteractionTimeoutSeconds);

    private void ResetInteractionState()
    {
        _interactionNodeId = null;
        _interactionStartedAt = null;
        _interactionIssued = false;
    }

    private void ResetBossBoundaryState()
    {
        _bossBoundaryNodeId = null;
        _bossCombatObserved = false;
    }

    private static float Distance(DutyPoint left, DutyPoint right)
    {
        var x = right.X - left.X;
        var y = right.Y - left.Y;
        var z = right.Z - left.Z;
        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }
}

public sealed class DutyObjectiveRouteRunner : IDutyInInstanceRunner
{
    private readonly DutyNavigationProfile _profile;
    private readonly DutyObjectiveNodeExecutor _executor;
    private int _nodeIndex;

    public DutyObjectiveRouteRunner(
        DutyNavigationProfile profile,
        DutyObjectiveNodeExecutor executor)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _profile.Validate();
        if (!_profile.IsRunnable)
        {
            throw new InvalidOperationException("Duty route must have territory verification and at least one objective before it can run.");
        }
    }

    public int CurrentNodeIndex => _nodeIndex;
    public bool IsComplete => _nodeIndex >= _profile.Objectives.Count;
    public DutyObjectiveNode? CurrentNode => IsComplete ? null : _profile.Objectives[_nodeIndex];

    public async Task<NodeExecutionResult> TickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsComplete)
        {
            return NodeExecutionResult.Completed;
        }

        var state = _executor.ReadState();
        if (state.TerritoryId != _profile.TerritoryId)
        {
            return NodeExecutionResult.FailedFatal;
        }

        var node = _profile.Objectives[_nodeIndex];
        var result = await _executor.ExecuteAsync(node, cancellationToken);
        if (result == NodeExecutionResult.Completed)
        {
            _nodeIndex++;
            return IsComplete ? NodeExecutionResult.Completed : NodeExecutionResult.InProgress;
        }

        return result;
    }
}

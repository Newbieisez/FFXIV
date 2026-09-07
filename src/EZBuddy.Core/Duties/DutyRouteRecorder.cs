using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Duties;

public sealed record DutyRouteRecorderOptions(
    float MovementDistanceMeters = 3.5f,
    float HeadingDeltaDegrees = 35f,
    float DefaultArrivalRadius = 1.5f,
    float InteractionRadius = 3f)
{
    public void Validate()
    {
        if (MovementDistanceMeters is <= 0 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(MovementDistanceMeters));
        }

        if (HeadingDeltaDegrees is <= 0 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(HeadingDeltaDegrees));
        }

        if (DefaultArrivalRadius is <= 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultArrivalRadius));
        }

        if (InteractionRadius is <= 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(InteractionRadius));
        }
    }
}

public sealed record DutyInteractionSnapshot(
    uint ObjectId,
    string Name,
    DutyObjectiveKind SuggestedKind,
    DutyPoint Position,
    bool IsTargetable,
    float DistanceMeters);

public sealed record DutyCastSnapshot(
    uint BossNpcId,
    string BossName,
    uint ActionId,
    float RemainingCastSeconds,
    DutyPoint CasterPosition,
    DutyPoint PlayerRelativeOffset,
    DutyPoint? TargetDestination = null);

public sealed record DutyRecorderFrame(
    uint TerritoryId,
    DutyPoint PlayerPosition,
    float HeadingRadians,
    bool InCombat,
    DateTimeOffset Timestamp,
    DutyInteractionSnapshot? Interaction = null,
    IReadOnlyList<DutyCastSnapshot>? ActiveCasts = null);

public interface IDutyRouteRecorderSource
{
    DutyRecorderFrame CaptureFrame(bool captureInteractionRequested);
}

public interface IDutyRouteRecordingSink
{
    Task SaveAsync(DutyNavigationProfile profile, CancellationToken cancellationToken = default);
}

public sealed class DutyRouteRecorder
{
    private readonly uint _queueDutyId;
    private readonly uint _territoryId;
    private readonly string _name;
    private readonly int _minimumLevel;
    private readonly int _maximumLevel;
    private readonly DutyRouteRecorderOptions _options;
    private readonly List<DutyObjectiveNode> _nodes = [];
    private readonly Dictionary<uint, List<BossMechanicRule>> _bossRules = [];
    private readonly Dictionary<uint, string> _bossNames = [];
    private readonly HashSet<(uint BossNpcId, uint ActionId)> _capturedCasts = [];
    private DutyPoint? _lastNodePosition;
    private float? _lastNodeHeading;
    private bool _wasInCombat;
    private int _nextNodeId = 1;

    public DutyRouteRecorder(
        uint queueDutyId,
        uint territoryId,
        string name,
        int minimumLevel,
        int maximumLevel,
        DutyRouteRecorderOptions? options = null)
    {
        if (queueDutyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueDutyId));
        }

        if (territoryId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(territoryId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (minimumLevel <= 0 || maximumLevel < minimumLevel || maximumLevel > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLevel));
        }

        _queueDutyId = queueDutyId;
        _territoryId = territoryId;
        _name = name.Trim();
        _minimumLevel = minimumLevel;
        _maximumLevel = maximumLevel;
        _options = options ?? new DutyRouteRecorderOptions();
        _options.Validate();
    }

    public int NodeCount => _nodes.Count;
    public int CapturedCastCount => _capturedCasts.Count;

    public void Sample(DutyRecorderFrame frame)
    {
        if (frame.TerritoryId != _territoryId)
        {
            throw new InvalidOperationException(
                $"Recorder territory mismatch. Expected {_territoryId}, observed {frame.TerritoryId}.");
        }

        CaptureMovement(frame);
        CaptureCombatBoundary(frame);
        CaptureInteraction(frame.Interaction);
        CaptureCasts(frame.ActiveCasts ?? Array.Empty<DutyCastSnapshot>());
        _wasInCombat = frame.InCombat;
    }

    public DutyNavigationProfile BuildProfile()
    {
        var bosses = _bossRules
            .OrderBy(pair => pair.Key)
            .Select(pair => new BossMechanicProfile(
                pair.Key,
                _bossNames[pair.Key],
                pair.Value.ToArray()))
            .ToArray();

        var profile = new DutyNavigationProfile(
            DutyId: _queueDutyId,
            Name: _name,
            MinimumLevel: _minimumLevel,
            MaximumLevel: _maximumLevel,
            Objectives: _nodes.ToArray(),
            Bosses: bosses,
            SourceLabel: "EZBuddy clean-room route recorder",
            TerritoryId: _territoryId);
        profile.Validate();
        return profile;
    }

    private void CaptureMovement(DutyRecorderFrame frame)
    {
        if (_lastNodePosition is null)
        {
            AddNode(DutyObjectiveKind.Waypoint, frame.PlayerPosition, _options.DefaultArrivalRadius, notes: "Recorder start.");
            _lastNodeHeading = frame.HeadingRadians;
            return;
        }

        var distance = Distance(_lastNodePosition.Value, frame.PlayerPosition);
        var headingDelta = _lastNodeHeading.HasValue
            ? HeadingDeltaDegrees(_lastNodeHeading.Value, frame.HeadingRadians)
            : 0f;

        if (distance > _options.MovementDistanceMeters || headingDelta > _options.HeadingDeltaDegrees)
        {
            AddNode(DutyObjectiveKind.Waypoint, frame.PlayerPosition, _options.DefaultArrivalRadius);
            _lastNodeHeading = frame.HeadingRadians;
        }
    }

    private void CaptureCombatBoundary(DutyRecorderFrame frame)
    {
        if (!_wasInCombat && frame.InCombat)
        {
            AddNode(
                DutyObjectiveKind.BossBoundary,
                frame.PlayerPosition,
                _options.DefaultArrivalRadius,
                notes: "Combat transition observed; validate whether this is a boss arena boundary before release.");
        }
    }

    private void CaptureInteraction(DutyInteractionSnapshot? interaction)
    {
        if (interaction is null || interaction.ObjectId == 0 || interaction.DistanceMeters > _options.InteractionRadius)
        {
            return;
        }

        if (_nodes.Any(node => node.ObjectId == interaction.ObjectId &&
                               Distance(node.Position, interaction.Position) <= _options.InteractionRadius))
        {
            return;
        }

        var kind = interaction.SuggestedKind is DutyObjectiveKind.Waypoint or DutyObjectiveKind.BossBoundary
            ? DutyObjectiveKind.Interact
            : interaction.SuggestedKind;
        AddNode(
            kind,
            interaction.Position,
            _options.InteractionRadius,
            interaction.ObjectId,
            notes: $"Captured target '{interaction.Name}'. Classification requires developer verification.");
    }

    private void CaptureCasts(IEnumerable<DutyCastSnapshot> casts)
    {
        foreach (var cast in casts)
        {
            if (cast.BossNpcId == 0 || cast.ActionId == 0 || !_capturedCasts.Add((cast.BossNpcId, cast.ActionId)))
            {
                continue;
            }

            if (!_bossRules.TryGetValue(cast.BossNpcId, out var rules))
            {
                rules = [];
                _bossRules[cast.BossNpcId] = rules;
            }

            _bossNames[cast.BossNpcId] = string.IsNullOrWhiteSpace(cast.BossName)
                ? $"NPC {cast.BossNpcId}"
                : cast.BossName;

            rules.Add(new BossMechanicRule(
                Id: $"observed-{cast.ActionId}",
                Kind: BossMechanicKind.CustomProvider,
                ActionId: cast.ActionId,
                ProviderKey: "recorded-observation",
                Notes: BuildCastNotes(cast)));
        }
    }

    private void AddNode(
        DutyObjectiveKind kind,
        DutyPoint position,
        float radius,
        uint? objectId = null,
        string? notes = null)
    {
        _nodes.Add(new DutyObjectiveNode(
            Id: $"node-{_nextNodeId++:D4}",
            Kind: kind,
            Position: position,
            Radius: radius,
            ObjectId: objectId,
            Required: kind != DutyObjectiveKind.Chest,
            Notes: notes));
        _lastNodePosition = position;
    }

    private static string BuildCastNotes(DutyCastSnapshot cast)
    {
        var target = cast.TargetDestination is { } destination
            ? $" target=({destination.X:F2},{destination.Y:F2},{destination.Z:F2});"
            : string.Empty;
        return $"Recorded observation only; not an avoidance rule. RemainingCast={cast.RemainingCastSeconds:F2}s; " +
               $"caster=({cast.CasterPosition.X:F2},{cast.CasterPosition.Y:F2},{cast.CasterPosition.Z:F2}); " +
               $"playerOffset=({cast.PlayerRelativeOffset.X:F2},{cast.PlayerRelativeOffset.Y:F2},{cast.PlayerRelativeOffset.Z:F2});{target}";
    }

    private static float Distance(DutyPoint left, DutyPoint right)
    {
        var x = right.X - left.X;
        var y = right.Y - left.Y;
        var z = right.Z - left.Z;
        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }

    private static float HeadingDeltaDegrees(float leftRadians, float rightRadians)
    {
        var delta = MathF.Abs(rightRadians - leftRadians) % (2 * MathF.PI);
        if (delta > MathF.PI)
        {
            delta = (2 * MathF.PI) - delta;
        }

        return delta * (180f / MathF.PI);
    }
}

public sealed class DutyRouteRecordingActivity : IEZActivity
{
    private readonly IDutyRouteRecorderSource _source;
    private readonly IDutyRouteRecordingSink _sink;
    private readonly DutyRouteRecorder _recorder;
    private bool _stopRequested;
    private bool _captureInteractionRequested;
    private bool _complete;

    public DutyRouteRecordingActivity(
        IDutyRouteRecorderSource source,
        IDutyRouteRecordingSink sink,
        DutyRouteRecorder recorder)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Duty Route Recorder";
    public ActivityCategory Category => ActivityCategory.Utility;
    public bool IsComplete => _complete;

    public void RequestInteractionCapture() => _captureInteractionRequested = true;
    public void RequestStop() => _stopRequested = true;

    public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_complete)
        {
            return ExecutionResult.Complete("Duty route recording already saved.");
        }

        if (_stopRequested)
        {
            var profile = _recorder.BuildProfile();
            await _sink.SaveAsync(profile, cancellationToken);
            _complete = true;
            return ExecutionResult.Complete(
                $"Saved clean-room duty route '{profile.Name}' with {_recorder.NodeCount} nodes and {_recorder.CapturedCastCount} mechanic observations.");
        }

        var captureInteraction = _captureInteractionRequested;
        _captureInteractionRequested = false;
        _recorder.Sample(_source.CaptureFrame(captureInteraction));
        return ExecutionResult.Yield(
            $"Recording duty route: {_recorder.NodeCount} nodes, {_recorder.CapturedCastCount} mechanic observations.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stopRequested = true;
        return Task.CompletedTask;
    }
}

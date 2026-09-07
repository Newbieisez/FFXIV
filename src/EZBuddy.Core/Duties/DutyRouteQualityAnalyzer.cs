namespace EZBuddy.Core.Duties;

public enum DutyRouteQualitySeverity
{
    Information,
    Warning,
    Error
}

public sealed record DutyRouteQualityIssue(
    string Code,
    DutyRouteQualitySeverity Severity,
    string Message,
    string? ObjectiveId = null,
    string? MechanicId = null);

public sealed record DutyRouteQualityOptions(
    int MinimumRecommendedNodes = 8,
    float NearDuplicateDistanceMeters = 0.35f,
    float WarningSegmentDistanceMeters = 45f,
    float ErrorSegmentDistanceMeters = 100f,
    bool RequireBossBoundaryWhenBossProfilesExist = true)
{
    public void Validate()
    {
        if (MinimumRecommendedNodes < 1 || MinimumRecommendedNodes > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumRecommendedNodes));
        }

        if (NearDuplicateDistanceMeters <= 0 || NearDuplicateDistanceMeters > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(NearDuplicateDistanceMeters));
        }

        if (WarningSegmentDistanceMeters <= NearDuplicateDistanceMeters || WarningSegmentDistanceMeters > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(WarningSegmentDistanceMeters));
        }

        if (ErrorSegmentDistanceMeters <= WarningSegmentDistanceMeters || ErrorSegmentDistanceMeters > 2_000)
        {
            throw new ArgumentOutOfRangeException(nameof(ErrorSegmentDistanceMeters));
        }
    }
}

public sealed record DutyRouteQualityReport(
    string ProfileName,
    uint QueueDutyId,
    uint TerritoryId,
    IReadOnlyList<DutyRouteQualityIssue> Issues,
    int ObjectiveCount,
    int BossBoundaryCount,
    int BossProfileCount,
    int RecordedObservationCount)
{
    public int ErrorCount => Issues.Count(issue => issue.Severity == DutyRouteQualitySeverity.Error);
    public int WarningCount => Issues.Count(issue => issue.Severity == DutyRouteQualitySeverity.Warning);
    public bool ReadyForLiveVerification => ErrorCount == 0;
}

/// <summary>
/// Performs static quality checks against EZBuddy-owned DutyNavigationProfile data before a
/// recorded route is allowed into live verification. It does not use third-party route geometry.
/// </summary>
public sealed class DutyRouteQualityAnalyzer
{
    private readonly DutyRouteQualityOptions _options;

    public DutyRouteQualityAnalyzer(DutyRouteQualityOptions? options = null)
    {
        _options = options ?? new DutyRouteQualityOptions();
        _options.Validate();
    }

    public DutyRouteQualityReport Analyze(DutyNavigationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var issues = new List<DutyRouteQualityIssue>();

        try
        {
            profile.Validate();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            issues.Add(new DutyRouteQualityIssue(
                "PROFILE_VALIDATION_FAILED",
                DutyRouteQualitySeverity.Error,
                exception.Message));
        }

        if (profile.QueueDutyId == 0)
        {
            AddUnique(issues, new DutyRouteQualityIssue(
                "QUEUE_ID_MISSING",
                DutyRouteQualitySeverity.Error,
                "Queue/registration ID is missing."));
        }

        if (profile.TerritoryId == 0)
        {
            issues.Add(new DutyRouteQualityIssue(
                "TERRITORY_ID_MISSING",
                DutyRouteQualitySeverity.Error,
                "Territory/map ID is required before a route may be live-verified."));
        }

        if (profile.Objectives.Count == 0)
        {
            issues.Add(new DutyRouteQualityIssue(
                "NO_OBJECTIVES",
                DutyRouteQualitySeverity.Error,
                "The route contains no objective nodes."));
        }
        else if (profile.Objectives.Count < _options.MinimumRecommendedNodes)
        {
            issues.Add(new DutyRouteQualityIssue(
                "SPARSE_ROUTE",
                DutyRouteQualitySeverity.Warning,
                $"The route contains only {profile.Objectives.Count} nodes; {_options.MinimumRecommendedNodes} or more are recommended before live verification."));
        }

        AnalyzeSegments(profile.Objectives, issues);
        AnalyzeObjectives(profile.Objectives, issues);
        AnalyzeBossCoverage(profile, issues);

        var boundaries = profile.Objectives.Count(node => node.Kind == DutyObjectiveKind.BossBoundary);
        var observations = profile.Bosses
            .SelectMany(boss => boss.Rules)
            .Count(rule => string.Equals(rule.ProviderKey, "recorded-observation", StringComparison.OrdinalIgnoreCase));

        return new DutyRouteQualityReport(
            profile.Name,
            profile.QueueDutyId,
            profile.TerritoryId,
            issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ToArray(),
            profile.Objectives.Count,
            boundaries,
            profile.Bosses.Count,
            observations);
    }

    private void AnalyzeSegments(
        IReadOnlyList<DutyObjectiveNode> objectives,
        ICollection<DutyRouteQualityIssue> issues)
    {
        for (var index = 1; index < objectives.Count; index++)
        {
            var previous = objectives[index - 1];
            var current = objectives[index];
            var distance = Distance(previous.Position, current.Position);

            if (distance <= _options.NearDuplicateDistanceMeters &&
                previous.Kind == current.Kind &&
                previous.ObjectId == current.ObjectId)
            {
                issues.Add(new DutyRouteQualityIssue(
                    "NEAR_DUPLICATE_NODE",
                    DutyRouteQualitySeverity.Warning,
                    $"'{current.Id}' is only {distance:0.00}m from the preceding equivalent node.",
                    current.Id));
            }

            if (distance >= _options.ErrorSegmentDistanceMeters)
            {
                issues.Add(new DutyRouteQualityIssue(
                    "ROUTE_GAP_ERROR",
                    DutyRouteQualitySeverity.Error,
                    $"Route jump from '{previous.Id}' to '{current.Id}' is {distance:0.0}m, suggesting missing recorder samples or a zone transition.",
                    current.Id));
            }
            else if (distance >= _options.WarningSegmentDistanceMeters)
            {
                issues.Add(new DutyRouteQualityIssue(
                    "ROUTE_GAP_WARNING",
                    DutyRouteQualitySeverity.Warning,
                    $"Route jump from '{previous.Id}' to '{current.Id}' is {distance:0.0}m; verify navigation coverage.",
                    current.Id));
            }
        }
    }

    private static void AnalyzeObjectives(
        IEnumerable<DutyObjectiveNode> objectives,
        ICollection<DutyRouteQualityIssue> issues)
    {
        foreach (var node in objectives)
        {
            if (node.Kind is DutyObjectiveKind.Interact or DutyObjectiveKind.Door or DutyObjectiveKind.Switch or DutyObjectiveKind.Lift &&
                node.ObjectId is null or 0)
            {
                issues.Add(new DutyRouteQualityIssue(
                    "INTERACTABLE_ID_MISSING",
                    DutyRouteQualitySeverity.Error,
                    $"Interactive objective '{node.Id}' does not have an object ID.",
                    node.Id));
            }

            if (node.Kind == DutyObjectiveKind.Chest && node.Required)
            {
                issues.Add(new DutyRouteQualityIssue(
                    "REQUIRED_CHEST",
                    DutyRouteQualitySeverity.Warning,
                    $"Chest '{node.Id}' is marked required. Recorded chests should normally remain optional so inventory safety can skip them.",
                    node.Id));
            }

            if (node.Notes?.Contains("requires developer verification", StringComparison.OrdinalIgnoreCase) is true)
            {
                issues.Add(new DutyRouteQualityIssue(
                    "UNVERIFIED_CLASSIFICATION",
                    DutyRouteQualitySeverity.Warning,
                    $"Objective '{node.Id}' still carries an unverified recorder classification.",
                    node.Id));
            }
        }
    }

    private void AnalyzeBossCoverage(
        DutyNavigationProfile profile,
        ICollection<DutyRouteQualityIssue> issues)
    {
        var boundaryCount = profile.Objectives.Count(node => node.Kind == DutyObjectiveKind.BossBoundary);
        if (_options.RequireBossBoundaryWhenBossProfilesExist && profile.Bosses.Count > 0 && boundaryCount == 0)
        {
            issues.Add(new DutyRouteQualityIssue(
                "BOSS_BOUNDARY_MISSING",
                DutyRouteQualitySeverity.Warning,
                "Boss mechanic profiles exist, but the route contains no BossBoundary nodes."));
        }

        if (boundaryCount > 0 && profile.Bosses.Count == 0)
        {
            issues.Add(new DutyRouteQualityIssue(
                "BOSS_MECHANICS_MISSING",
                DutyRouteQualitySeverity.Warning,
                $"The recorder captured {boundaryCount} combat boundary node(s), but no boss mechanic observations exist."));
        }

        foreach (var boss in profile.Bosses)
        {
            if (boss.Rules.Count == 0)
            {
                issues.Add(new DutyRouteQualityIssue(
                    "EMPTY_BOSS_PROFILE",
                    DutyRouteQualitySeverity.Warning,
                    $"Boss '{boss.Name}' has no mechanic rules."));
                continue;
            }

            foreach (var rule in boss.Rules)
            {
                if (string.Equals(rule.ProviderKey, "recorded-observation", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new DutyRouteQualityIssue(
                        "RECORDED_MECHANIC_UNVALIDATED",
                        DutyRouteQualitySeverity.Warning,
                        $"Boss '{boss.Name}' action {rule.ActionId} is still a recorder observation and has not been classified into executable mechanic behavior.",
                        MechanicId: rule.Id));
                }

                if (rule.Kind is not BossMechanicKind.CustomProvider &&
                    rule.ActionId is null or 0 &&
                    rule.ObjectId is null or 0)
                {
                    issues.Add(new DutyRouteQualityIssue(
                        "MECHANIC_TRIGGER_MISSING",
                        DutyRouteQualitySeverity.Warning,
                        $"Mechanic '{rule.Id}' has no action or object trigger.",
                        MechanicId: rule.Id));
                }
            }
        }
    }

    private static void AddUnique(ICollection<DutyRouteQualityIssue> issues, DutyRouteQualityIssue issue)
    {
        if (!issues.Any(existing => existing.Code == issue.Code))
        {
            issues.Add(issue);
        }
    }

    private static float Distance(DutyPoint left, DutyPoint right)
    {
        var x = right.X - left.X;
        var y = right.Y - left.Y;
        var z = right.Z - left.Z;
        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }
}

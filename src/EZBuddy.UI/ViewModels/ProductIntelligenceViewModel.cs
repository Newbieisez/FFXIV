using System.Collections.ObjectModel;
using System.Windows.Input;
using EZBuddy.Core.Goals;
using EZBuddy.Core.Product;
using EZBuddy.UI.Infrastructure;

namespace EZBuddy.UI.ViewModels;

public sealed record ProductIntelligenceUpdate(
    PreflightReport Preflight,
    DryRunPlan DryRun,
    GoalPlan Goal,
    IReadOnlyList<string> Recommendations);

public interface IProductIntelligenceProvider
{
    Task<ProductIntelligenceUpdate> EvaluateAsync(
        GoalType goalType,
        string subject,
        CancellationToken cancellationToken = default);
}

public static class ProductIntelligenceRuntime
{
    public static IProductIntelligenceProvider? Provider { get; set; }
}

public sealed class ProductIntelligenceViewModel : ObservableObject
{
    private readonly IProductIntelligenceProvider? _provider;
    private GoalType _selectedGoalType = GoalType.WeeklyChores;
    private string _goalSubject = "My character";
    private string _overallState = "NOT EVALUATED";
    private string _statusMessage = "Run analysis to simulate the current EZBuddy plan.";
    private bool _refreshing;

    public ProductIntelligenceViewModel(IProductIntelligenceProvider? provider = null)
    {
        _provider = provider ?? ProductIntelligenceRuntime.Provider;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PreflightChecks = [];
        DryRunActions = [];
        GoalSteps = [];
        Recommendations = [];
    }

    public IReadOnlyList<GoalType> GoalTypes { get; } = Enum.GetValues<GoalType>();
    public ObservableCollection<PreflightCheck> PreflightChecks { get; }
    public ObservableCollection<DryRunAction> DryRunActions { get; }
    public ObservableCollection<GoalStep> GoalSteps { get; }
    public ObservableCollection<string> Recommendations { get; }
    public ICommand RefreshCommand { get; }

    public GoalType SelectedGoalType
    {
        get => _selectedGoalType;
        set => SetProperty(ref _selectedGoalType, value);
    }

    public string GoalSubject
    {
        get => _goalSubject;
        set => SetProperty(ref _goalSubject, value);
    }

    public string OverallState
    {
        get => _overallState;
        private set => SetProperty(ref _overallState, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        if (_provider is null)
        {
            OverallState = "UNAVAILABLE";
            StatusMessage = "Product Intelligence provider is not available in this host.";
            return;
        }

        _refreshing = true;
        try
        {
            var subject = string.IsNullOrWhiteSpace(GoalSubject) ? "My character" : GoalSubject.Trim();
            var update = await _provider.EvaluateAsync(SelectedGoalType, subject).ConfigureAwait(true);

            Replace(PreflightChecks, update.Preflight.Checks);
            Replace(DryRunActions, update.DryRun.Actions);
            Replace(GoalSteps, update.Goal.Steps);
            Replace(Recommendations, update.Recommendations);

            OverallState = update.Preflight.OverallState;
            StatusMessage = $"{update.DryRun.RunnableCount} action(s) would run; {update.DryRun.BlockedCount} blocked or awaiting approval. Goal can start: {update.Goal.CanStart}.";
        }
        catch (Exception exception)
        {
            OverallState = "ERROR";
            StatusMessage = $"Product Intelligence evaluation failed: {exception.Message}";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}

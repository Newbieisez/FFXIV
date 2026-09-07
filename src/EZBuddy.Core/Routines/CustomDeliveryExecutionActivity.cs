using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Routines;

public sealed record CustomDeliveryExecutionOptions(
    IReadOnlyCollection<string> ClientKeys,
    string CraftingClassKey = "Carpenter")
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(ClientKeys);
        if (ClientKeys.Count == 0 || ClientKeys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Custom Deliveries execution requires at least one explicit non-empty client key.",
                nameof(ClientKeys));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(CraftingClassKey);
    }
}

/// <summary>
/// Executes one explicitly selected Custom Deliveries weekly pass. The activity intentionally
/// never retries a failed/ambiguous adapter result because the external workflow may have
/// completed some hand-ins before returning false.
/// </summary>
public sealed class CustomDeliveryExecutionActivity : IEZActivity
{
    private readonly ICustomDeliveryAdapter _adapter;
    private readonly CustomDeliveryExecutionOptions _options;
    private bool _complete;
    private bool _attempted;

    public CustomDeliveryExecutionActivity(
        ICustomDeliveryAdapter adapter,
        CustomDeliveryExecutionOptions options)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Custom Deliveries Weekly Pass";
    public ActivityCategory Category => ActivityCategory.DailyWeekly;
    public bool IsComplete => _complete;

    public async Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_complete || _attempted)
        {
            return false;
        }

        var status = await _adapter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.Health == AdapterHealth.Ready;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_complete)
        {
            return ExecutionResult.Complete("Custom Deliveries weekly pass already completed.");
        }

        if (_attempted)
        {
            return ExecutionResult.Block(
                "Custom Deliveries execution already returned an ambiguous failure in this activity. Review live allowance state before trying again.");
        }

        var status = await _adapter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Health != AdapterHealth.Ready)
        {
            return ExecutionResult.Block($"Custom Deliveries bridge is not ready: {status.Message}");
        }

        _attempted = true;
        var succeeded = await _adapter.RunSelectedAsync(
            _options.ClientKeys,
            _options.CraftingClassKey,
            cancellationToken).ConfigureAwait(false);

        if (!succeeded)
        {
            return ExecutionResult.Block(
                "Custom Deliveries workflow did not report a clean success. EZBuddy will not automatically repeat the weekly pass because partial hand-ins may already have occurred.");
        }

        _complete = true;
        return ExecutionResult.Complete(
            $"Custom Deliveries completed for {_options.ClientKeys.Count} explicitly selected client(s) using {_options.CraftingClassKey} for crafting work.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
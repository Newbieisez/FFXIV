using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Progression;

public sealed class OrderBotProfileActivity : IEZActivity
{
    private readonly IOrderBotAdapter _adapter;
    private readonly string _profilePath;
    private bool _started;
    private bool _complete;

    public OrderBotProfileActivity(
        IOrderBotAdapter adapter,
        string profilePath,
        string? displayName = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        _profilePath = profilePath;
        Name = string.IsNullOrWhiteSpace(displayName)
            ? $"OrderBot: {Path.GetFileNameWithoutExtension(profilePath)}"
            : displayName.Trim();
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }
    public string Name { get; }
    public ActivityCategory Category => ActivityCategory.Questing;
    public bool IsComplete => _complete;
    public string ProfilePath => _profilePath;

    public async Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_complete || !File.Exists(_profilePath))
        {
            return false;
        }

        var status = await _adapter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.Health is AdapterHealth.Ready or AdapterHealth.Busy;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_profilePath))
        {
            _complete = true;
            return ExecutionResult.Fail($"Generated profile no longer exists: {_profilePath}");
        }

        if (!_started)
        {
            var loaded = await _adapter.LoadProfileAsync(_profilePath, cancellationToken).ConfigureAwait(false);
            if (!loaded)
            {
                return ExecutionResult.Retry("OrderBot did not accept the generated profile.", TimeSpan.FromSeconds(2));
            }

            _started = true;
            return ExecutionResult.Continue($"Loaded generated profile '{Path.GetFileName(_profilePath)}'.");
        }

        if (await _adapter.IsProfileRunningAsync(cancellationToken).ConfigureAwait(false))
        {
            return ExecutionResult.Continue("Generated progression profile is running.");
        }

        _complete = true;
        return ExecutionResult.Complete("Generated progression profile completed.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _started = false;
        return Task.CompletedTask;
    }
}

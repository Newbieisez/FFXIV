using EZBuddy.Core.Adapters;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class MagitekAdapter : IMagitekAdapter
{
    public const string AdapterKey = "magitek";
    private const string ExpectedRoutineName = "Magitek";

    public string Key => AdapterKey;
    public string DisplayName => "Magitek";
    public bool IsRequiredForCombat => true;

    public Task<bool> IsCurrentRoutineAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentName = RoutineManager.Current?.Name ?? string.Empty;
        return Task.FromResult(string.Equals(currentName, ExpectedRoutineName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var currentName = RoutineManager.Current?.Name ?? string.Empty;
        var isCurrent = await IsCurrentRoutineAsync(cancellationToken).ConfigureAwait(false);

        if (isCurrent)
        {
            return new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Magitek is the active combat routine.", DateTimeOffset.UtcNow);
        }

        if (string.IsNullOrWhiteSpace(currentName))
        {
            return new AdapterStatus(Key, DisplayName, AdapterHealth.Missing, "No combat routine is currently selected.", DateTimeOffset.UtcNow);
        }

        return new AdapterStatus(
            Key,
            DisplayName,
            AdapterHealth.Degraded,
            $"Combat routine mismatch: '{currentName}' is active. Combat-dependent EZBuddy activities require Magitek.",
            DateTimeOffset.UtcNow);
    }

    public static bool IsActive(out string diagnostic)
    {
        try
        {
            var currentName = RoutineManager.Current?.Name ?? string.Empty;
            if (!string.Equals(currentName, ExpectedRoutineName, StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = string.IsNullOrWhiteSpace(currentName)
                    ? "No combat routine is selected. Magitek must be active."
                    : $"Combat routine mismatch: '{currentName}' is selected. Magitek must be active.";
                return false;
            }

            diagnostic = "Magitek verified as the active combat routine.";
            return true;
        }
        catch (Exception exception)
        {
            diagnostic = $"Magitek verification error: {exception.Message}";
            return false;
        }
    }
}

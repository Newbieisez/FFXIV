using EZBuddy.Core.Diagnostics;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Diagnostics;

public sealed class RebornBuddyRuntimeComponentSource : IRuntimeComponentSource
{
    public Task<IReadOnlyList<RuntimeComponent>> GetComponentsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var components = new List<RuntimeComponent>();

        foreach (var container in PluginManager.Plugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plugin = container.Plugin;
            components.Add(new RuntimeComponent(
                plugin.Name,
                "Plugin",
                container.Enabled,
                false,
                plugin.Version));
        }

        foreach (var bot in BotManager.Bots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            components.Add(new RuntimeComponent(
                bot.Name,
                "BotBase",
                true,
                ReferenceEquals(BotManager.Current, bot)));
        }

        var routine = RoutineManager.Current;
        if (routine is not null)
        {
            components.Add(new RuntimeComponent(
                routine.Name,
                "CombatRoutine",
                true,
                true));
        }

        return Task.FromResult<IReadOnlyList<RuntimeComponent>>(components);
    }
}

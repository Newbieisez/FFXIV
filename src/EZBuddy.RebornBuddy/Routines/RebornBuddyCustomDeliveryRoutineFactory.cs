using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;
using EZBuddy.RebornBuddy.Adapters;

namespace EZBuddy.RebornBuddy.Routines;

public sealed class RebornBuddyCustomDeliveryRoutineFactory : IRoutineActivityFactory
{
    private const string ClientsEnvironmentKey = "EZBUDDY_CUSTOM_DELIVERY_CLIENTS";
    private const string CraftingClassEnvironmentKey = "EZBUDDY_CUSTOM_DELIVERY_CRAFTING_CLASS";

    public string RoutineKey => "custom-deliveries";

    public Task<IEZActivity?> CreateAsync(
        RoutineDefinition routine,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(routine);

        var clients = ReadClientKeys();
        if (clients.Count == 0)
        {
            return Task.FromResult<IEZActivity?>(null);
        }

        var craftingClass = Environment.GetEnvironmentVariable(CraftingClassEnvironmentKey)?.Trim();
        if (string.IsNullOrWhiteSpace(craftingClass))
        {
            craftingClass = "Carpenter";
        }

        IEZActivity activity = new CustomDeliveryExecutionActivity(
            new LlamaCustomDeliveryAdapter(),
            new CustomDeliveryExecutionOptions(clients, craftingClass));
        return Task.FromResult<IEZActivity?>(activity);
    }

    public static IReadOnlyCollection<string> ReadClientKeys()
    {
        var value = Environment.GetEnvironmentVariable(ClientsEnvironmentKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
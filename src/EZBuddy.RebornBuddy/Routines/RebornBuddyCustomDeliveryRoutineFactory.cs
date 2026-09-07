using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;
using EZBuddy.RebornBuddy.Adapters;

namespace EZBuddy.RebornBuddy.Routines;

public sealed class RebornBuddyCustomDeliveryRoutineFactory : IRoutineActivityFactory
{
    private readonly IReadOnlyCollection<string> _clientKeys;
    private readonly string _craftingClassKey;

    public RebornBuddyCustomDeliveryRoutineFactory(
        IReadOnlyCollection<string> clientKeys,
        string craftingClassKey)
    {
        _clientKeys = clientKeys ?? throw new ArgumentNullException(nameof(clientKeys));
        _craftingClassKey = string.IsNullOrWhiteSpace(craftingClassKey)
            ? throw new ArgumentException("Custom Deliveries crafting class is required.", nameof(craftingClassKey))
            : craftingClassKey.Trim();
    }

    public string RoutineKey => "custom-deliveries";

    public Task<IEZActivity?> CreateAsync(
        RoutineDefinition routine,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(routine);

        var clients = _clientKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (clients.Length == 0)
        {
            return Task.FromResult<IEZActivity?>(null);
        }

        IEZActivity activity = new CustomDeliveryExecutionActivity(
            new LlamaCustomDeliveryAdapter(),
            new CustomDeliveryExecutionOptions(clients, _craftingClassKey));
        return Task.FromResult<IEZActivity?>(activity);
    }
}
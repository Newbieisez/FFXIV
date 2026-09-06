using System.Reflection;
using EZBuddy.Core.Retainers;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Retainers;

public sealed class RebornBuddyRetainerBellAccess : IRetainerBellAccess
{
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(6);
    private static readonly string[] RetainerWindowTypeNames =
    [
        "ff14bot.RemoteWindows.RetainerList",
        "ff14bot.RemoteWindows.AddonRetainerList",
        "LlamaLibrary.RemoteWindows.RetainerList"
    ];

    public async Task<RetainerBellAccessResult> EnsureBellOpenAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsRetainerWindowOpen())
        {
            return new RetainerBellAccessResult(true, true, "Retainer list is already open.");
        }

        var localBell = GameObjectManager.GameObjects
            .Where(gameObject =>
                gameObject is not null &&
                gameObject.IsValid &&
                string.Equals(gameObject.Name, "Summoning Bell", StringComparison.OrdinalIgnoreCase))
            .OrderBy(gameObject => DistanceSquared(gameObject.Location.X, gameObject.Location.Y, gameObject.Location.Z))
            .FirstOrDefault();

        if (localBell is not null && localBell.IsWithinInteractRange)
        {
            localBell.Interact();
            if (await WaitUntilAsync(IsRetainerWindowOpen, OpenTimeout, cancellationToken).ConfigureAwait(false))
            {
                return new RetainerBellAccessResult(true, true, "Opened the nearest summoning bell in the current zone.");
            }
        }

        var llamaResult = await TryUseLlamaBellRoutingAsync(cancellationToken).ConfigureAwait(false);
        if (llamaResult)
        {
            return new RetainerBellAccessResult(true, false, "No usable local bell was open; LlamaLibrary routed to a summoning bell.");
        }

        var localMessage = localBell is null
            ? "No summoning bell is visible in the current zone."
            : "A local summoning bell is visible but could not be opened.";

        return new RetainerBellAccessResult(
            false,
            localBell is not null,
            $"{localMessage} Optional routed bell access is unavailable.");
    }

    private static async Task<bool> TryUseLlamaBellRoutingAsync(CancellationToken cancellationToken)
    {
        var helperType = ResolveType("LlamaLibrary.Retainers.HelperFunctions");
        var method = helperType?.GetMethod(
            "UseSummoningBell",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (method?.Invoke(null, null) is not Task task)
        {
            return false;
        }

        await task.WaitAsync(TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
        return resultProperty?.GetValue(task) is bool result && result;
    }

    private static bool IsRetainerWindowOpen()
    {
        foreach (var typeName in RetainerWindowTypeNames)
        {
            var type = ResolveType(typeName);
            var instance = type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (instance is null)
            {
                continue;
            }

            if (instance.GetType().GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) is true)
            {
                return true;
            }
        }

        return false;
    }

    private static Type? ResolveType(string fullName)
        => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly =>
            {
                try
                {
                    return assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                }
                catch
                {
                    return null;
                }
            })
            .FirstOrDefault(type => type is not null);

    private static async Task<bool> WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate())
            {
                return true;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return predicate();
    }

    private static double DistanceSquared(float x, float y, float z)
    {
        var player = ff14bot.Core.Player;
        if (player is null)
        {
            return double.MaxValue;
        }

        var location = player.Location;
        var dx = x - location.X;
        var dy = y - location.Y;
        var dz = z - location.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}

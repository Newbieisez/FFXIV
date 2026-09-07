using System.Reflection;
using EZBuddy.Core.Routines;

namespace EZBuddy.RebornBuddy.Routines;

/// <summary>
/// Reads Grand Company seal state only through the optional public LlamaLibrary extension surface.
/// No raw offsets are used here; absence or signature drift is reported as unavailable state.
/// </summary>
public sealed class LlamaGrandCompanySealStateProvider : IGrandCompanySealStateProvider
{
    private const string ExtensionTypeName = "LlamaLibrary.Extensions.LocalPlayerExtensions";

    public Task<GrandCompanySealState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ff14bot.Core.Player is null || ff14bot.Behavior.CommonBehaviors.IsLoading)
        {
            return Task.FromResult<GrandCompanySealState?>(null);
        }

        try
        {
            var type = ResolveType(ExtensionTypeName);
            if (type is null)
            {
                return Task.FromResult<GrandCompanySealState?>(null);
            }

            var player = ff14bot.Core.Me;
            var currentMethod = ResolveSinglePlayerMethod(type, "GCSeals", player);
            var maximumMethod = ResolveSinglePlayerMethod(type, "MaxGCSeals", player);
            if (currentMethod is null || maximumMethod is null)
            {
                return Task.FromResult<GrandCompanySealState?>(null);
            }

            var current = Convert.ToInt32(currentMethod.Invoke(null, [player]));
            var maximum = Convert.ToInt32(maximumMethod.Invoke(null, [player]));
            var state = new GrandCompanySealState(current, maximum);
            state.Validate();
            return Task.FromResult<GrandCompanySealState?>(state);
        }
        catch (Exception exception) when (exception is
            TargetInvocationException or
            ArgumentException or
            MethodAccessException or
            InvalidCastException or
            FormatException or
            OverflowException or
            InvalidDataException)
        {
            return Task.FromResult<GrandCompanySealState?>(null);
        }
    }

    private static MethodInfo? ResolveSinglePlayerMethod(Type type, string methodName, object player)
        => type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method => method.GetParameters().Length == 1)
            .FirstOrDefault(method => method.GetParameters()[0].ParameterType.IsInstanceOfType(player));

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
}

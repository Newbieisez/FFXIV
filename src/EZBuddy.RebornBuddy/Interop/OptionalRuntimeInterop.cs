using System.Collections.Concurrent;
using System.Reflection;

namespace EZBuddy.RebornBuddy.Interop;

/// <summary>
/// Small allowlisted reflection helper for optional runtime integrations such as LlamaLibrary.
/// Positive type/method resolutions are cached; misses are deliberately not cached because
/// RebornBuddy can load optional assemblies after EZBuddy starts.
/// </summary>
internal static class OptionalRuntimeInterop
{
    private static readonly ConcurrentDictionary<string, Type> TypeCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<MethodCacheKey, MethodInfo> ExactStaticMethodCache = new();

    public static Type? ResolveType(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        if (TypeCache.TryGetValue(fullName, out var cached))
        {
            return cached;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type;
            try
            {
                type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            }
            catch
            {
                continue;
            }

            if (type is null)
            {
                continue;
            }

            TypeCache.TryAdd(fullName, type);
            return type;
        }

        return null;
    }

    public static MethodInfo? ResolveStaticMethod(string typeName, string methodName, params Type[] parameterTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(parameterTypes);

        var key = new MethodCacheKey(typeName, methodName, BuildSignature(parameterTypes));
        if (ExactStaticMethodCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var type = ResolveType(typeName);
        var method = type?.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        if (method is not null)
        {
            ExactStaticMethodCache.TryAdd(key, method);
        }

        return method;
    }

    public static MethodInfo? ResolveStaticMethod(
        string typeName,
        string methodName,
        Func<MethodInfo, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(predicate);

        var type = ResolveType(typeName);
        if (type is null)
        {
            return null;
        }

        return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .FirstOrDefault(predicate);
    }

    public static bool TryInvokeStatic(
        string typeName,
        string methodName,
        object?[] arguments,
        out object? result)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        result = null;

        var method = ResolveStaticMethod(
            typeName,
            methodName,
            candidate => ParametersMatch(candidate.GetParameters(), arguments));
        if (method is null)
        {
            return false;
        }

        try
        {
            result = method.Invoke(null, arguments);
            return true;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (MethodAccessException)
        {
            return false;
        }
    }

    public static T ReadTaskResult<T>(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var value = ReadTaskResultObject(task);
        if (value is T typed)
        {
            return typed;
        }

        try
        {
            return value is null ? default! : (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default!;
        }
    }

    public static object? ReadTaskResultObject(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            return task.GetType()
                .GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(task);
        }
        catch
        {
            return null;
        }
    }

    private static bool ParametersMatch(ParameterInfo[] parameters, object?[] arguments)
    {
        if (parameters.Length != arguments.Length)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            var argument = arguments[index];
            var parameterType = parameters[index].ParameterType;

            if (argument is null)
            {
                if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) is null)
                {
                    return false;
                }

                continue;
            }

            if (!parameterType.IsInstanceOfType(argument))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildSignature(IEnumerable<Type> parameterTypes)
        => string.Join("|", parameterTypes.Select(type => type.AssemblyQualifiedName ?? type.FullName ?? type.Name));

    private readonly record struct MethodCacheKey(string TypeName, string MethodName, string Signature);
}

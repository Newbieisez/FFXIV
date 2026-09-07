using EZBuddy.Core.Settings;

namespace EZBuddy.Core.Bundles;

public sealed record FirstPlayableLoopStartResult(
    bool Success,
    string Message,
    IReadOnlyList<string> StageNames)
{
    public static FirstPlayableLoopStartResult Rejected(string message)
        => new(false, message, Array.Empty<string>());

    public static FirstPlayableLoopStartResult Started(string message, IReadOnlyList<string> stageNames)
        => new(true, message, stageNames);
}

public interface IFirstPlayableLoopController
{
    Task<FirstPlayableLoopStartResult> QueueAsync(
        FirstPlayableLoopSettings settings,
        CancellationToken cancellationToken = default);
}

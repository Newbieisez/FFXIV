namespace EZBuddy.Core.Duties;

public sealed record DutyRouteRecorderStartRequest(
    uint QueueDutyId,
    uint TerritoryId,
    string Name,
    int MinimumLevel,
    int MaximumLevel)
{
    public void Validate()
    {
        if (QueueDutyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(QueueDutyId));
        }

        if (TerritoryId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TerritoryId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (MinimumLevel <= 0 || MaximumLevel < MinimumLevel || MaximumLevel > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumLevel));
        }
    }
}

public sealed record DutyRouteRecorderControlResult(bool Success, string Message)
{
    public static DutyRouteRecorderControlResult Ok(string message) => new(true, message);
    public static DutyRouteRecorderControlResult Reject(string message) => new(false, message);
}

public interface IDutyRouteRecorderController
{
    DutyRouteRecorderControlResult Start(DutyRouteRecorderStartRequest request);
    DutyRouteRecorderControlResult CaptureCurrentTarget();
    DutyRouteRecorderControlResult StopAndSave();
}

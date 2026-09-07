using EZBuddy.Core.Duties;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Runtime;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Duties;

public sealed class RebornBuddyDutyRouteRecorderController : IDutyRouteRecorderController
{
    private readonly object _sync = new();
    private DutyRouteRecordingActivity? _activeRecorder;

    public DutyRouteRecorderControlResult Start(DutyRouteRecorderStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        lock (_sync)
        {
            if (_activeRecorder is { IsComplete: false })
            {
                return DutyRouteRecorderControlResult.Reject("A duty route recording is already active.");
            }

            if (WorldManager.ZoneId != request.TerritoryId)
            {
                return DutyRouteRecorderControlResult.Reject(
                    $"Recorder territory mismatch. Current territory is {WorldManager.ZoneId}; configured territory is {request.TerritoryId}.");
            }

            var queue = EZBuddyRuntime.Queue;
            var active = queue.CurrentActivity is not null || queue.GetSnapshots().Any(snapshot =>
                snapshot.State is ActivityState.Pending or ActivityState.Waiting or ActivityState.Running or ActivityState.GentleStopping);
            if (active)
            {
                return DutyRouteRecorderControlResult.Reject(
                    "Route recording requires an empty EZBuddy queue so developer sampling cannot overlap normal automation.");
            }

            var recorder = new DutyRouteRecorder(
                request.QueueDutyId,
                request.TerritoryId,
                request.Name,
                request.MinimumLevel,
                request.MaximumLevel);
            var activity = new DutyRouteRecordingActivity(
                new RebornBuddyDutyRouteRecorderSource(),
                new JsonDutyNavigationProfileStore(),
                recorder);

            _activeRecorder = activity;
            queue.Enqueue(new ActivityQueueItem(
                activity,
                Priority: 50_000,
                MaxRetries: 0,
                ContinueOnFailure: false,
                StopConditionLabel: "Developer stops and saves route"));
            queue.Start();

            return DutyRouteRecorderControlResult.Ok(
                $"Recording '{request.Name}' in territory {request.TerritoryId}. Walk the route manually; use Capture Target for ambiguous interactables, then Stop & Save.");
        }
    }

    public DutyRouteRecorderControlResult CaptureCurrentTarget()
    {
        lock (_sync)
        {
            if (_activeRecorder is null || _activeRecorder.IsComplete)
            {
                return DutyRouteRecorderControlResult.Reject("No duty route recording is active.");
            }

            _activeRecorder.RequestInteractionCapture();
            return DutyRouteRecorderControlResult.Ok("Current non-combat target will be captured on the next EZBuddy BotBase tick.");
        }
    }

    public DutyRouteRecorderControlResult StopAndSave()
    {
        lock (_sync)
        {
            if (_activeRecorder is null || _activeRecorder.IsComplete)
            {
                return DutyRouteRecorderControlResult.Reject("No duty route recording is active.");
            }

            _activeRecorder.RequestStop();
            return DutyRouteRecorderControlResult.Ok(
                "Stop requested. The route will be validated and saved on the next EZBuddy BotBase tick.");
        }
    }
}

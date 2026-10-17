using CyEngineHub.Core.Tasks;

namespace CyEngineHub.Core.Diagnostics;

/// <summary>Keeps task lifecycle events separate from high-frequency technical progress.</summary>
public sealed class ActivityLogPolicy
{
    private readonly Dictionary<Guid, HubTaskStatus> _statuses = [];
    private readonly object _gate = new();
    public string LevelFor(HubTaskSnapshot task)
    {
        lock (_gate)
        {
            var transition = !_statuses.TryGetValue(task.Id, out var previous) || previous != task.Status;
            _statuses[task.Id] = task.Status;
            if (task.Status == HubTaskStatus.Failed) return "ERROR";
            if (task.Status == HubTaskStatus.Canceled) return "WARN";
            // Network recovery can be extremely repetitive; retain it in the complete log.
            return transition ? "TASK" : "TRACE";
        }
    }

    public static bool IsImportant(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || char.IsWhiteSpace(line[0])) return false;
        if (line.Contains("[TRACE]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[DEBUG]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[OUT]", StringComparison.OrdinalIgnoreCase)) return false;
        // Older logs used TASK for every progress callback, including individual chunks.
        if (line.Contains("[TASK]", StringComparison.OrdinalIgnoreCase))
        {
            var marker = line.IndexOf(" · Running · ", StringComparison.Ordinal);
            if (marker >= 0 && line[(marker + " · Running · ".Length)..].Trim().Length > 0) return false;
        }
        return true;
    }
}

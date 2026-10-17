namespace CyEngineHub.Core.Tasks;

public enum HubTaskStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled,
}

public sealed record HubTaskSnapshot(
    Guid Id,
    string Category,
    string Title,
    HubTaskStatus Status,
    double? Progress,
    string? Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error)
{
    public bool IsDownload { get; init; }
    public IReadOnlyList<HubTaskLink> Links { get; init; } = [];

}

public sealed record HubTaskLink(string Kind, string Id, string Label, string? Source = null);

public sealed record HubTaskDefinition(
    string Category,
    string Title,
    Func<HubTaskContext, CancellationToken, Task> ExecuteAsync)
{
    public bool IsDownload { get; init; }
    public IReadOnlyList<HubTaskLink> Links { get; init; } = [];

    public HubTaskDefinition(string category, string title, IReadOnlyList<HubTaskLink> links,
        Func<HubTaskContext, CancellationToken, Task> executeAsync)
        : this(category, title, executeAsync) => Links = links.ToArray();
}

public sealed class HubTaskContext
{
    private readonly Action<double?, string?> _report;

    internal HubTaskContext(Action<double?, string?> report)
    {
        _report = report;
    }

    public void Report(double? progress = null, string? message = null)
    {
        if (progress is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(progress), "Progress must be between 0 and 1.");
        }

        _report(progress, message);
    }
}

public sealed class HubTaskHandle
{
    private readonly Action _cancel;

    internal HubTaskHandle(Guid id, Task<HubTaskSnapshot> completion, Action cancel)
    {
        Id = id;
        Completion = completion;
        _cancel = cancel;
    }

    public Guid Id { get; }

    public Task<HubTaskSnapshot> Completion { get; }

    public void Cancel() => _cancel();
}

public sealed class HubTaskChangedEventArgs : EventArgs
{
    public HubTaskChangedEventArgs(HubTaskSnapshot task)
    {
        Task = task;
    }

    public HubTaskSnapshot Task { get; }
}

using System.Collections.Concurrent;

namespace CyEngineHub.Core.Tasks;

public sealed class HubTaskScheduler : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<WorkItem> _pending = [];
    private readonly HashSet<WorkItem> _running = [];
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly ConcurrentDictionary<Guid, WorkItem> _items = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _dispatcher;
    private readonly int _maximumConcurrency;
    private readonly IHubTaskDiskResolver _disks;
    private int _downloadLimit = 3;
    private bool _disposed;

    public HubTaskScheduler(int maximumConcurrency = 1, IHubTaskDiskResolver? disks = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        _maximumConcurrency = maximumConcurrency;
        _disks = disks ?? new HubTaskDiskResolver();
        _dispatcher = Task.Run(DispatchAsync);
    }

    public event EventHandler<HubTaskChangedEventArgs>? TaskChanged;

    public IReadOnlyList<HubTaskSnapshot> GetSnapshot() => _items.Values
        .Select(item => item.ReadSnapshot()).OrderByDescending(item => item.CreatedAt).ToArray();

    public HubTaskHandle Enqueue(HubTaskDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        WorkItem item;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            item = new WorkItem(definition, RaiseChanged, _disks, _shutdown.Token);
            _items[item.Id] = item;
            _pending.Add(item);
            // Publish the queued snapshot before dispatch can start this work.
            RaiseChanged(item.ReadSnapshot());
        }
        Signal();
        return new HubTaskHandle(item.Id, item.Completion, item.Cancel);
    }

    public bool Cancel(Guid id)
    {
        if (!_items.TryGetValue(id, out var item)) return false;
        item.Cancel();
        return true;
    }

    public void SetDownloadConcurrency(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16);
        lock (_gate) { _downloadLimit = count; }
        Signal();
    }

    private void Signal()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { }
    }

    private void RaiseChanged(HubTaskSnapshot snapshot)
    {
        TaskChanged?.Invoke(this, new HubTaskChangedEventArgs(snapshot));
        if (snapshot.Status is HubTaskStatus.Canceled or HubTaskStatus.Completed or HubTaskStatus.Failed) Signal();
    }

    private async Task DispatchAsync()
    {
        while (true)
        {
            await _wake.WaitAsync().ConfigureAwait(false);
            List<WorkItem> ready = [];
            lock (_gate)
            {
                _pending.RemoveAll(item => item.ReadSnapshot().Status != HubTaskStatus.Queued);
                if (_disposed && _pending.Count == 0 && _running.Count == 0) return;
                List<WorkItem> earlierWaiting = [];
                foreach (var item in _pending.ToArray())
                {
                    var earlierWriter = earlierWaiting.FirstOrDefault(earlier =>
                        item.Keys.Intersect(earlier.Keys.Concat(earlier.SharedKeys), StringComparer.OrdinalIgnoreCase).Any() ||
                        item.SharedKeys.Intersect(earlier.Keys, StringComparer.OrdinalIgnoreCase).Any() ||
                        item.Paths.Any(path => earlier.Paths.Any(other => Overlap(path, other))));
                    var reason = earlierWriter is null ? WaitingReason(item) : $"En attente : priorité à « {earlierWriter.Title} » sur le même projet/pack.";
                    if (reason is not null) { item.Waiting(reason); earlierWaiting.Add(item); continue; }
                    _pending.Remove(item);
                    _running.Add(item);
                    ready.Add(item);
                }
            }
            // A blocked earlier item does not occupy a worker or block independent work.
            foreach (var item in ready) _ = Task.Run(() => ExecuteAsync(item));
        }
    }

    private string? WaitingReason(WorkItem item)
    {
        foreach (var active in _running)
        {
            if (item.Paths.Any(path => active.Paths.Any(other => Overlap(path, other))))
                return $"En attente : le même projet/dossier est utilisé par « {active.Title} ».";
            if (item.Keys.Intersect(active.Keys.Concat(active.SharedKeys), StringComparer.OrdinalIgnoreCase).Any() ||
                item.SharedKeys.Intersect(active.Keys, StringComparer.OrdinalIgnoreCase).Any())
                return $"En attente : le même pack/ressource est utilisé par « {active.Title} ».";
        }
        foreach (var disk in item.Disks)
        {
            var users = _running.Where(active => active.Disks.Any(other => other.Id.Equals(disk.Id, StringComparison.OrdinalIgnoreCase))).ToArray();
            var limit = users.SelectMany(active => active.Disks).Where(other => other.Id.Equals(disk.Id, StringComparison.OrdinalIgnoreCase))
                .Select(other => other.MaximumConcurrency).Append(disk.MaximumConcurrency).Min();
            if (users.Length >= limit)
                return $"En attente : limite d’E/S sur {disk.Label} ({limit} opération(s) simultanée(s)).";
        }
        var limitForLane = item.IsDownload ? _downloadLimit : item.IsInstallation ? 4 : _maximumConcurrency;
        if (_running.Count(active => active.IsDownload == item.IsDownload && active.IsInstallation == item.IsInstallation) >= limitForLane)
            return item.IsInstallation ? "En attente : limite de 4 installations simultanées." :
                item.IsDownload ? $"En attente : limite de {_downloadLimit} téléchargements simultanés." : "En attente : une autre action est en cours.";
        return null;
    }

    private static bool Overlap(string first, string second) =>
        first.Equals(second, StringComparison.OrdinalIgnoreCase) ||
        first.StartsWith(Path.TrimEndingDirectorySeparator(second) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        second.StartsWith(Path.TrimEndingDirectorySeparator(first) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private async Task ExecuteAsync(WorkItem item)
    {
        try { await item.ExecuteAsync().ConfigureAwait(false); }
        finally { lock (_gate) { _running.Remove(item); } Signal(); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _shutdown.Cancel();
        foreach (var item in _items.Values) item.Cancel();
        Signal();
        await _dispatcher.ConfigureAwait(false);
        foreach (var item in _items.Values) item.Dispose();
        _shutdown.Dispose();
        _wake.Dispose();
    }

    private sealed class WorkItem : IDisposable
    {
        private readonly object _gate = new();
        private readonly HubTaskDefinition _definition;
        private readonly CancellationTokenSource _cancellation;
        private readonly TaskCompletionSource<HubTaskSnapshot> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action<HubTaskSnapshot> _changed;
        private HubTaskSnapshot _snapshot;

        public WorkItem(HubTaskDefinition definition, Action<HubTaskSnapshot> changed,
            IHubTaskDiskResolver disks, CancellationToken shutdownToken)
        {
            if (definition.IsDownload && definition.IsInstallation) throw new ArgumentException("A task must use one lane.", nameof(definition));
            _definition = definition;
            _changed = changed;
            Paths = definition.Resources.ExclusivePaths.Concat(definition.Links.Where(link => link.Kind is "project" or "engine")
                    .Select(link => Path.GetExtension(link.Id).Equals(".uproject", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetDirectoryName(link.Id)! : link.Id))
                .Select(Path.GetFullPath).Select(Path.TrimEndingDirectorySeparator)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Keys = definition.Resources.ExclusiveKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            SharedKeys = definition.Resources.SharedKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Disks = definition.Resources.DiskPaths.Concat(Paths).SelectMany(disks.Resolve).GroupBy(disk => disk.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.MinBy(disk => disk.MaximumConcurrency)!).ToArray();
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            Id = Guid.NewGuid();
            _snapshot = new(Id, definition.Category, definition.Title, HubTaskStatus.Queued, null, null,
                DateTimeOffset.UtcNow, null, null, null) { Links = definition.Links.ToArray(), IsDownload = definition.IsDownload };
        }

        public Guid Id { get; }
        public string Title => _definition.Title;
        public bool IsDownload => _definition.IsDownload;
        public bool IsInstallation => _definition.IsInstallation;
        public string[] Paths { get; }
        public string[] Keys { get; }
        public string[] SharedKeys { get; }
        public HubTaskDisk[] Disks { get; }
        public Task<HubTaskSnapshot> Completion => _completion.Task;
        public HubTaskSnapshot ReadSnapshot() { lock (_gate) { return _snapshot; } }

        public void Waiting(string reason)
        {
            HubTaskSnapshot snapshot;
            lock (_gate)
            {
                if (_snapshot.Status != HubTaskStatus.Queued || _snapshot.Message == reason) return;
                snapshot = _snapshot = _snapshot with { Message = reason };
            }
            _changed(snapshot);
        }

        public void Cancel()
        {
            _cancellation.Cancel();
            HubTaskSnapshot snapshot;
            lock (_gate)
            {
                if (_snapshot.Status != HubTaskStatus.Queued) return;
                snapshot = _snapshot = _snapshot with { Status = HubTaskStatus.Canceled, FinishedAt = DateTimeOffset.UtcNow };
            }
            _changed(snapshot);
            _completion.TrySetResult(snapshot);
        }

        public async Task ExecuteAsync()
        {
            HubTaskSnapshot started;
            lock (_gate)
            {
                if (_snapshot.Status != HubTaskStatus.Queued) return;
                started = _snapshot = _snapshot with { Status = HubTaskStatus.Running, StartedAt = DateTimeOffset.UtcNow, Message = null };
            }
            _changed(started);
            var context = new HubTaskContext((progress, message, indeterminate) => Update(snapshot => snapshot with
            { Progress = indeterminate ? null : progress ?? snapshot.Progress, Message = message ?? snapshot.Message }));
            try
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                await _definition.ExecuteAsync(context, _cancellation.Token).ConfigureAwait(false);
                Complete(HubTaskStatus.Completed, null);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { Complete(HubTaskStatus.Canceled, null); }
            catch (Exception exception) { Complete(HubTaskStatus.Failed, exception.Message); }
        }

        private void Complete(HubTaskStatus status, string? error)
        {
            var snapshot = Update(current => current with
            { Status = status, Progress = status == HubTaskStatus.Completed ? 1 : current.Progress, FinishedAt = DateTimeOffset.UtcNow, Error = error });
            _completion.TrySetResult(snapshot);
        }

        private HubTaskSnapshot Update(Func<HubTaskSnapshot, HubTaskSnapshot> update)
        {
            HubTaskSnapshot snapshot;
            lock (_gate) { snapshot = _snapshot = update(_snapshot); }
            _changed(snapshot);
            return snapshot;
        }

        public void Dispose() => _cancellation.Dispose();
    }
}

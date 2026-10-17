using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CyEngineHub.Core.Tasks;

public sealed class HubTaskScheduler : IAsyncDisposable
{
    private readonly Channel<WorkItem> _channel;
    private readonly Channel<WorkItem> _downloads = Channel.CreateUnbounded<WorkItem>();
    private readonly object _downloadGate = new();
    private readonly SemaphoreSlim _downloadReader = new(1, 1);
    private TaskCompletionSource _downloadChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _downloadLimit = 3;
    private int _activeDownloads;
    private readonly ConcurrentDictionary<Guid, WorkItem> _items = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private bool _disposed;

    public HubTaskScheduler(int maximumConcurrency = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);

        _channel = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = maximumConcurrency == 1,
            SingleWriter = false,
        });

        _workers = Enumerable.Range(0, maximumConcurrency)
            .Select(_ => Task.Run(WorkerAsync))
            .Concat(Enumerable.Range(0, 16).Select(_ => Task.Run(DownloadWorkerAsync)))
            .ToArray();
    }

    public event EventHandler<HubTaskChangedEventArgs>? TaskChanged;

    public IReadOnlyList<HubTaskSnapshot> GetSnapshot() => _items.Values
        .Select(item => item.ReadSnapshot())
        .OrderByDescending(item => item.CreatedAt)
        .ToArray();

    public HubTaskHandle Enqueue(HubTaskDefinition definition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definition);

        var item = new WorkItem(definition, RaiseChanged, _shutdown.Token);
        if (!_items.TryAdd(item.Id, item) || !(definition.IsDownload ? _downloads : _channel).Writer.TryWrite(item))
        {
            item.Dispose();
            throw new InvalidOperationException("The task could not be queued.");
        }

        RaiseChanged(item.ReadSnapshot());
        return new HubTaskHandle(item.Id, item.Completion, item.Cancel);
    }

    public bool Cancel(Guid id)
    {
        if (!_items.TryGetValue(id, out var item))
        {
            return false;
        }

        item.Cancel();
        return true;
    }

    private async Task WorkerAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                await item.ExecuteAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private void RaiseChanged(HubTaskSnapshot snapshot) =>
        TaskChanged?.Invoke(this, new HubTaskChangedEventArgs(snapshot));

    public void SetDownloadConcurrency(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16);
        lock (_downloadGate) { _downloadLimit = count; SignalDownloads(); }
    }

    private void SignalDownloads()
    {
        var previous = _downloadChanged;
        _downloadChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }

    private async Task DownloadWorkerAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                WorkItem item;
                await _downloadReader.WaitAsync(_shutdown.Token).ConfigureAwait(false);
                try
                {
                    if (!await _downloads.Reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false)) return;
                    while (true)
                    {
                        Task changed;
                        lock (_downloadGate)
                        {
                            if (_activeDownloads < _downloadLimit) { _activeDownloads++; break; }
                            changed = _downloadChanged.Task;
                        }
                        await changed.WaitAsync(_shutdown.Token).ConfigureAwait(false);
                    }
                    item = await _downloads.Reader.ReadAsync(_shutdown.Token).ConfigureAwait(false);
                }
                finally { _downloadReader.Release(); }
                try { await item.ExecuteAsync().ConfigureAwait(false); }
                finally { lock (_downloadGate) { _activeDownloads--; SignalDownloads(); } }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _downloads.Writer.TryComplete();
        _shutdown.Cancel();
        foreach (var item in _items.Values) item.Cancel();

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var item in _items.Values)
        {
            item.Dispose();
        }

        _shutdown.Dispose();
        _downloadReader.Dispose();
    }

    private sealed class WorkItem : IDisposable
    {
        private readonly object _gate = new();
        private readonly HubTaskDefinition _definition;
        private readonly CancellationTokenSource _cancellation;
        private readonly TaskCompletionSource<HubTaskSnapshot> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action<HubTaskSnapshot> _changed;
        private HubTaskSnapshot _snapshot;

        public WorkItem(
            HubTaskDefinition definition,
            Action<HubTaskSnapshot> changed,
            CancellationToken shutdownToken)
        {
            _definition = definition;
            _changed = changed;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            Id = Guid.NewGuid();
            _snapshot = new HubTaskSnapshot(
                Id,
                definition.Category,
                definition.Title,
                HubTaskStatus.Queued,
                null,
                null,
                DateTimeOffset.UtcNow,
                null,
                null,
                null) { Links = definition.Links.ToArray(), IsDownload = definition.IsDownload };
        }

        public Guid Id { get; }

        public Task<HubTaskSnapshot> Completion => _completion.Task;

        public HubTaskSnapshot ReadSnapshot()
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }

        public void Cancel()
        {
            _cancellation.Cancel();
            HubTaskSnapshot snapshot;
            lock (_gate)
            {
                if (_snapshot.Status != HubTaskStatus.Queued) return;
                snapshot = _snapshot = _snapshot with
                {
                    Status = HubTaskStatus.Canceled,
                    FinishedAt = DateTimeOffset.UtcNow,
                };
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
                started = _snapshot = _snapshot with
                {
                    Status = HubTaskStatus.Running,
                    StartedAt = DateTimeOffset.UtcNow,
                };
            }
            _changed(started);

            var context = new HubTaskContext((progress, message) => Update(snapshot => snapshot with
            {
                Progress = progress ?? snapshot.Progress,
                Message = message ?? snapshot.Message,
            }));

            try
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                await _definition.ExecuteAsync(context, _cancellation.Token).ConfigureAwait(false);
                Complete(HubTaskStatus.Completed, null);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                Complete(HubTaskStatus.Canceled, null);
            }
            catch (Exception exception)
            {
                Complete(HubTaskStatus.Failed, exception.Message);
            }
        }

        private void Complete(HubTaskStatus status, string? error)
        {
            var snapshot = Update(current => current with
            {
                Status = status,
                Progress = status == HubTaskStatus.Completed ? 1 : current.Progress,
                FinishedAt = DateTimeOffset.UtcNow,
                Error = error,
            });
            _completion.TrySetResult(snapshot);
        }

        private HubTaskSnapshot Update(Func<HubTaskSnapshot, HubTaskSnapshot> update)
        {
            HubTaskSnapshot snapshot;
            lock (_gate)
            {
                _snapshot = update(_snapshot);
                snapshot = _snapshot;
            }

            _changed(snapshot);
            return snapshot;
        }

        public void Dispose() => _cancellation.Dispose();
    }
}

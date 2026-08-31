using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CyEngineHub.Core.Tasks;

public sealed class HubTaskScheduler : IAsyncDisposable
{
    private readonly Channel<WorkItem> _channel;
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
        if (!_items.TryAdd(item.Id, item) || !_channel.Writer.TryWrite(item))
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _shutdown.Cancel();

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
                null);
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

        public void Cancel() => _cancellation.Cancel();

        public async Task ExecuteAsync()
        {
            if (_cancellation.IsCancellationRequested)
            {
                Complete(HubTaskStatus.Canceled, null);
                return;
            }

            Update(snapshot => snapshot with
            {
                Status = HubTaskStatus.Running,
                StartedAt = DateTimeOffset.UtcNow,
            });

            var context = new HubTaskContext((progress, message) => Update(snapshot => snapshot with
            {
                Progress = progress ?? snapshot.Progress,
                Message = message ?? snapshot.Message,
            }));

            try
            {
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

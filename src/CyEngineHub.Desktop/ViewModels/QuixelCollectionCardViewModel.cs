using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class QuixelCollectionCardViewModel : ObservableObject, IDisposable
{
    private readonly RemoteImageCache _imageCache;
    private CancellationTokenSource? _imageCancellation;
    private Bitmap? _thumbnail;
    private bool _disposed;

    public QuixelCollectionCardViewModel(QuixelCollection collection, RemoteImageCache imageCache, Func<string, Task> open)
    {
        Collection = collection;
        _imageCache = imageCache;
        OpenCommand = new AsyncRelayCommand(() => open(collection.Id));
    }

    public QuixelCollection Collection { get; }
    public string Name => Collection.Name;
    public string Category => Collection.Category.Replace(" | ", " · ", StringComparison.Ordinal);
    public string Description => Collection.Description;
    public ICommand OpenCommand { get; }
    public Bitmap? Thumbnail { get => _thumbnail; private set => SetProperty(ref _thumbnail, value); }
    public bool HasThumbnail => Thumbnail is not null;

    public void EnsureThumbnailLoaded()
    {
        if (_disposed || _imageCancellation is not null || Thumbnail is not null) return;
        var cancellation = new CancellationTokenSource();
        _imageCancellation = cancellation;
        _ = LoadAsync(cancellation);
    }

    private async Task LoadAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        Bitmap? bitmap = null;
        try
        {
            bitmap = await _imageCache.LoadThumbnailAsync(Collection.ThumbnailUri, token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested) return;
                Thumbnail = bitmap;
                bitmap = null;
                OnPropertyChanged(nameof(HasThumbnail));
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* A broken image must not prevent browsing collections. */ }
        finally { bitmap?.Dispose(); }
    }

    public void ReleaseThumbnail()
    {
        _imageCancellation?.Cancel();
        _imageCancellation?.Dispose();
        _imageCancellation = null;
        var old = Thumbnail;
        Thumbnail = null;
        OnPropertyChanged(nameof(HasThumbnail));
        old?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseThumbnail();
        GC.SuppressFinalize(this);
    }
}

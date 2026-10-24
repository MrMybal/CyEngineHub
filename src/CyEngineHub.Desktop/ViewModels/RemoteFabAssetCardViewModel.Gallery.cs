using Avalonia.Media.Imaging;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class RemoteFabAssetCardViewModel
{
    private CancellationTokenSource? _galleryPreviewCancellation;
    private int _galleryPreviewGeneration;
    private int _selectedGalleryIndex = -1;
    private bool _galleryDetailsAvailable;
    private string? _galleryImageError;

    public int SelectedGalleryIndex => _selectedGalleryIndex;
    public string GalleryCounter => GalleryImages.Count == 0 ? "0 / 0" : $"{_selectedGalleryIndex + 1} / {GalleryImages.Count}";
    public string GalleryStatus => _galleryImageError ?? (IsGalleryLoading ? "Chargement de l’image…" :
        !_galleryDetailsAvailable ? "Images du catalogue · galerie complète indisponible" :
        GalleryImages.Count == 0 ? "Aucune image disponible" : GalleryImages.Count == 1 ? "1 image disponible" : $"{GalleryImages.Count} images disponibles");

    private static Uri[] NormalizeGalleryUris(IEnumerable<Uri> uris) => uris
        .Where(uri => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
        .Distinct().Take(32).ToArray();

    private void RebuildGallery()
    {
        var previousUri = GalleryImages.ElementAtOrDefault(_selectedGalleryIndex)?.Uri;
        CancelGalleryPreview();
        foreach (var image in GalleryImages) image.Dispose();
        GalleryImages.Clear();
        foreach (var uri in _galleryUris) GalleryImages.Add(new(uri));
        _selectedGalleryIndex = -1;
        if (GalleryImages.Count > 0)
        {
            var previousIndex = Array.IndexOf(_galleryUris.ToArray(), previousUri);
            SelectGalleryImage(Math.Max(0, previousIndex));
        }
        NotifyGalleryState();
    }

    public void SelectGalleryImage(int index, bool retry = false)
    {
        if (_disposed || index < 0 || index >= GalleryImages.Count || index == _selectedGalleryIndex && !retry) return;
        CancelGalleryPreview();
        _selectedGalleryIndex = index;
        _galleryImageError = null;
        var entry = GalleryImages[index];
        var generation = _galleryPreviewGeneration;
        _galleryPreviewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_thumbnailCancellation.Token);
        IsGalleryLoading = true;
        NotifyGalleryState();
        _ = LoadSelectedGalleryImageAsync(entry, generation, _galleryPreviewCancellation.Token);
    }

    public void MoveGalleryImage(int direction)
    {
        if (GalleryImages.Count < 2) return;
        SelectGalleryImage((_selectedGalleryIndex + direction % GalleryImages.Count + GalleryImages.Count) % GalleryImages.Count);
    }

    public void RetryGalleryImages()
    {
        if (_disposed) return;
        foreach (var entry in GalleryImages.Where(entry => entry.Image is null && !entry.IsLoading)) entry.LoadStarted = false;
        SelectGalleryImage(_selectedGalleryIndex, retry: true);
    }

    // Called only for the visible part of the thumbnail strip, never for the whole catalogue.
    public void EnsureGalleryThumbnailLoaded(int index)
    {
        if (_disposed || index < 0 || index >= GalleryImages.Count) return;
        var entry = GalleryImages[index];
        if (entry.LoadStarted) return;
        entry.LoadStarted = true;
        entry.IsLoading = true;
        _ = LoadGalleryThumbnailAsync(entry);
    }

    private async Task LoadGalleryThumbnailAsync(FabGalleryImageViewModel entry)
    {
        Bitmap? image = null;
        try
        {
            image = await _loadGalleryThumbnail(entry.Uri, entry.Cancellation.Token).ConfigureAwait(true);
            if (_disposed || entry.IsDisposed) { image?.Dispose(); return; }
            entry.Image = image;
            if (GalleryImages.ElementAtOrDefault(_selectedGalleryIndex) == entry) NotifyGalleryState();
        }
        catch (OperationCanceledException) { image?.Dispose(); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or ArgumentException)
        { image?.Dispose(); }
        finally { entry.IsLoading = false; }
    }

    private async Task LoadSelectedGalleryImageAsync(FabGalleryImageViewModel entry, int generation, CancellationToken token)
    {
        Bitmap? image = null;
        try
        {
            image = await _loadPreview(entry.Uri, token).ConfigureAwait(true);
            if (_disposed || generation != _galleryPreviewGeneration) { image?.Dispose(); return; }
            SelectedGalleryImage = image;
            if (image is null) _galleryImageError = "Image indisponible · Réessayer la galerie";
        }
        catch (OperationCanceledException) { image?.Dispose(); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            image?.Dispose();
            if (!_disposed && generation == _galleryPreviewGeneration) _galleryImageError = "Image indisponible · Réessayer la galerie";
        }
        finally
        {
            if (!_disposed && generation == _galleryPreviewGeneration)
            { IsGalleryLoading = false; NotifyGalleryState(); }
        }
    }

    private void CancelGalleryPreview()
    {
        _galleryPreviewGeneration++;
        _galleryPreviewCancellation?.Cancel();
        _galleryPreviewCancellation?.Dispose();
        _galleryPreviewCancellation = null;
        var previous = SelectedGalleryImage;
        SelectedGalleryImage = null;
        previous?.Dispose();
        IsGalleryLoading = false;
    }

    private void NotifyGalleryState()
    {
        OnPropertyChanged(nameof(SelectedGalleryIndex));
        OnPropertyChanged(nameof(GalleryCounter));
        OnPropertyChanged(nameof(GalleryStatus));
        OnPropertyChanged(nameof(HasGalleryImages));
        OnPropertyChanged(nameof(PreviewImage));
        OnPropertyChanged(nameof(HasPreviewImage));
    }
}

public sealed class FabGalleryImageViewModel(Uri uri) : IDisposable
{
    public Uri Uri { get; } = uri;
    public Bitmap? Image { get; internal set; }
    internal bool LoadStarted { get; set; }
    internal bool IsLoading { get; set; }
    internal CancellationTokenSource Cancellation { get; } = new();
    internal bool IsDisposed { get; private set; }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Cancellation.Cancel(); Cancellation.Dispose();
        Image?.Dispose(); Image = null;
    }
}

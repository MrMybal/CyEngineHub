using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Remote;
using SkiaSharp;

static partial class Program
{
    static async Task TestFabGallery(Window window, MainWindowViewModel vm)
    {
        vm.FabCacheDirectory = Path.Combine(Output, "GalleryCache");
        var directory = Path.Combine(vm.FabCacheDirectory, "Images");
        Directory.CreateDirectory(directory);
        var uris = Enumerable.Range(0, 5).Select(index => new Uri($"https://gallery-fixture.invalid/image-{index}.png")).ToArray();
        for (var index = 0; index < uris.Length; index++)
        {
            using var bitmap = new SKBitmap(index == 2 ? 700 : 1600, index == 2 ? 1200 : 900);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor((byte)(45 + index * 33), (byte)(105 + index * 12), (byte)(155 - index * 22)));
            using var paint = new SKPaint { Color = new SKColor(240, 220, 180), IsAntialias = true };
            canvas.DrawCircle(bitmap.Width * .65f, bitmap.Height * .35f, bitmap.Width * .15f, paint);
            paint.Color = new SKColor(30, 50, 70); canvas.DrawRect(0, bitmap.Height * .7f, bitmap.Width, bitmap.Height * .3f, paint);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 90);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uris[index].AbsoluteUri)));
            using var file = File.Create(Path.Combine(directory, hash + ".image")); encoded.SaveTo(file);
        }
        var source = new RemoteFabAsset("gallery-fixture", "fab", "Galerie Fab · test local", "## Galerie\n\nImages à la demande, sans téléchargement de pack.", "Fab", "assetPack",
            new Uri("https://example.invalid/listing"), uris[0], ["Architecture"], [], uris.Take(3).ToArray());
        var client = (FixtureFab)typeof(MainWindowViewModel).GetField("_directFabClient", Private)!.GetValue(vm)!;
        client.FailProductDetails = true;
        typeof(MainWindowViewModel).GetField("_remoteFabCatalog", Private)!.SetValue(vm, new[] { source });
        Call(vm, "ApplyDirectFabStatus", new DirectFabStatus(DirectFabConnectionState.Connected, "Fixture catalog"));
        vm.ShowLibraryFabCommand.Execute(null); Call(vm, "ShowCachedRemoteFabPreview");
        using var card = (RemoteFabAssetCardViewModel)typeof(MainWindowViewModel).GetMethod("CreateRemoteAssetCard", Private)!.Invoke(vm, [source])!;
        card.OpenDetailsCommand.Execute(null);
        var selected = vm.SelectedRemoteFabAssetCard!;
        async Task Wait(Func<bool> predicate)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!predicate()) { if (DateTime.UtcNow > until) throw new TimeoutException("Gallery fixture did not settle: " + selected.GalleryStatus); await ImGuiDiagnostics.FramesAsync(window, 2); }
            await ImGuiDiagnostics.FramesAsync(window, 4);
        }
        await Wait(() => selected.GalleryImages.Count == 3 && !selected.IsGalleryLoading && selected.SelectedGalleryImage is not null);
        Check(selected.GalleryStatus.Contains("catalogue", StringComparison.Ordinal), "Offline detail fallback explicitly identifies the incomplete catalogue gallery");
        Check(selected.GalleryImages.Where(entry => entry.Image is not null).All(entry => Math.Max(entry.Image!.PixelSize.Width, entry.Image.PixelSize.Height) <= 192), "Gallery thumbnails use a separate lightweight decode");
        Check(ImGuiDiagnostics.Elements(window).Contains("gallery-thumbnail/2"), "The product fiche exposes its actual image thumbnails");
        await ImGuiDiagnostics.ClickAsync(window, "gallery-thumbnail/1");
        await Wait(() => selected.SelectedGalleryIndex == 1 && !selected.IsGalleryLoading);
        Check(selected.SelectedGalleryImage is not null && selected.GalleryCounter == "2 / 3", "Clicking a thumbnail selects and loads its large image");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-gallery-fiche.png"));
        await ImGuiDiagnostics.ClickAsync(window, "gallery-open");
        Check(vm.IsFabImagePreviewVisible, "The fiche image opens the enlarged native gallery");
        await ImGuiDiagnostics.ClickAsync(window, "gallery-next");
        await Wait(() => selected.SelectedGalleryIndex == 2 && !selected.IsGalleryLoading);
        Check(selected.PreviewImage!.PixelSize.Height > selected.PreviewImage.PixelSize.Width, "Portrait images preserve their aspect ratio");
        await ImGuiDiagnostics.KeyAsync(window, Key.Right);
        await Wait(() => selected.SelectedGalleryIndex == 0 && !selected.IsGalleryLoading);
        await ImGuiDiagnostics.KeyAsync(window, Key.Left);
        await Wait(() => selected.SelectedGalleryIndex == 2 && !selected.IsGalleryLoading);
        Check(selected.GalleryCounter == "3 / 3", "Keyboard arrows wrap at both ends of the gallery");
        await ImGuiDiagnostics.ClickAsync(window, "gallery-zoom-in");
        Check(ImGuiDiagnostics.Elements(window).Contains("gallery-zoom/125"), "Enlarged images support explicit zoom");
        await ImGuiDiagnostics.PointerGestureAsync(window, "gallery-canvas", wheel: 1, drag: new(0, 30));
        Check(ImGuiDiagnostics.Elements(window).Contains("gallery-zoom/150") && ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("gallery-pan/", StringComparison.Ordinal) && key != "gallery-pan/0/0"), "Mouse wheel zoom and dragging pan the enlarged image");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-gallery-viewer-zoom.png"));
        await ImGuiDiagnostics.ClickAsync(window, "gallery-fit");
        Check(ImGuiDiagnostics.Elements(window).Contains("gallery-zoom/100"), "Fit resets zoom and pan");
        window.Width = 1024; await ImGuiDiagnostics.FramesAsync(window, 5);
        var canvasBounds = ImGuiDiagnostics.ElementBounds(window, "gallery-canvas");
        Check(canvasBounds.X > 0 && canvasBounds.Z < 1024 && canvasBounds.W > canvasBounds.Y + 100, "The enlarged viewer fits a 1024-pixel window without overflow");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-gallery-viewer-1024.png"));
        client.ProductDetails = new(source.Description, uris); client.FailProductDetails = false;
        await ImGuiDiagnostics.ClickAsync(window, "gallery-retry");
        await Wait(() => selected.GalleryImages.Count == 5 && !selected.IsGalleryLoading);
        Check(selected.SelectedGalleryIndex == 2 && !selected.GalleryStatus.Contains("indisponible", StringComparison.Ordinal), "Retry replaces the incomplete gallery with online details and preserves the selected image");
        await ImGuiDiagnostics.KeyAsync(window, Key.End);
        await Wait(() => selected.SelectedGalleryIndex == 4 && !selected.IsGalleryLoading);
        await ImGuiDiagnostics.KeyAsync(window, Key.Home);
        await Wait(() => selected.SelectedGalleryIndex == 0 && !selected.IsGalleryLoading);
        await ImGuiDiagnostics.EscapeAsync(window);
        Check(!vm.IsFabImagePreviewVisible && vm.IsRemoteFabProductVisible, "Escape closes only the viewer and keeps the product fiche");
        window.Width = 1440; await ImGuiDiagnostics.FramesAsync(window, 4);
        await TestGalleryLifetime(selected.Organization, source, uris);
        TestGalleryCacheBudget(directory);
    }

    static async Task TestGalleryLifetime(LibraryItemOrganizationViewModel organization, RemoteFabAsset source, Uri[] uris)
    {
        var requests = new List<(Uri Uri, CancellationToken Token, TaskCompletionSource<Bitmap?> Completion)>();
        var thumbnails = 0;
        using var card = new RemoteFabAssetCardViewModel(source, organization, _ => { },
            (_, _) => Task.FromResult<Bitmap?>(null),
            loadPreview: (uri, token) => { var completion = new TaskCompletionSource<Bitmap?>(); requests.Add((uri!, token, completion)); return completion.Task; },
            loadGalleryThumbnail: (_, _) => { thumbnails++; return Task.FromResult<Bitmap?>(null); });
        card.EnsureGalleryLoaded();
        Check(requests.Count == 1 && thumbnails == 0, "Opening a gallery loads only the selected full image, not every large image or thumbnail");
        card.EnsureGalleryThumbnailLoaded(1); card.EnsureGalleryThumbnailLoaded(1);
        Check(thumbnails == 1, "Visible thumbnails are loaded at most once");
        card.SelectGalleryImage(1); card.SelectGalleryImage(2);
        Check(requests[0].Token.IsCancellationRequested && requests[1].Token.IsCancellationRequested, "Rapid navigation cancels obsolete image requests");
        requests[2].Completion.SetResult(null); requests[1].Completion.SetResult(null); requests[0].Completion.SetResult(null);
        await Task.Yield();
        Check(card.SelectedGalleryIndex == 2 && !card.IsGalleryLoading && card.GalleryStatus.Contains("Image indisponible", StringComparison.Ordinal), "Late and failed image responses cannot replace the latest selection or leave the gallery loading");
        card.ApplyProductDetails(new("Updated", uris));
        Check(card.GalleryImages.Count == 5 && requests.Count == 4 && card.SelectedGalleryIndex == 2, "Late product details rebuild an already initialized gallery");
        card.Dispose(); requests[3].Completion.SetResult(null); await Task.Yield();
        Check(requests[3].Token.IsCancellationRequested && card.GalleryImages.Count == 0, "Closing a fiche cancels pending work and releases gallery images");
        using var bounded = new RemoteFabAssetCardViewModel(source with { ImageUris = Enumerable.Range(0, 40).Select(i => new Uri($"https://example.invalid/{i}")).Concat([new Uri("file:///C:/private")]).ToArray() }, organization, _ => { }, (_, _) => Task.FromResult<Bitmap?>(null));
        bounded.EnsureGalleryLoaded();
        Check(bounded.GalleryImages.Count == 32, "Gallery entries are bounded and restricted to safe HTTPS image addresses");
        using var empty = new RemoteFabAssetCardViewModel(source with { ImageUris = [], ThumbnailUri = null }, organization, _ => { }, (_, _) => Task.FromResult<Bitmap?>(null));
        empty.ApplyProductDetails(new("Empty", [])); empty.EnsureGalleryLoaded(); empty.MoveGalleryImage(1);
        Check(empty.GalleryCounter == "0 / 0" && empty.GalleryStatus == "Aucune image disponible", "Products without images stay usable with an explicit empty state");
    }

    static void TestGalleryCacheBudget(string directory)
    {
        var trimDirectory = Path.Combine(directory, "BudgetFixture"); Directory.CreateDirectory(trimDirectory);
        for (var index = 0; index < 4; index++)
        {
            var path = Path.Combine(trimDirectory, index.ToString("X64") + ".image");
            using (var file = File.Create(path)) file.SetLength(100);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(index - 10));
        }
        var userFile = Path.Combine(trimDirectory, "user-file.image"); File.WriteAllText(userFile, "Preserve me");
        var partial = Path.Combine(trimDirectory, new string('A', 64) + ".image.partial"); File.WriteAllText(partial, "Active transfer");
        var preserve = Path.Combine(trimDirectory, 3.ToString("X64") + ".image");
        typeof(RemoteImageCache).GetMethod("TrimCache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [trimDirectory, preserve, 250L]);
        Check(Directory.GetFiles(trimDirectory, "*.image").Where(path => Path.GetFileName(path).Length == 70).Sum(path => new FileInfo(path).Length) <= 250 && File.Exists(preserve), "The image disk cache evicts oldest generated entries within its budget and preserves the active image");
        Check(File.Exists(userFile) && File.Exists(partial), "Cache eviction never deletes user-named files or active partial downloads");
    }
}

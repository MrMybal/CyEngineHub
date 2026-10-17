using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Desktop.Services;
using System.Runtime.CompilerServices;

namespace CyEngineHub.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private sealed class RealizedRemoteFabRow
    {
        public RemoteFabAssetRowViewModel? Value { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, RealizedRemoteFabRow> RealizedRemoteFabRows = new();
    private ExtensionHubWindow? _extensionHubWindow;
    private MainWindowViewModel? _viewModel;
    private CancellationTokenSource? _fabAuthorizationCaptureCancellation;
    private FabAuthorizationWindow? _fabAuthorizationWindow;
    private readonly Dictionary<string, MarketplaceBrowserWindow> _marketplaceWindows = new(StringComparer.OrdinalIgnoreCase);
    private ScrollViewer? _remoteFabScrollViewer;

    public MainWindow()
    {
        if (Environment.GetCommandLineArgs().Contains("--classic", StringComparer.Ordinal)) AvaloniaXamlLoader.Load(this);
        else
        {
            NameScope.SetNameScope(this, new NameScope());
            Title = "CyEngineHub V2 · Alpha";
            Width = 1440; Height = 900; MinWidth = 960; MinHeight = 640;
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://CyEngineHub.Desktop/Assets/Branding/cyenginehub-logo.png")));
            Content = new CyEngineHub.Desktop.ImGui.ImGuiHost("Main", name =>
            {
                switch (name)
                {
                    case "BrowseDerivedSourceFolder": BrowseDerivedSourceFolder(this, new()); break;
                    case "BrowseManualEnginePath": BrowseManualEnginePath(this, new()); break;
                    case "BrowseSourceEngineTarget": BrowseSourceEngineTarget(this, new()); break;
                    case "BrowseEpicEngineTarget": BrowseEpicEngineTarget(this, new()); break;
                    case "BrowseNativeStoreSource": BrowseNativeStoreSource(this, new()); break;
                    case "FocusEngineCompilation":
                        (Content as CyEngineHub.Desktop.ImGui.ImGuiHost)?.Surface.Workspace?.FocusCompilation(); break;
                }
            });
        }
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs args) =>
        AttachViewModel(DataContext as MainWindowViewModel);

    private void FocusEngineCompilation(object? sender, Avalonia.Interactivity.RoutedEventArgs args) =>
        this.FindControl<Border>("EngineCompilationPanel")?.BringIntoView();

    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.ExtensionWindowRequested -= OnExtensionWindowRequested;
            _viewModel.FabAuthorizationCaptureRequested -= OnFabAuthorizationCaptureRequested;
            _viewModel.FabWebAuthorizationRequested -= OnFabWebAuthorizationRequested;
            _viewModel.MarketplaceBrowserRequested -= OnMarketplaceBrowserRequested;
            _viewModel.MarketplaceImportRequested -= OnMarketplaceImportRequested;
            _viewModel.MarketplaceLibraryActivated -= OnMarketplaceLibraryActivated;
            foreach (var browser in _marketplaceWindows.Values.ToArray()) browser.ForceClose();
            _marketplaceWindows.Clear();
            _fabAuthorizationWindow?.Close(new FabWebAuthorizationResult());
            _viewModel.RawLogScrollRequested -= OnRawLogScrollRequested;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.ExtensionWindowRequested += OnExtensionWindowRequested;
            _viewModel.FabAuthorizationCaptureRequested += OnFabAuthorizationCaptureRequested;
            _viewModel.FabWebAuthorizationRequested += OnFabWebAuthorizationRequested;
            _viewModel.MarketplaceBrowserRequested += OnMarketplaceBrowserRequested;
            _viewModel.MarketplaceImportRequested += OnMarketplaceImportRequested;
            _viewModel.MarketplaceLibraryActivated += OnMarketplaceLibraryActivated;
            _viewModel.RawLogScrollRequested += OnRawLogScrollRequested;
        }
    }

    private void OnMarketplaceBrowserRequested(object? sender, MarketplaceBrowserRequestEventArgs request)
    {
        if (_marketplaceWindows.TryGetValue(request.Id, out var existing))
        {
            existing.Show();
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var window = new MarketplaceBrowserWindow(request);
        _marketplaceWindows.Add(request.Id, window);
        window.Closed += (_, _) => _marketplaceWindows.Remove(request.Id);
        window.Show(this);
    }

    private void OnMarketplaceLibraryActivated(object? sender, EventArgs args)
    {
        // Owned browser windows otherwise remain in front of their native library owner.
        // Minimize instead of closing: ongoing downloads and the WebView session are kept.
        foreach (var browser in _marketplaceWindows.Values) browser.WindowState = WindowState.Minimized;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private async void OnMarketplaceImportRequested(object? sender, MarketplaceImportRequestEventArgs args)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Ajouter des fichiers téléchargés · originaux conservés sur place",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Archives et packages") { Patterns = ["*.zip", "*.7z", "*.rar", "*.unitypackage"] }, FilePickerFileTypes.All]
            });
            args.Completion.TrySetResult(files.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray());
        }
        catch (Exception exception) { args.Completion.TrySetException(exception); }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _fabAuthorizationCaptureCancellation?.Cancel();
        _fabAuthorizationCaptureCancellation?.Dispose();
        _fabAuthorizationCaptureCancellation = null;
        DetachRemoteFabScrollViewer();
        AttachViewModel(null);
    }

    private void AttachRemoteFabCatalog(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is not Control catalog)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            DetachRemoteFabScrollViewer();
            _remoteFabScrollViewer = catalog.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_remoteFabScrollViewer is not null)
            {
                _remoteFabScrollViewer.ScrollChanged += OnRemoteFabCatalogScrollChanged;
            }
        });
    }

    private void DetachRemoteFabCatalog(object? sender, VisualTreeAttachmentEventArgs args) =>
        DetachRemoteFabScrollViewer();

    private void DetachRemoteFabScrollViewer()
    {
        if (_remoteFabScrollViewer is not null)
        {
            _remoteFabScrollViewer.ScrollChanged -= OnRemoteFabCatalogScrollChanged;
            _remoteFabScrollViewer = null;
        }
    }

    private void OnRemoteFabCatalogScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (sender is not ScrollViewer scrollViewer || _viewModel is null)
        {
            return;
        }

        var remaining = scrollViewer.Extent.Height - scrollViewer.Offset.Y - scrollViewer.Viewport.Height;
        if (remaining <= Math.Max(600, scrollViewer.Viewport.Height))
        {
            _viewModel.LoadMoreLibraryItems();
        }
    }

    private void OnRawLogScrollRequested(object? sender, EventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Content is CyEngineHub.Desktop.ImGui.ImGuiHost host)
            { host.Surface.Workspace?.ScrollToEnd("RawLogLines"); return; }
            var list = this.FindControl<ListBox>("RawLogList");
            if (list is not null && list.ItemCount > 0)
            {
                list.ScrollIntoView(list.ItemCount - 1);
            }
        });
    }

    private async void BrowseDerivedSourceFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_viewModel is null) return;
        await BrowseFolderAsync("Choisir le dossier déjà préparé à enregistrer comme dérivé", path => _viewModel.DerivedSourceFolder = path);
    }

    private void OnFabAuthorizationCaptureRequested(object? sender, EventArgs args)
    {
        _fabAuthorizationCaptureCancellation?.Cancel();
        _fabAuthorizationCaptureCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _fabAuthorizationCaptureCancellation = cancellation;
        _ = CaptureFabAuthorizationAsync(cancellation);
    }

    private async void OnFabWebAuthorizationRequested(object? sender, FabWebAuthorizationRequestEventArgs request)
    {
        _fabAuthorizationCaptureCancellation?.Cancel();
        if (_fabAuthorizationWindow is not null)
        {
            request.Complete(new FabWebAuthorizationResult());
            return;
        }
        try
        {
            var window = new FabAuthorizationWindow(request);
            _fabAuthorizationWindow = window;
            request.Complete(await window.ShowDialog<FabWebAuthorizationResult?>(this) ?? new FabWebAuthorizationResult());
        }
        catch (Exception)
        {
            request.Complete(new FabWebAuthorizationResult { UseManualMode = true });
        }
        finally { _fabAuthorizationWindow = null; }
    }

    private async Task CaptureFabAuthorizationAsync(CancellationTokenSource cancellation)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                _viewModel?.NotifyFabAuthorizationCaptureTimedOut();
                return;
            }

            var previousText = await clipboard.TryGetTextAsync();
            for (var attempt = 0; attempt < 240; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation.Token);
                var currentText = await clipboard.TryGetTextAsync();
                if (string.IsNullOrWhiteSpace(currentText) ||
                    string.Equals(currentText, previousText, StringComparison.Ordinal))
                {
                    continue;
                }

                previousText = currentText;
                if (_viewModel is not null &&
                    await _viewModel.TryConnectAuthorizationFromClipboardAsync(currentText))
                {
                    return;
                }
            }

            _viewModel?.NotifyFabAuthorizationCaptureTimedOut();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_fabAuthorizationCaptureCancellation, cancellation))
            {
                _fabAuthorizationCaptureCancellation.Dispose();
                _fabAuthorizationCaptureCancellation = null;
            }
        }
    }

    private void OnExtensionWindowRequested(object? sender, EventArgs args)
    {
        if (_extensionHubWindow is not null)
        {
            _extensionHubWindow.Activate();
            return;
        }

        _extensionHubWindow = new ExtensionHubWindow { DataContext = DataContext };
        _extensionHubWindow.Closed += (_, _) => _extensionHubWindow = null;
        _extensionHubWindow.Show(this);
    }

    private static void OpenDetailsFromItem(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not Control item)
        {
            return;
        }

        var current = args.Source as Control;
        while (current is not null && !ReferenceEquals(current, item))
        {
            if (current is Button or TextBox or ComboBox or ToggleSwitch)
            {
                return;
            }

            current = current.Parent as Control;
        }

        var command = item.DataContext switch
        {
            EngineCardViewModel engine => engine.OpenDetailsCommand,
            LocalEngineInstallationCardViewModel engine => engine.OpenDetailsCommand,
            ProjectCardViewModel project => project.OpenDetailsCommand,
            LocalEngineProjectCardViewModel project => project.OpenDetailsCommand,
            _ => null,
        };
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            args.Handled = true;
        }
    }

    private static void LoadRemoteFabThumbnail(
        object? sender,
        VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control { DataContext: RemoteFabAssetCardViewModel card })
        {
            card.EnsureThumbnailLoaded();
        }
    }

    private static void RealizeRemoteFabRow(
        object? sender,
        VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control) TrackRemoteFabRow(control);
    }

    private static void RealizeRemoteFabRowDataContext(object? sender, EventArgs args)
    {
        if (sender is Control control) TrackRemoteFabRow(control);
    }

    private static void RecycleRemoteFabRow(
        object? sender,
        VisualTreeAttachmentEventArgs args)
    {
        if (sender is not Control control) return;
        if (RealizedRemoteFabRows.TryGetValue(control, out var realized))
        {
            realized.Value?.Recycle();
            RealizedRemoteFabRows.Remove(control);
        }
    }

    private static void TrackRemoteFabRow(Control control)
    {
        var realized = RealizedRemoteFabRows.GetOrCreateValue(control);
        var current = control.DataContext as RemoteFabAssetRowViewModel;
        if (ReferenceEquals(realized.Value, current)) return;
        realized.Value?.Recycle();
        realized.Value = current;
        current?.Realize();
    }

    private async void BrowseManualEnginePath(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_viewModel is null)
        {
            return;
        }

        await BrowseFolderAsync("Choisir l’installation du moteur", path => _viewModel.ManualEnginePath = path);
    }

    private async void BrowseSourceEngineTarget(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_viewModel is null)
        {
            return;
        }

        await BrowseFolderAsync("Choisir le dossier cible Unreal Engine", path => _viewModel.SourceEngineTargetDirectory = path);
    }

    private async void BrowseEpicEngineTarget(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_viewModel is null)
        {
            return;
        }

        await BrowseFolderAsync("Choisir le dossier parent du moteur Unreal", path =>
        {
            var selected = _viewModel.SelectedEpicEngineRelease;
            _viewModel.EpicEngineTargetDirectory = Path.Combine(
                path,
                selected?.AppName ?? "UE_Official");
        });
    }

    private async void BrowseNativeStoreSource(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_viewModel is null)
        {
            return;
        }

        await BrowseFolderAsync(_viewModel.PendingNativeStoreTitle, path => _viewModel.PendingNativeStorePath = path);
    }

    private Task BrowseFolderAsync(string title, Action<string> apply) => SafeFolderSelection.SelectAsync(async () =>
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }, apply, message => _viewModel?.ReportFolderSelectionError(message));
}

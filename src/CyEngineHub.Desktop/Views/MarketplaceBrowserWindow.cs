using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CyEngineHub.Desktop.ViewModels;
using Microsoft.Web.WebView2.Core;
using System.Collections.ObjectModel;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using CyEngineHub.Core.Marketplaces;

namespace CyEngineHub.Desktop.Views;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Browser disposed when this window closes.")]
public sealed class MarketplaceBrowserWindow : Window
{
    private readonly MarketplaceBrowserRequestEventArgs _request;
    private readonly MarketplaceWebView _browser;
    private readonly TextBlock _status;
    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _lastDownload;
    private string? _lastCompletedPath;
    private event EventHandler<string>? LocalDownloadCompleted;
    private readonly List<MarketplaceBrowserWindow> _popups = [];
    private readonly ObservableCollection<BrowserDownloadViewModel> _downloadRows = [];
    private readonly Border _downloadPanel;
    private bool _forceClose;

    public MarketplaceBrowserWindow(MarketplaceBrowserRequestEventArgs request, CoreWebView2Environment? environment = null)
    {
        _request = request;
        Title = request.Name + " — Navigateur CyEngineHub";
        Width = 1280; Height = 860; MinWidth = 700; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#0F171F");
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(10), RowSpacing = 8 };
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        _browser = new MarketplaceWebView(environment is null ? request.InventoryUri : null, request.ProfileDirectory,
            request.GetDownloadDirectory, request.RegisterDownload, environment);
        var library = new Button { Content = request.SynchronizeFabLibrary is null ? "Mes assets téléchargés" : "Fiches natives / MCP", Margin = new Thickness(0, 0, 8, 6) };
        library.Click += async (_, _) =>
        {
            try { await request.ShowLocalLibrary(null); }
            catch (Exception) { _status!.Text = "Impossible d’ouvrir la bibliothèque locale."; }
        };
        toolbar.Children.Add(library);
        if (request.SynchronizeFabLibrary is not null && environment is null)
        {
            var sync = new Button { Content = "Synchroniser les fiches Megascans", Margin = new Thickness(0, 0, 8, 6) };
            var stop = new Button { Content = "Annuler la synchronisation", IsVisible = false, Margin = new Thickness(0, 0, 8, 6) };
            CancellationTokenSource? cancellation = null;
            stop.Click += (_, _) => cancellation?.Cancel();
            Closed += (_, _) => cancellation?.Cancel();
            sync.Click += async (_, _) =>
            {
                sync.IsEnabled = false; stop.IsVisible = true;
                cancellation = new CancellationTokenSource();
                try
                {
                    _status!.Text = "Synchronisation des fiches acquises · aucun asset téléchargé…";
                    await request.SynchronizeFabLibrary(_browser.ReadFabLibraryPageAsync, cancellation.Token);
                    _status.Text = "Fiches synchronisées : disponibles dans la bibliothèque native et le MCP.";
                }
                catch (OperationCanceledException) { _status!.Text = "Synchronisation annulée. Cache précédent conservé."; }
                catch (Exception exception) { _status!.Text = exception.Message; }
                finally { cancellation.Dispose(); cancellation = null; sync.IsEnabled = true; stop.IsVisible = false; }
            };
            toolbar.Children.Add(sync); toolbar.Children.Add(stop);
        }
        _lastDownload = new Button { Content = "Voir la fiche du dernier téléchargement", IsEnabled = false, Margin = new Thickness(0, 0, 8, 6) };
        _lastDownload.Click += async (_, _) =>
        {
            var path = _lastCompletedPath;
            if (path is null) return;
            _lastDownload.IsEnabled = false;
            try
            {
                // Also retries indexing when the download succeeded but catalogue writing failed.
                await request.RegisterDownload(path);
                await request.ShowLocalLibrary(path);
            }
            catch (Exception) { _status!.Text = "Fiche indisponible. Le fichier reste dans son dossier de téléchargement."; }
            finally { _lastDownload.IsEnabled = true; }
        };
        toolbar.Children.Add(_lastDownload);
        _back = AddButton(toolbar, "←", _browser.Back);
        _forward = AddButton(toolbar, "→", _browser.Forward);
        AddButton(toolbar, "Actualiser", _browser.Reload);
        var isMegascansFab = request.InventoryUri.Host.Equals("www.fab.com", StringComparison.OrdinalIgnoreCase);
        AddButton(toolbar, isMegascansFab ? "Catalogue Megascans" : "Mon inventaire", () => _browser.Navigate(request.InventoryUri));
        if (isMegascansFab) AddButton(toolbar, "Ma bibliothèque Fab", () => _browser.Navigate(new Uri("https://www.fab.com/library")));
        AddButton(toolbar, "Téléchargements", _browser.ShowDownloads);
        var folder = new Button { Content = "Dossier des téléchargements", Margin = new Thickness(0, 0, 8, 6) };
        folder.Click += async (_, _) =>
        {
            try { await request.OpenDownloadDirectory(); }
            catch (Exception) { _status!.Text = "Dossier indisponible : vérifiez Paramètres → Marketplaces."; }
        };
        toolbar.Children.Add(folder);
        var external = new Button { Content = isMegascansFab ? "Catalogue en externe ↗" : "Inventaire en externe ↗", Margin = new Thickness(0, 0, 8, 6) };
        external.Click += async (_, _) =>
        {
            try { await request.OpenExternal(); }
            catch (Exception) { _status!.Text = "Impossible d’ouvrir le navigateur externe."; }
        };
        toolbar.Children.Add(external);
        layout.Children.Add(toolbar);
        _status = new TextBlock { Text = "Initialisation du navigateur interne…", FontSize = 12, Foreground = Brush.Parse("#8CC2FF"), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(_status, 1); layout.Children.Add(_status);
        _downloadPanel = new Border
        {
            IsVisible = false, Background = Brush.Parse("#172632"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8),
            Child = new ScrollViewer { MaxHeight = 180, Content = new ItemsControl
            {
                ItemsSource = _downloadRows, ItemTemplate = new FuncDataTemplate<BrowserDownloadViewModel>((_, _) => BuildDownloadRow())
            } }
        };
        Grid.SetRow(_downloadPanel, 2); layout.Children.Add(_downloadPanel);
        var container = new Border { Background = Brushes.White, ClipToBounds = true };
        Grid.SetRow(container, 3); layout.Children.Add(container);
        var note = new TextBlock { Text = "Destination : Paramètres → Marketplaces → connecteur → dossier de téléchargement. Un sous-dossier par transfert, sans écrasement. Session conservée sur ce PC ; aucun inventaire extrait automatiquement.", FontSize = 11, Foreground = Brush.Parse("#8294A8"), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(note, 4); layout.Children.Add(note);
        Activated += (_, _) =>
        {
            try { note.Text = "Dossier : " + request.GetDownloadDirectory() + " · Réglable dans Paramètres → Marketplaces. Un sous-dossier par transfert, sans écrasement."; }
            catch (Exception) { note.Text = "Dossier non disponible ou marketplace désactivée : vérifiez Paramètres → Marketplaces."; }
        };
        Content = layout;
        _browser.StatusChanged += (_, message) => _status.Text = message;
        _browser.BrowserStateChanged += (_, _) => UpdateButtons();
        _browser.CloseRequested += (_, _) => Close();
        _browser.LocalDownloadCompleted += (_, path) => OnLocalDownloadCompleted(path);
        _browser.DownloadProgressChanged += (_, snapshot) => UpdateDownload(snapshot);
        _browser.CreatePopupAsync = CreatePopupAsync;
        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows()) container.Child = _browser;
            else _status.Text = "Le navigateur intégré nécessite Windows et Microsoft Edge WebView2.";
        };
        Closing += (_, args) =>
        {
            if (!_forceClose && HasActiveDownloads)
            {
                args.Cancel = true;
                _status.Text = "Téléchargement actif : la fenêtre reste disponible. Terminez ou annulez-le dans Téléchargements avant de fermer.";
            }
        };
        Closed += (_, _) =>
        {
            foreach (var popup in _popups.ToArray()) popup.ForceClose();
            _browser.Dispose();
        };
        UpdateButtons();
    }

    private bool HasActiveDownloads => _browser.ActiveDownloads > 0 || _popups.Any(item => item.HasActiveDownloads);
    public void ForceClose() { _forceClose = true; Close(); }

    private async Task<CoreWebView2?> CreatePopupAsync(CoreWebView2Environment environment)
    {
        var completion = new TaskCompletionSource<CoreWebView2?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var popup = new MarketplaceBrowserWindow(_request, environment);
                _popups.Add(popup);
                popup.LocalDownloadCompleted += (_, path) => OnLocalDownloadCompleted(path);
                popup.Closed += (_, _) => _popups.Remove(popup);
                popup.Show(this);
                completion.TrySetResult(await popup._browser.Ready);
            }
            catch (Exception) { completion.TrySetResult(null); }
        });
        return await completion.Task;
    }

    private void UpdateDownload(DownloadTelemetrySnapshot snapshot)
    {
        var row = _downloadRows.FirstOrDefault(item => item.FilePath == snapshot.FilePath);
        if (row is null) _downloadRows.Insert(0, new BrowserDownloadViewModel(snapshot));
        else row.Update(snapshot);
        _downloadPanel.IsVisible = true;
    }

    private static StackPanel BuildDownloadRow()
    {
        var row = new StackPanel { Spacing = 3, Margin = new Thickness(2, 3, 2, 9) };
        var name = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Bind(TextBlock.TextProperty, new Binding(nameof(BrowserDownloadViewModel.Name)));
        var summary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        summary.Bind(TextBlock.TextProperty, new Binding(nameof(BrowserDownloadViewModel.Summary)));
        var timing = new TextBlock { FontSize = 11, Foreground = Brush.Parse("#A4B3C2"), TextWrapping = TextWrapping.Wrap };
        timing.Bind(TextBlock.TextProperty, new Binding(nameof(BrowserDownloadViewModel.Timing)));
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4 };
        progress.Bind(ProgressBar.ValueProperty, new Binding(nameof(BrowserDownloadViewModel.Percent)));
        progress.Bind(ProgressBar.IsIndeterminateProperty, new Binding(nameof(BrowserDownloadViewModel.IsIndeterminate)));
        row.Children.Add(name); row.Children.Add(summary); row.Children.Add(timing); row.Children.Add(progress);
        return row;
    }

    private void OnLocalDownloadCompleted(string path)
    {
        _lastCompletedPath = path;
        _lastDownload.IsEnabled = true;
        LocalDownloadCompleted?.Invoke(this, path);
    }

    private void UpdateButtons()
    {
        _back.IsEnabled = _browser.CanGoBack;
        _forward.IsEnabled = _browser.CanGoForward;
    }

    private static Button AddButton(WrapPanel parent, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 6) };
        button.Click += (_, _) => action();
        parent.Children.Add(button);
        return button;
    }
}

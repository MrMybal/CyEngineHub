using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CyEngineHub.Desktop.ViewModels;

namespace CyEngineHub.Desktop.Views;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The native browser is disposed when the window closes or reports a failure.")]
public sealed class FabAuthorizationWindow : Window
{
    private readonly FabWebAuthorizationRequestEventArgs _request;
    private readonly TextBlock _status;
    private readonly Border _browserContainer;
    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromMinutes(10) };
    private EpicAuthorizationWebView? _browser;

    public FabAuthorizationWindow(FabWebAuthorizationRequestEventArgs request)
    {
        _request = request;
        Title = $"Connexion {request.ProviderDisplayName} — CyEngineHub";
        Width = 980;
        Height = 800;
        MinWidth = 620;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#0F171F");
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16), RowSpacing = 12 };
        var header = new StackPanel { Spacing = 5 };
        header.Children.Add(new TextBlock { Text = "CONNEXION " + request.ProviderDisplayName.ToUpperInvariant(), FontSize = 20, FontWeight = FontWeight.Bold });
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var back = new Button { Content = "← Retour" };
        back.Click += (_, _) => _browser?.GoBack();
        var reload = new Button { Content = "Actualiser" };
        reload.Click += (_, _) => _browser?.Reload();
        var retry = new Button { Content = "Recommencer la connexion" };
        retry.Click += (_, _) => StartBrowser();
        navigation.Children.Add(back);
        navigation.Children.Add(reload);
        navigation.Children.Add(retry);
        header.Children.Add(navigation);
        header.Children.Add(new TextBlock { Text = "Connectez-vous sur la page officielle. Le code sera récupéré automatiquement.", TextWrapping = TextWrapping.Wrap });
        _status = new TextBlock { Text = "Initialisation…", Foreground = Brush.Parse("#8CC2FF"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        header.Children.Add(_status);
        layout.Children.Add(header);
        _browserContainer = new Border { Background = Brushes.White, ClipToBounds = true };
        Grid.SetRow(_browserContainer, 1);
        layout.Children.Add(_browserContainer);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
        footer.Children.Add(new TextBlock {
            Text = "Session privée WebView2 · aucun mot de passe enregistré.\nUn fournisseur refuse la fenêtre intégrée ? Le mode manuel reste disponible.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var manual = new Button { Content = "Mode manuel", Padding = new Thickness(14, 9), VerticalAlignment = VerticalAlignment.Center };
        manual.Click += (_, _) => Close(new FabWebAuthorizationResult { UseManualMode = true });
        Grid.SetColumn(manual, 1);
        footer.Children.Add(manual);
        var cancel = new Button { Content = "Annuler", Padding = new Thickness(14, 9), VerticalAlignment = VerticalAlignment.Center };
        cancel.Click += (_, _) => Close(new FabWebAuthorizationResult());
        Grid.SetColumn(cancel, 2);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        Content = layout;
        Opened += OnOpened;
        Closed += (_, _) => { _timeout.Stop(); _browser?.Dispose(); };
        KeyDown += (_, args) => { if (args.Key == Key.Escape) Close(new FabWebAuthorizationResult()); };
        _timeout.Tick += (_, _) => ShowFailure("Le délai de connexion est dépassé. Fermez cette fenêtre pour réessayer, ou utilisez le mode manuel.");
    }

    private void OnOpened(object? sender, EventArgs args)
        => StartBrowser();

    private void StartBrowser()
    {
        _timeout.Stop();
        _browser?.Dispose();
        _browserContainer.Child = null;
        _browserContainer.Background = Brushes.White;
        _status.Text = "Initialisation…";
        if (!OperatingSystem.IsWindows())
        {
            ShowFailure("La connexion intégrée nécessite Windows et WebView2. Le mode manuel fonctionne avec votre navigateur.");
            return;
        }
        _browser = new EpicAuthorizationWebView(_request.AuthorizationUri, _request.ProfileDirectory);
        var browser = _browser;
        browser.StatusChanged += (_, message) => { if (ReferenceEquals(_browser, browser)) _status.Text = message; };
        browser.Failed += (_, message) => { if (ReferenceEquals(_browser, browser)) ShowFailure(message); };
        browser.AuthorizationCodeReceived += (_, code) => { if (ReferenceEquals(_browser, browser)) Close(new FabWebAuthorizationResult { Code = code }); };
        browser.CancelRequested += (_, _) => { if (ReferenceEquals(_browser, browser)) Close(new FabWebAuthorizationResult()); };
        _browserContainer.Child = _browser;
        _timeout.Start();
    }

    private void ShowFailure(string message)
    {
        _timeout.Stop();
        _browser?.Dispose();
        _browser = null;
        _status.Text = "Connexion automatique indisponible";
        _browserContainer.Background = Brush.Parse("#162330");
        _browserContainer.Child = new TextBlock { Text = message, Margin = new Thickness(28),
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 16 };
    }
}

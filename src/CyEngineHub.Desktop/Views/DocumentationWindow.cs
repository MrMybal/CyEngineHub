using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CyEngineHub.Desktop.Documentation;

namespace CyEngineHub.Desktop.Views;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The native view is disposed by the window Closed event, following Avalonia ownership.")]
public sealed class DocumentationWindow : Window
{
    private readonly DocumentationLocation _location;
    private readonly DocumentationWebView? _view;
    private readonly Func<Uri, Task> _open;
    private Uri _uri;
    public DocumentationWindow(DocumentationLocation location, Uri uri, string profile, Func<Uri, Task> open, Action<string> languageChanged)
    {
        _location = location; _uri = uri; _open = open;
        Title = "CyEngineHub · Documentation / Aide"; Width = 1280; Height = 850; MinWidth = 640; MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#25282D"));
        var layout = new DockPanel();
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new(4) };
        void Button(string label, Action action) { var button = new Button { Content = label, Margin = new(4) }; button.Click += (_, _) => action(); bar.Children.Add(button); }
        Button("← Back / Précédent", () => _view?.GoBack()); Button("Forward / Suivant →", () => _view?.GoForward());
        Button("Open in browser / Navigateur", () => _ = _open(_uri)); Button("Return to app / Retour", Close);
        DockPanel.SetDock(bar, Dock.Top); layout.Children.Add(bar);
        var status = new TextBlock { Text = "Local documentation · file:// · F1", Margin = new(8), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        if (OperatingSystem.IsWindows())
        {
            _view = new(location, uri, profile);
            _view.Failed += (_, message) => status.Text = message;
            _view.ExternalLinkRequested += (_, link) => _ = _open(link);
            _view.SourceChanged += (_, link) => { _uri = link; var locale = link.Fragment.Split('/').ElementAtOrDefault(1); if (locale is "en" or "fr") languageChanged(locale); };
            layout.Children.Add(_view);
        }
        else status.Text = "Use Open in browser to read the bundled file:// documentation on this platform.";
        Content = layout; Closed += (_, _) => Dispose();
    }
    public void Navigate(string page, string language, string? anchor = null, string? media = null)
    { _uri = _location.CreateUri(page, language, anchor, media); _view?.Navigate(_uri); }
    private void Dispose() => _view?.Dispose();
}

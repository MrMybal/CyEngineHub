using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CyEngineHub.Desktop.Views;

public sealed partial class ExtensionHubWindow : Window
{
    public ExtensionHubWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

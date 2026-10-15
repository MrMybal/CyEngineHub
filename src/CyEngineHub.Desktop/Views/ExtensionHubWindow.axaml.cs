using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CyEngineHub.Desktop.Views;

public sealed partial class ExtensionHubWindow : Window
{
    public ExtensionHubWindow()
    {
        AvaloniaXamlLoader.Load(this);
        if (!Environment.GetCommandLineArgs().Contains("--classic", StringComparer.Ordinal))
            Content = new CyEngineHub.Desktop.ImGui.ImGuiHost("Extensions");
    }
}

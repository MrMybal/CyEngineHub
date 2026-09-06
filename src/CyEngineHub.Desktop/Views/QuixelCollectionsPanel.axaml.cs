using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using CyEngineHub.Desktop.ViewModels;

namespace CyEngineHub.Desktop.Views;

public sealed partial class QuixelCollectionsPanel : UserControl
{
    public QuixelCollectionsPanel() => AvaloniaXamlLoader.Load(this);
    private static void AttachCard(object? sender, VisualTreeAttachmentEventArgs args) => TrackCard(sender);
    private static void ChangeCard(object? sender, EventArgs args) => TrackCard(sender);
    private static void DetachCard(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is not Control control) return;
        (control.Tag as QuixelCollectionCardViewModel)?.ReleaseThumbnail();
        control.Tag = null;
    }
    private static void TrackCard(object? sender)
    {
        if (sender is not Control control) return;
        var current = control.DataContext as QuixelCollectionCardViewModel;
        if (!ReferenceEquals(control.Tag, current))
        {
            (control.Tag as QuixelCollectionCardViewModel)?.ReleaseThumbnail();
            control.Tag = current;
        }
        if (TopLevel.GetTopLevel(control) is not null) current?.EnsureThumbnailLoaded();
    }
}

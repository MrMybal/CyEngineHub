using Avalonia.Controls;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;

static partial class Program
{
    static async Task TestResourceToolbarLayout(Window window, MainWindowViewModel vm)
    {
        var oldWidth = window.Width; var oldDensity = vm.UseCompactInterface;
        foreach (var engines in new[] { true, false })
        {
            if (engines) vm.ShowEnginesCommand.Execute(null); else vm.ShowProjectsCommand.Execute(null);
            vm.SetResourcePresentation(engines, 0);
            var scope = engines ? "engines" : "projects";
            foreach (var width in new[] { 1920, 1280, 1050, 900 })
            foreach (var compact in new[] { true, false })
            {
                window.Width = width; vm.UseCompactInterface = compact;
                await ImGuiDiagnostics.FramesAsync(window, 4);
                var viewFrame = ImGuiDiagnostics.ElementBounds(window, "toolbar-group/resource-display/" + scope);
                var starFrame = ImGuiDiagnostics.ElementBounds(window, "toolbar-group/library-favorites");
                var first = ImGuiDiagnostics.ElementBounds(window, "view-mode-icon/" + scope + "/0");
                var last = ImGuiDiagnostics.ElementBounds(window, "view-mode-icon/" + scope + "/3");
                var star = ImGuiDiagnostics.ElementBounds(window, "favorites/only");
                var counter = ImGuiDiagnostics.ElementBounds(window, "resource-counter");
                var search = ImGuiDiagnostics.ElementBounds(window, "resource-search");
                var analyze = ImGuiDiagnostics.ElementBounds(window, "size-analysis/" + scope);
                Check(ImGuiDiagnostics.Elements(window).Contains("resource-toolbar-row/" + scope), scope + " display modes, favorite filter and counter belong to one explicit toolbar row");
                Check(Math.Abs(first.Y - star.Y) < 1 && Math.Abs(first.W - star.W) < 1 && last.Z < star.X,
                    scope + " display modes and favorite icon share one aligned compact row at " + width + "/" + compact);
                Check(Math.Abs(viewFrame.Y - starFrame.Y) < 1 && Math.Abs(viewFrame.W - starFrame.W) < 1,
                    scope + " display and favorites frames share the same top and height");
                Check(counter.X > starFrame.Z && counter.Z <= search.Z + 1 && Math.Abs((counter.Y + counter.W - star.Y - star.W) / 2) < 2,
                    scope + " item counter is centered beside display controls without a dedicated row");
                Check(viewFrame.Y > analyze.W && starFrame.Z <= search.Z && analyze.Z <= search.Z + 1,
                    scope + " actions and display toolbar stay separate and within the content width");
                var card = ImGuiDiagnostics.Elements(window).FirstOrDefault(key => key.StartsWith("card/body/resource/", StringComparison.Ordinal));
                if (card is not null) Check(ImGuiDiagnostics.ElementBounds(window, card).Y >= viewFrame.W,
                    scope + " cards start below the complete display toolbar");
                if (compact && width is 1920 or 1280 or 900)
                    await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-resource-toolbar-" + scope + "-" + width + ".png"));
            }
            window.Width = 1280; vm.UseCompactInterface = true; vm.SetResourcePresentation(engines, 2);
            await ImGuiDiagnostics.FramesAsync(window, 4);
            var frame = ImGuiDiagnostics.ElementBounds(window, "toolbar-group/resource-display/" + scope);
            var header = ImGuiDiagnostics.ElementBounds(window, "table-header/resources/" + scope + "/0");
            Check(header.Y >= frame.W, scope + " list header starts below the toolbar without overlap");
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-resource-toolbar-" + scope + "-rows.png"));
            vm.SetResourcePresentation(engines, 0);
        }
        window.Width = oldWidth; vm.UseCompactInterface = oldDensity;
        vm.ShowEnginesCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
    }
}

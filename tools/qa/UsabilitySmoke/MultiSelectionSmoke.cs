using Avalonia.Controls;
using Avalonia.Input;
using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Remote;

static partial class Program
{
    static async Task TestMultiSelectionToolbar(Window window, MainWindowViewModel vm, RecordingLauncher launcher)
    {
        var launches = launcher.Launches.Count;
        vm.ShowLibraryFabCommand.Execute(null); vm.ShowImageOnlyLibraryCardsCommand.Execute(null);
        vm.FavoritesOnly = false; vm.FavoritesFirst = false;
        await ImGuiDiagnostics.FramesAsync(window, 4);
        var toolbarBottom = ImGuiDiagnostics.ElementBounds(window, "toolbar-group/library-pagination").W;
        var bodies = ImGuiDiagnostics.Elements(window).Where(key => key.StartsWith("card/body/asset/", StringComparison.Ordinal) &&
                ImGuiDiagnostics.ElementBounds(window, key).Y >= toolbarBottom && ImGuiDiagnostics.ElementBounds(window, key).W < window.Height - 30)
            .OrderBy(key => ImGuiDiagnostics.ElementBounds(window, key).Y).ThenBy(key => ImGuiDiagnostics.ElementBounds(window, key).X).Take(3).ToArray();
        Check(bodies.Length == 3, "Catalog multi-selection fixture has three visible cards");
        await ImGuiDiagnostics.ClickAsync(window, bodies[0]);
        await ImGuiDiagnostics.ClickAsync(window, bodies[1], modifiers: KeyModifiers.Control);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-multi-control-check.png"));
        Check(ImGuiDiagnostics.Elements(window).Contains("selection/count/2"), "Control-click selects two catalog cards without opening a product");
        await ImGuiDiagnostics.ClickAsync(window, bodies[0]);
        await ImGuiDiagnostics.ClickAsync(window, bodies[2], modifiers: KeyModifiers.Shift);
        Check(ImGuiDiagnostics.Elements(window).Contains("selection/count/3"), "Shift-click selects the displayed contiguous card range");
        await ImGuiDiagnostics.ClickAsync(window, bodies[1], modifiers: KeyModifiers.Control);
        Check(ImGuiDiagnostics.Elements(window).Contains("selection/count/2"), "Control-click removes only the clicked card from the group");
        await ImGuiDiagnostics.ClickAsync(window, bodies[0], rightButton: true);
        Check(ImGuiDiagnostics.Elements(window).Contains("item-context/count/2"), "Right-click on a selected card preserves its selected group");
        await ImGuiDiagnostics.ClickAsync(window, "item-context/favorite");
        var catalog = (RemoteFabAsset[])typeof(MainWindowViewModel).GetField("_remoteFabCatalog", Private)!.GetValue(vm)!;
        var selected = new[] { bodies[0], bodies[2] }.Select(key => catalog.Single(asset => key.EndsWith("/" + asset.AssetId, StringComparison.Ordinal))).ToArray();
        Check(selected.All(vm.IsResourceFavorite), "Context favorite action applies to the selected group only");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-fab-multi-selection.png"));
        await ImGuiDiagnostics.ClickAsync(window, "library-view/rows");
        Check(ImGuiDiagnostics.Elements(window).Contains("selection/count/2"), "Changing cards to rows preserves stable multi-selection keys");
        await ImGuiDiagnostics.ClickAsync(window, "library-view/images");
        await ImGuiDiagnostics.ClickAsync(window, "favorites/only");
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        Check(vm.FavoritesOnly && ImGuiDiagnostics.Elements(window).Contains("icon/favorites") &&
            !ImGuiDiagnostics.Elements(window).Contains("favorites/first"), "Favorites uses one icon-only toggle, without a second text button");
        Check(((Array)typeof(MainWindowViewModel).GetField("_visibleRemoteFabCatalog", Private)!.GetValue(vm)!).Length == 2, "Active star displays only the two selected favorites");
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(ImGuiDiagnostics.Elements(window).Contains("favorites/selected"), "Active favorite filter has a visible selected star state");
        await ImGuiDiagnostics.ClickAsync(window, "favorites/only");
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        Check(!vm.FavoritesOnly && ((Array)typeof(MainWindowViewModel).GetField("_visibleRemoteFabCatalog", Private)!.GetValue(vm)!).Length == 240,
            "Turning the star off restores the complete catalog");
        foreach (var asset in selected) vm.ToggleResourceFavorite(asset);
        vm.FavoritesFirst = true;

        foreach (var width in new[] { 1440, 1050 })
        {
            window.Width = width; await ImGuiDiagnostics.FramesAsync(window, 4);
            var refresh = ImGuiDiagnostics.Elements(window).Single(key => key.StartsWith("command/RefreshDirectFabLibraryCommand/", StringComparison.Ordinal));
            var refreshRect = ImGuiDiagnostics.ElementBounds(window, refresh);
            var analyze = ImGuiDiagnostics.ElementBounds(window, "size-analysis/packs");
            var favorites = ImGuiDiagnostics.ElementBounds(window, "toolbar-group/library-favorites");
            var filters = ImGuiDiagnostics.ElementBounds(window, "toolbar-group/library-filters");
            var pagination = ImGuiDiagnostics.ElementBounds(window, "toolbar-group/library-pagination");
            var content = ImGuiDiagnostics.ElementBounds(window, "pane/library/content");
            Check(Math.Abs(refreshRect.Y - analyze.Y) < 2 && analyze.X > refreshRect.Z, "Size analysis sits beside Refresh Fab at width " + width);
            Check(favorites.Y >= filters.Y && favorites.Z <= content.Z, "Framed star belongs to the filter/display toolbar at width " + width);
            Check(pagination.Z > content.Z - 28 && pagination.Z <= content.Z, "Pagination stays aligned to the content's right edge at width " + width);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-catalog-toolbar-" + width + ".png"));
        }
        window.Width = 1280;
        vm.ShowProjectsCommand.Execute(null); vm.SetResourcePresentation(false, 1);
        var original = vm.Projects[0];
        var additions = new List<ProjectCardViewModel>();
        for (var index = 0; index < 2; index++)
        {
            var root = Path.Combine(Output, "MultiProject-" + index); Directory.CreateDirectory(root);
            var project = original.Project with { Id = "multi-" + index, Name = "Multi project " + index, RootPath = root, FilePath = Path.Combine(root, "Fixture.uproject") };
            var organization = new LibraryItemOrganizationViewModel(WorkspaceItemKind.Project, project.FilePath, project.Name, null,
                (_, _, _, _) => Task.CompletedTask, null, error => throw error);
            var card = new ProjectCardViewModel(project, organization, _ => Task.CompletedTask, _ => Task.CompletedTask,
                _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => { }, error => throw error);
            vm.Projects.Add(card); additions.Add(card);
        }
        Call(vm, "ApplyEngineFamilyFilters"); await ImGuiDiagnostics.FramesAsync(window, 4);
        var cards = ImGuiDiagnostics.Elements(window).Where(key => key.StartsWith("card/body/resource/", StringComparison.Ordinal))
            .OrderBy(key => ImGuiDiagnostics.ElementBounds(window, key).Y).ThenBy(key => ImGuiDiagnostics.ElementBounds(window, key).X).Take(3).ToArray();
        await ImGuiDiagnostics.ClickAsync(window, cards[0]);
        await ImGuiDiagnostics.ClickAsync(window, cards[1], modifiers: KeyModifiers.Control);
        Check(ImGuiDiagnostics.Elements(window).Contains("selection/count/2"), "Project cards support the same Control multi-selection");
        await ImGuiDiagnostics.ClickAsync(window, cards[0], rightButton: true);
        Check(ImGuiDiagnostics.Elements(window).Contains("item-context/count/2"), "Project card context menu retains the selected group");
        await ImGuiDiagnostics.EscapeAsync(window);
        await ImGuiDiagnostics.ClickAsync(window, cards[0]);
        await ImGuiDiagnostics.ClickAsync(window, cards[2], modifiers: KeyModifiers.Shift);
        Check(ImGuiDiagnostics.Elements(window).Contains("selection/count/3"), "Project card Shift range follows displayed order");
        await ImGuiDiagnostics.ClickAsync(window, cards[0], rightButton: true);
        await ImGuiDiagnostics.ClickAsync(window, "item-context/size");
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.IsResourceSizeBusy && DateTime.UtcNow < until) await ImGuiDiagnostics.FramesAsync(window, 2);
        Check(!vm.IsResourceSizeBusy && additions.Append(original).All(card => vm.ResourceSizeBytes(card).HasValue),
            "Context size analysis measures every selected project without touching other resources");
        Check(launcher.Launches.Count == launches, "Multi-selection never launches an editor or starts an installation");
        foreach (var card in additions) vm.Projects.Remove(card);
        Call(vm, "ApplyEngineFamilyFilters"); await ImGuiDiagnostics.FramesAsync(window, 3);
    }
}

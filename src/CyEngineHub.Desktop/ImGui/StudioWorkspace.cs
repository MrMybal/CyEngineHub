using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Windows.Input;
using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    internal Dictionary<string, Vector4> Elements { get; } = new(StringComparer.Ordinal);
    private string _resourceQuery = "";
    private object? _selectedResource;
    private string? _resourceEngineSort;
    private UiNode[] _previousModals = [];
    private bool _focusCompilation;
    internal void FocusCompilation() => _focusCompilation = true;
    internal void Record(string key)
    {
        var min = Gui.GetItemRectMin(); var max = Gui.GetItemRectMax();
        if (Gui.IsItemVisible()) Elements[key] = new(min.X, min.Y, max.X, max.Y);
    }
    private static string Value(object? model, string property) => Text(model, "{Binding " + property + "}");
    private static bool Flag(object? model, string property) => UiBinding.Boolean(model, "{Binding " + property + "}", false);
    private static void MutedText(string text)
    {
        Gui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Muted); Gui.TextUnformatted(text); Gui.PopStyleColor();
    }
    private static void TextTooltip(string text) { Gui.BeginTooltip(); Gui.TextUnformatted(text); Gui.EndTooltip(); }
    private void ActionButton(object? model, string label, string command, bool primary = false)
    {
        var action = UiBinding.Read(model, "{Binding " + command + "}") as ICommand;
        Gui.PushID(command);
        Gui.BeginDisabled(action?.CanExecute(null) != true);
        var colorCount = ImGuiTheme.PushActionColors(command, primary);
        if (Gui.Button(label)) Execute(model, "{Binding " + command + "}");
        Record("action/" + command);
        if (command is "ToggleProjectCreatorCommand" or "ToggleEngineImporterCommand") Record("creation-action/" + command);
        if (colorCount > 0) Gui.PopStyleColor(colorCount);
        Gui.EndDisabled(); Gui.PopID();
    }
    private void StudioNav(string label, string command, string active)
    {
        if (Gui.Selectable(label, Flag(_root, active), ImGuiSelectableFlags.None, new(0, 29 * UiScale)))
        {
            _resourceQuery = ""; _selectedResource = null;
            Execute(_root, "{Binding " + command + "}");
        }
        Record("nav/" + label);
    }
    private void DrawStudioShell(Vector2 size)
    {
        // Fixed chrome, independent scroll regions, no scrolling the entire app.
        Gui.BeginChild("studio-body", new(0, -27 * UiScale), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
        Gui.BeginChild("studio-sidebar", new(180 * UiScale, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
        var logsVisible = Flag(_root, "IsLogsNavigationVisible");
        var footerHeight = (logsVisible ? 4 : 3) * (29 * UiScale + Gui.GetStyle().ItemSpacing.Y) +
            Gui.GetTextLineHeightWithSpacing() + Gui.GetStyle().ItemSpacing.Y + 1;
        Gui.BeginChild("sidebar-main", new(0, -footerHeight), ImGuiChildFlags.None);
        if (_logo is not null) { Gui.Image(_image(_logo), new(28)); Gui.SameLine(); }
        Gui.BeginGroup(); Gui.TextUnformatted("CYENGINEHUB"); Gui.TextColored(ImGuiTheme.Accent, "STUDIO  /  V2"); Gui.EndGroup();
        Gui.TextDisabled(Value(_root, "ApplicationVersion"));
        Gui.Spacing(); Gui.Separator(); Gui.Spacing();
        Gui.TextDisabled("ESPACE DE TRAVAIL");
        StudioNav("Vue d’ensemble", "ShowHomeCommand", "IsHomeNavigationActive");
        StudioNav("Moteurs", "ShowEnginesCommand", "IsEnginesNavigationActive");
        StudioNav("Projets", "ShowProjectsCommand", "IsProjectsNavigationActive");
        StudioNav("Bibliothèque", "ShowLibraryCommand", "IsLibraryNavigationActive");
        StudioNav("Extensions", "ShowHubExtensionsCommand", "IsExtensionsNavigationActive");
        Gui.EndChild();
        Gui.Separator();
        Gui.TextDisabled("ACTIVITÉ");
        Record("nav/activity-heading");
        StudioNav("Stockage", "ShowStorageCommand", "IsStorageNavigationActive");
        StudioNav("Téléchargements", "ShowTasksCommand", "IsTasksNavigationActive");
        if (logsVisible) StudioNav("Journal", "ShowLogsCommand", "IsLogsNavigationActive");
        StudioNav("Paramètres", "ShowSettingsCommand", "IsSettingsNavigationActive");
        Gui.EndChild(); Gui.SameLine();
        Gui.BeginChild("studio-workspace", new(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
        if (Flag(_root, "IsEngineDetailsVisible") || Flag(_root, "IsProjectDetailsVisible"))
        {
            var engine = Flag(_root, "IsEngineDetailsVisible");
            ActionButton(_root, engine ? "< Moteurs" : "< Projets", engine ? "ShowEnginesCommand" : "ShowProjectsCommand"); Gui.SameLine();
        }
        Gui.TextUnformatted(Value(_root, "HeaderTitle"));
        var available = Gui.GetContentRegionAvail().X;
        if (available > 650) Gui.SameLine(Math.Max(Gui.GetCursorPosX(), available - 440));
        else Gui.SameLine();
        foreach (var provider in new[] { "Epic", "Fab", "Unity" })
        {
            if (!Flag(_root, "Is" + provider + "ConnectionEnabled")) continue;
            var connected = provider != "Unity" && Flag(_root, "IsDirectFabConnected");
            Gui.PushStyleColor(ImGuiCol.Text, connected ? ImGuiTheme.Success : ImGuiTheme.Warning);
            ActionButton(_root, provider.ToUpperInvariant() + (connected ? " · Connecté" : " · À connecter"), "Show" + provider + "ConnectionSettingsCommand");
            Record("connection/" + provider + (connected ? "/connected" : "/disconnected"));
            Gui.PopStyleColor();
            if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Gui.SetTooltip(Value(_root, provider == "Unity" ? "UnityStoreConnectionStatus" : provider + "ConnectionStatus"));
            Gui.SameLine();
        }
        Gui.NewLine(); Gui.Separator();
        Gui.BeginChild("studio-page", new(0, 0), ImGuiChildFlags.None);
        var page = _document.Pages.FirstOrDefault(node => Visible(node, _root));
        if (page is not null)
        {
            if (_page != page.Id) { _page = page.Id; _error = null; Gui.SetScrollY(0); }
            if (!DrawStudioPage(page)) DrawNode(page, _root);
        }
        Gui.EndChild(); Gui.EndChild(); Gui.EndChild();
        DrawEngineSelectionMenus();
        Gui.Separator();
        Gui.TextColored(_error is null ? ImGuiTheme.Muted : ImGuiTheme.Rgb(0xE58C83),
            Elide(_error ?? Value(_root, "StatusMessage"), Math.Max(20, size.X - 35)));
    }
    private bool DrawStudioPage(UiNode page)
    {
        if (_root is not MainWindowViewModel vm) return false;
        if (vm.IsHomeVisible) { DrawDashboard(vm); return true; }
        if (vm.IsEnginesVisible || vm.IsProjectsVisible)
        {
            DrawResources(vm, vm.IsEnginesVisible);
            return true;
        }
        if (vm.IsStorageVisible) { DrawStorage(vm, page); return true; }
        if (vm.IsTasksVisible) { DrawTasks(vm); return true; }
        if (vm.IsHubExtensionsVisible) { DrawExtensions(vm, page); return true; }
        if (vm.IsPluginsVisible) { DrawPlugins(vm, page); return true; }
        if (vm.IsLibraryVisible || vm.IsSettingsVisible) { DrawSection(vm, page, vm.IsSettingsVisible); return true; }
        // The advanced forms use the same ImGui controls and command pipeline.
        // They are not hidden legacy Avalonia windows.
        return false;
    }
    private void DrawDashboard(MainWindowViewModel vm)
    {
        Gui.TextDisabled("VOTRE ENVIRONNEMENT");
        Gui.TextWrapped("Moteurs, projets et assets réunis dans un espace de travail local.");
        Gui.Spacing();
        if (Gui.BeginTable("overview-metrics", 4, ImGuiTableFlags.SizingStretchSame))
        {
            Metric("MOTEURS", (vm.Engines.Count + vm.LocalEngineInstallations.Count).ToString(CultureInfo.CurrentCulture));
            Metric("PROJETS", vm.Projects.Count.ToString(CultureInfo.CurrentCulture));
            Metric("OPÉRATIONS", vm.Tasks.Count.ToString(CultureInfo.CurrentCulture));
            Metric("PACKS", vm.StoragePacksSize);
            Gui.EndTable();
        }
        Gui.Spacing(); Gui.Separator(); Gui.Spacing();
        ActionButton(vm, "+ Nouveau projet", "ToggleProjectCreatorCommand", true); Gui.SameLine();
        ActionButton(vm, "+ Ajouter un moteur", "ToggleEngineImporterCommand", true); Gui.SameLine();
        ActionButton(vm, "Actualiser", "RefreshCommand");
        Gui.Spacing();
        Gui.TextDisabled("MOTEURS INSTALLÉS");
        DrawResourceTable(vm.Engines.Cast<object>().Concat(vm.LocalEngineInstallations).ToArray(), true, "home-engines", 210);
        Gui.Spacing(); Gui.TextDisabled("PROJETS");
        DrawResourceTable(vm.Projects.Cast<object>().ToArray(), false, "home-projects", 200);
        Gui.Spacing(); Gui.TextDisabled("STOCKAGE");
        foreach (var drive in vm.StorageDrives)
        {
            Gui.TextUnformatted(drive.Name + " · " + drive.Free + " libres");
            Gui.ProgressBar((float)drive.UsedPercent / 100, new(-1, 4), "");
        }
        if (vm.StorageDrives.Count == 0) Gui.TextDisabled("Analyse manuelle depuis Stockage pour mesurer l’espace disponible.");
    }
    private static void Metric(string name, string value)
    {
        Gui.TableNextColumn();
        Gui.BeginChild(name, new(0, 62), ImGuiChildFlags.Borders);
        Gui.TextDisabled(name); Gui.TextUnformatted(value); Gui.EndChild();
    }
    private void DrawResources(MainWindowViewModel vm, bool engines)
    {
        if (engines && _resourceEngineSort != vm.SelectedEngineSort)
        {
            _tableSorts.Remove("resources/engines");
            _resourceEngineSort = vm.SelectedEngineSort;
        }
        var familyProperty = engines ? "SelectedEngineTab" : "SelectedProjectTab";
        var tabs = vm.EngineTabs;
        foreach (var family in tabs)
        {
            var selected = Equals(UiBinding.Read(vm, "{Binding " + familyProperty + "}"), family);
            if (selected) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            if (Gui.Button(family.Label)) { Set(vm, "{Binding " + familyProperty + "}", family); _selectedResource = null; }
            Record("family/" + family.Label);
            if (selected) Gui.PopStyleColor();
            Gui.SameLine();
        }
        Gui.NewLine();
        Gui.SetNextItemWidth(-1);
        Gui.InputTextWithHint("###resource-search", "Rechercher par nom, version ou chemin…", ref _resourceQuery, 4096);
        Record("resource-search");
        if (engines)
        {
            Gui.SetNextItemWidth(145 * UiScale);
            if (Gui.BeginCombo("###engine-sort", vm.SelectedEngineSort))
            {
                foreach (var sort in vm.EngineSortOptions)
                    if (Gui.Selectable(sort, sort == vm.SelectedEngineSort)) Set(vm, "{Binding SelectedEngineSort}", sort);
                Gui.EndCombo();
            }
            Gui.SameLine();
        }
        ActionButton(vm, engines ? "+ Ajouter un moteur" : "+ Nouveau projet", engines ? "ToggleEngineImporterCommand" : "ToggleProjectCreatorCommand", true);
        if (!engines) { Gui.SameLine(); ActionButton(vm, "Importer des projets…", "ToggleManualProjectAddCommand"); }
        else { Gui.SameLine(); ActionButton(vm, "Vérifier les mises à jour", "CheckOfficialEngineUpdatesCommand"); }
        if (engines) { Gui.TextDisabled(Elide(vm.OfficialEngineUpdateSummary, Gui.GetContentRegionAvail().X)); if (Gui.IsItemHovered()) Gui.SetTooltip(vm.OfficialEngineUpdateSummary); }
        IEnumerable<object> values = engines
            ? vm.VisibleEpicEngineDownloads.Cast<object>().Concat(vm.VisibleEngines).Concat(vm.VisibleLocalEngineInstallations)
            : vm.VisibleProjects.Cast<object>().Concat(vm.VisibleLocalEngineProjects);
        var items = values.Where(item => string.IsNullOrWhiteSpace(_resourceQuery) ||
            (ResourceName(item) + " " + ResourceVersion(item) + " " + Value(item, "Path"))
            .Contains(_resourceQuery.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        var cards = engines ? vm.EngineCardsView : vm.ProjectCardsView;
        foreach (var view in new[] { (Label: "Cartes", Cards: true), (Label: "Liste", Cards: false) })
        {
            if (cards == view.Cards) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            if (Gui.Button(view.Label)) Set(vm, engines ? "{Binding EngineCardsView}" : "{Binding ProjectCardsView}", view.Cards);
            Record("resource-view/" + view.Label);
            if (cards == view.Cards) Gui.PopStyleColor();
            Gui.SameLine();
        }
        Gui.TextDisabled(items.Length + (engines ? " moteur(s)" : " projet(s)"));
        if (engines)
        {
            var availablePaths = values.Select(item => Value(item, "Path")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _selectedEnginePaths.IntersectWith(availablePaths);
            Gui.SameLine(); Gui.TextDisabled($"· {_selectedEnginePaths.Count} sélectionné(s) · Ctrl / Maj + clic · clic droit : actions");
        }
        if (_selectedResource is not null && !items.Contains(_selectedResource)) _selectedResource = null;
        if (cards) DrawResourceCards(items, engines);
        else DrawResourceTable(items, engines, "resources", Math.Max(180, Gui.GetContentRegionAvail().Y - 12));
        Record("resources/full-width");
    }
    private static string ResourceName(object item) => Value(item, item is EngineCardViewModel ? "DisplayName" : "Name");
    private static string ResourceVersion(object item) => Value(item, item is EngineCardViewModel ? "VersionLabel" : item is ProjectCardViewModel ? "EngineLabel" : "Version");
    private static string ResourceType(object item) => item is ProjectCardViewModel project
        ? project.ModuleCount > 0 ? "C++" : "Blueprint"
        : Value(item, "Family");
    private void DrawResourceTable(object[] items, bool engines, string id, float height)
    {
        id += engines ? "/engines" : "/projects";
        if (!Gui.BeginTable(id, engines ? 5 : 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp, new(0, height))) return;
        Gui.TableSetupColumn(engines ? "Moteur" : "Projet", ImGuiTableColumnFlags.WidthStretch, 2);
        Gui.TableSetupColumn("Version", ImGuiTableColumnFlags.WidthFixed, 85);
        Gui.TableSetupColumn("Emplacement", ImGuiTableColumnFlags.WidthStretch, 3);
        Gui.TableSetupColumn(engines ? "Taille" : "Type", ImGuiTableColumnFlags.WidthFixed, 105);
        if (engines) Gui.TableSetupColumn("État / mise à jour", ImGuiTableColumnFlags.WidthFixed, 165 * UiScale);
        Gui.TableSetupScrollFreeze(0, 1);
        SortableHeaders(id, engines ? ["Moteur", "Version", "Emplacement", "Taille", "État / mise à jour"] : ["Projet", "Version", "Emplacement", "Type"]);
        items = SortTableItems(id, items, (item, column) => column switch
        { 0 => ResourceName(item), 1 => ResourceVersion(item), 2 => Value(item, "Path"), 4 => Value(item, "OfficialUpdateLabel"), _ => engines ? SizeSortValue(Value(item, "EffectiveSizeLabel")) : ResourceType(item) });
        var rowHeight = Math.Max((items.Any(item => item is EngineDownloadCardViewModel) ? 62 : 30) * UiScale, Gui.GetFrameHeight() + Gui.GetStyle().CellPadding.Y * 2);
        Clipped(items.Length, rowHeight, index =>
        {
            var item = items[index]; Gui.PushID(index); Gui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            if (engines && Flag(item, "IsOfficialEpic") && Flag(item, "IsInstallationAvailable")) Gui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, Gui.ColorConvertFloat4ToU32(ImGuiTheme.OfficialRow));
            Gui.TableNextColumn();
            if (engines && Flag(item, "IsOfficialEpic")) Gui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Success);
            if (Gui.Selectable(ResourceName(item) + (engines && Flag(item, "IsOfficialEpic") ? " · officiel" : "") + "###item", IsResourceSelected(item, engines),
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick | (item is EngineDownloadCardViewModel ? ImGuiSelectableFlags.AllowOverlap : ImGuiSelectableFlags.None), new(0, rowHeight - Gui.GetStyle().CellPadding.Y * 2)))
            {
                SelectResource(item, items, engines);
                if (Gui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Execute(item, "{Binding OpenDetailsCommand}");
            }
            Record("row/" + ResourceName(item));
            if (item is EngineDownloadCardViewModel rowDownload) Record("engine-download/row/" + rowDownload.TaskId);
            RecordEngineSelection(item, engines);
            if (engines && Gui.IsItemClicked(ImGuiMouseButton.Right)) RequestEngineContext(item);
            if (engines && Flag(item, "IsOfficialEpic")) { Record("official-engine/" + Value(item, "Path")); Gui.PopStyleColor(); }
            Gui.TableNextColumn(); Gui.TextUnformatted(ResourceVersion(item));
            Gui.TableNextColumn(); Gui.TextUnformatted(Elide(Value(item, "Path"), Gui.GetContentRegionAvail().X));
            if (Gui.IsItemHovered()) Gui.SetTooltip(Value(item, "Path"));
            if (item is EngineDownloadCardViewModel downloading && Gui.IsItemHovered()) TextTooltip(downloading.Message);
            Gui.TableNextColumn();
            if (item is EngineDownloadCardViewModel download)
            {
                Gui.ProgressBar((float)download.ProgressPercent / 100, new(-1, 7), "");
                MutedText(download.ProgressPercent.ToString("0.#", CultureInfo.CurrentCulture) + " %");
            }
            else Gui.TextDisabled(engines ? Value(item, "EffectiveSizeLabel") : ResourceType(item));
            if (engines)
            {
                Gui.TableNextColumn();
                Gui.TextColored(item is EngineDownloadCardViewModel || !Flag(item, "IsInstallationAvailable") && item is EngineCardViewModel || Flag(item, "OfficialUpdateAvailable") ? ImGuiTheme.Warning : ImGuiTheme.Muted, Elide(Value(item, "OfficialUpdateLabel"), Gui.GetContentRegionAvail().X));
                if (item is EngineDownloadCardViewModel pending)
                {
                    if (pending.CanCancel) { ActionButton(pending, pending.IsCancellationRequested ? "Annulation…" : "Annuler", "CancelCommand"); Record("engine-download/cancel/" + pending.TaskId); }
                    if (pending.CanRetry) { ActionButton(pending, "Reprendre", "RetryCommand", true); Record("engine-download/retry/" + pending.TaskId); }
                    if (Gui.IsItemHovered()) TextTooltip(pending.Message);
                }
            }
            Gui.PopID();
        });
        if (items.Length == 0) { Gui.TableNextRow(); Gui.TableNextColumn(); Gui.TextDisabled("Aucun élément. Ajoutez un dossier ou actualisez la détection."); }
        Gui.EndTable();
    }
    private static unsafe void Clipped(int count, float height, Action<int> draw)
    {
        var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        try { clipper.Begin(count, height); while (clipper.Step()) for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++) draw(i); }
        finally { clipper.Destroy(); }
    }
    private void DrawStorage(MainWindowViewModel vm, UiNode page)
    {
        var labels = new[] { "Vue d’ensemble", "Moteurs", "Projets", "Packs", "Caches / nettoyage" };
        for (var i = 0; i < labels.Length; i++)
        {
            if (i != 0) Gui.SameLine();
            var index = i;
            if (vm.StorageOverviewTab == index) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            if (Gui.Button(labels[i])) Set(vm, "{Binding StorageOverviewTab}", index);
            Record("storage-tab/" + labels[i]);
            if (vm.StorageOverviewTab == index) Gui.PopStyleColor();
        }
        Gui.Separator();
        Gui.TextDisabled(vm.StorageMeasurementDate);
        ActionButton(vm, "Analyser le stockage", "RefreshStorageCommand", true); Gui.SameLine();
        Gui.TextDisabled(vm.StorageStatusSummary);
        if (Gui.BeginTable("storage-metrics", 4, ImGuiTableFlags.SizingStretchSame))
        {
            Metric("MOTEURS", vm.StorageEngineSize); Metric("PROJETS", vm.StorageProjectSize);
            Metric("PACKS", vm.StoragePacksSize); Metric("RÉCUPÉRABLE", vm.StorageReclaimableSize); Gui.EndTable();
        }
        foreach (var drive in vm.StorageDrives)
        {
            Gui.TextUnformatted(drive.Name + "  ·  " + drive.Free + " libres / " + drive.Total);
            Gui.ProgressBar((float)drive.UsedPercent / 100, new(-1, 5), "");
            if (drive.Drive.IsLowSpace) Gui.TextColored(ImGuiTheme.Rgb(0xF0A43A), "Espace faible : vérifiez vos prochains téléchargements.");
        }
        if (!string.IsNullOrWhiteSpace(vm.StorageAnalysisWarnings)) Gui.TextWrapped(vm.StorageAnalysisWarnings);
        if (Gui.CollapsingHeader("Quotas et ordre des bibliothèques"))
            foreach (var location in vm.StorageLibraryCapacities)
            { Gui.TextUnformatted(location.Name + " · " + location.Quota); Gui.TextDisabled(location.Path); Gui.TextWrapped(location.Status); }
        if (vm.HasPendingStorageCleanup)
        {
            Gui.TextWrapped(vm.PendingStorageCleanupSummary);
            ActionButton(vm, "Confirmer le nettoyage", "ConfirmStorageCleanupCommand"); Gui.SameLine();
            ActionButton(vm, "Annuler", "CancelStorageCleanupCommand");
        }
        if (Gui.BeginTable("storage-entries", 4, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable,
            new(0, Math.Max(120, Gui.GetContentRegionAvail().Y - 12))))
        {
            foreach (var label in new[] { "Propriétaire / type", "Taille", "Chemin", "Actions" }) Gui.TableSetupColumn(label);
            Gui.TableSetupScrollFreeze(0, 1); SortableHeaders("storage-entries", ["Propriétaire / type", "Taille", "Chemin", "Actions"], 3);
            foreach (var entry in SortTableItems("storage-entries", vm.FilteredStorageEntries.ToArray(),
                (entry, column) => column switch { 0 => entry.Owner, 1 => entry.Entry.SizeBytes, _ => (object)entry.Path }))
            {
                Gui.PushID(entry.Entry.Id); Gui.TableNextRow(); Gui.TableNextColumn();
                Gui.TextUnformatted(entry.Owner); Gui.TextDisabled(entry.Category);
                Gui.TableNextColumn(); Gui.TextUnformatted(entry.Size);
                Gui.TableNextColumn(); Gui.TextUnformatted(Elide(entry.Path, Gui.GetContentRegionAvail().X));
                if (Gui.IsItemHovered()) Gui.SetTooltip(entry.Path);
                Gui.TableNextColumn(); ActionButton(entry, "Dossier", "OpenFolderCommand");
                if (entry.CanClean) ActionButton(entry, "Préparer le nettoyage", "PrepareCleanupCommand");
                Gui.PopID();
            }
            Gui.EndTable();
        }
    }
    private void DrawTasks(MainWindowViewModel vm)
    {
        Gui.TextDisabled("TÉLÉCHARGEMENTS / INSTALLATIONS / OPÉRATIONS");
        var right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
        ActionButton(vm, "Paramètres de téléchargement", "ShowDownloadSettingsCommand");
        var options = $"{vm.DownloadConcurrency} transferts simultanés · {vm.EngineChunkConcurrency} connexions / moteur";
        if (Gui.GetItemRectMax().X + Gui.GetStyle().ItemSpacing.X + Gui.CalcTextSize(options).X <= right) Gui.SameLine();
        Gui.TextDisabled(options);
        Gui.TextUnformatted(vm.Tasks.Count.ToString(CultureInfo.CurrentCulture) + " opération(s) · la file continue pendant la navigation");
        if (!Gui.BeginTable("task-table", 5, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable,
            new(0, Math.Max(100, Gui.GetContentRegionAvail().Y - 10)))) return;
        Gui.TableSetupColumn("Opération", ImGuiTableColumnFlags.WidthStretch, 4);
        Gui.TableSetupColumn("État", ImGuiTableColumnFlags.WidthFixed, 90);
        Gui.TableSetupColumn("Progression", ImGuiTableColumnFlags.WidthStretch, 1);
        Gui.TableSetupColumn("Durée", ImGuiTableColumnFlags.WidthFixed, 65);
        Gui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 140);
        Gui.TableSetupScrollFreeze(0, 1); SortableHeaders("task-table", ["Opération", "État", "Progression", "Durée", "Actions"], 4);
        foreach (var task in SortTableItems("task-table", vm.Tasks.ToArray(),
            (task, column) => column switch { 0 => task.Title, 1 => task.Status, 2 => task.ProgressPercent, _ => (object)task.DurationSeconds }))
        {
            Gui.PushID(task.Id.ToString()); Gui.TableNextRow(); Gui.TableNextColumn();
            Gui.TextUnformatted(Elide(task.Title, Gui.GetContentRegionAvail().X));
            Record("task/" + task.Id);
            foreach (var line in task.Message.Replace(" · Vitesse :", "\nVitesse :", StringComparison.Ordinal).Split('\n')) MutedText(Elide(line, Gui.GetContentRegionAvail().X));
            if (Gui.IsItemHovered()) TextTooltip(task.Message);
            Gui.TableNextColumn(); Gui.TextUnformatted(task.Status); Gui.TextDisabled(task.Category);
            Gui.TableNextColumn(); Gui.ProgressBar((float)task.ProgressPercent / 100, new(-1, 5), "");
            MutedText(task.ProgressPercent.ToString("0.#", CultureInfo.CurrentCulture) + " %");
            Gui.TableNextColumn(); Gui.TextUnformatted(task.Duration);
            Gui.TableNextColumn();
            if (task.CanCancel) { ActionButton(task, "Annuler", "CancelCommand"); Record("task/cancel/" + task.Id); }
            var resumable = vm.EpicEngineDownloads.FirstOrDefault(item => item.TaskId == task.Id && item.CanRetry);
            if (resumable is not null) { ActionButton(resumable, "Reprendre", "RetryCommand", true); Record("task/retry/" + task.Id); }
            var linkIndex = 0;
            foreach (var link in task.Links) { Gui.PushID(linkIndex++); ActionButton(link, link.Label, "OpenCommand"); Gui.PopID(); }
            Gui.PopID();
        }
        if (vm.Tasks.Count == 0) { Gui.TableNextRow(); Gui.TableNextColumn(); Gui.TextDisabled("Aucune opération pour le moment."); }
        Gui.EndTable();
    }
}

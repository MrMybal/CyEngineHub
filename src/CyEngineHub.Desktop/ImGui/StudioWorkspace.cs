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
    private bool _showAdvancedResources;
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
    private void ActionButton(object? model, string label, string command, bool primary = false)
    {
        var action = UiBinding.Read(model, "{Binding " + command + "}") as ICommand;
        Gui.PushID(command);
        Gui.BeginDisabled(action?.CanExecute(null) != true);
        if (primary) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Rgb(0x5A497E));
        if (Gui.Button(label)) Execute(model, "{Binding " + command + "}");
        Record("action/" + command);
        if (primary) Gui.PopStyleColor();
        Gui.EndDisabled(); Gui.PopID();
    }
    private void StudioNav(string label, string command, string active)
    {
        if (Gui.Selectable(label, Flag(_root, active), ImGuiSelectableFlags.None, new(0, 29)))
        {
            _resourceQuery = ""; _selectedResource = null;
            _showAdvancedResources = false;
            Execute(_root, "{Binding " + command + "}");
        }
        Record("nav/" + label);
    }
    private void DrawStudioShell(Vector2 size)
    {
        // Fixed chrome, independent scroll regions, no scrolling the entire app.
        Gui.BeginChild("studio-body", new(0, -27), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
        Gui.BeginChild("studio-sidebar", new(180, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
        if (_logo is not null) { Gui.Image(_image(_logo), new(28)); Gui.SameLine(); }
        Gui.BeginGroup(); Gui.TextUnformatted("CYENGINEHUB"); Gui.TextColored(ImGuiTheme.Accent, "STUDIO  /  V2 ALPHA"); Gui.EndGroup();
        Gui.TextDisabled(Value(_root, "ApplicationVersion"));
        Gui.Spacing(); Gui.Separator(); Gui.Spacing();
        Gui.TextDisabled("ESPACE DE TRAVAIL");
        StudioNav("Vue d’ensemble", "ShowHomeCommand", "IsHomeNavigationActive");
        StudioNav("Moteurs", "ShowEnginesCommand", "IsEnginesNavigationActive");
        StudioNav("Projets", "ShowProjectsCommand", "IsProjectsNavigationActive");
        StudioNav("Bibliothèque", "ShowLibraryCommand", "IsLibraryNavigationActive");
        StudioNav("Extensions", "ShowHubExtensionsCommand", "IsExtensionsNavigationActive");
        StudioNav("Stockage", "ShowStorageCommand", "IsStorageNavigationActive");
        Gui.Spacing(); Gui.Separator(); Gui.Spacing();
        Gui.TextDisabled("ACTIVITÉ");
        StudioNav("Téléchargements", "ShowTasksCommand", "IsTasksNavigationActive");
        if (Flag(_root, "IsLogsNavigationVisible")) StudioNav("Journal", "ShowLogsCommand", "IsLogsNavigationActive");
        StudioNav("Paramètres", "ShowSettingsCommand", "IsSettingsNavigationActive");
        Gui.Spacing(); Gui.Separator();
        var compact = _compact;
        if (Gui.Checkbox("Densité compacte", ref compact)) Set(_root, "{Binding UseCompactInterface}", compact);
        Record("density");
        Gui.TextDisabled("Dear ImGui / GPU");
        Gui.EndChild(); Gui.SameLine();
        Gui.BeginChild("studio-workspace", new(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
        ActionButton(_root, "<", "NavigateBackCommand"); Gui.SameLine();
        ActionButton(_root, ">", "NavigateForwardCommand"); Gui.SameLine();
        Gui.TextUnformatted(Value(_root, "HeaderTitle"));
        var available = Gui.GetContentRegionAvail().X;
        if (available > 650) Gui.SameLine(Math.Max(Gui.GetCursorPosX(), available - 295));
        else Gui.SameLine();
        foreach (var provider in new[] { "Epic", "Fab", "Unity" })
        {
            if (!Flag(_root, "Is" + provider + "ConnectionEnabled")) continue;
            ActionButton(_root, provider.ToUpperInvariant(), "Show" + provider + "ConnectionSettingsCommand");
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
            if (_showAdvancedResources || Flag(vm, "IsEngineImporterVisible"))
            {
                if (Gui.Button("< Revenir à la vue compacte")) _showAdvancedResources = false;
                DrawNode(page, vm);
            }
            else DrawResources(vm, vm.IsEnginesVisible);
            return true;
        }
        if (vm.IsStorageVisible) { DrawStorage(vm, page); return true; }
        if (vm.IsTasksVisible) { DrawTasks(vm); return true; }
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
        ActionButton(vm, "+ Ajouter un moteur", "ToggleEngineImporterCommand"); Gui.SameLine();
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
        var familyProperty = engines ? "SelectedEngineTab" : "SelectedProjectTab";
        var tabs = vm.EngineTabs;
        foreach (var family in tabs)
        {
            var selected = Equals(UiBinding.Read(vm, "{Binding " + familyProperty + "}"), family);
            if (selected) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Rgb(0x51445F));
            if (Gui.Button(family.Label)) { Set(vm, "{Binding " + familyProperty + "}", family); _selectedResource = null; }
            Record("family/" + family.Label);
            if (selected) Gui.PopStyleColor();
            Gui.SameLine();
        }
        Gui.NewLine();
        Gui.SetNextItemWidth(Math.Max(140, Gui.GetContentRegionAvail().X - 480));
        Gui.InputTextWithHint("###resource-search", "Rechercher par nom, version ou chemin…", ref _resourceQuery, 4096);
        Record("resource-search");
        Gui.SameLine();
        if (engines)
        {
            Gui.SetNextItemWidth(145);
            if (Gui.BeginCombo("###engine-sort", vm.SelectedEngineSort))
            {
                foreach (var sort in vm.EngineSortOptions)
                    if (Gui.Selectable(sort, sort == vm.SelectedEngineSort)) Set(vm, "{Binding SelectedEngineSort}", sort);
                Gui.EndCombo();
            }
            Gui.SameLine();
        }
        ActionButton(vm, engines ? "+ Ajouter" : "+ Créer", engines ? "ToggleEngineImporterCommand" : "ToggleProjectCreatorCommand", true);
        Gui.SameLine();
        if (!engines) { ActionButton(vm, "Profils", "ShowProjectProfilesCommand"); Gui.SameLine(); }
        if (Gui.Button("Outils / options")) _showAdvancedResources = true;
        Record("resource-tools");
        IEnumerable<object> values = engines
            ? vm.VisibleEngines.Cast<object>().Concat(vm.VisibleLocalEngineInstallations)
            : vm.VisibleProjects.Cast<object>().Concat(vm.VisibleLocalEngineProjects);
        var items = values.Where(item => string.IsNullOrWhiteSpace(_resourceQuery) ||
            (ResourceName(item) + " " + ResourceVersion(item) + " " + Value(item, "Path"))
            .Contains(_resourceQuery.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        Gui.TextDisabled(items.Length + (engines ? " moteur(s)" : " projet(s)") + "  ·  double-clic pour ouvrir la fiche");
        if (Gui.BeginTable("resource-split", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
        {
            Gui.TableSetupColumn("Liste", ImGuiTableColumnFlags.WidthStretch, 3);
            Gui.TableSetupColumn("Inspecteur", ImGuiTableColumnFlags.WidthStretch, 1);
            Gui.TableNextColumn(); DrawResourceTable(items, engines, "resources", Math.Max(180, Gui.GetContentRegionAvail().Y - 12));
            Gui.TableNextColumn();
            Gui.BeginChild("resource-inspector", new(0, 0), ImGuiChildFlags.Borders);
            Gui.TextDisabled("INSPECTEUR"); Gui.Separator();
            if (_selectedResource is null) Gui.TextWrapped("Sélectionnez une ligne pour afficher ses actions et informations.");
            else
            {
                Gui.TextWrapped(ResourceName(_selectedResource));
                Gui.TextColored(ImGuiTheme.Accent, ResourceVersion(_selectedResource));
                Gui.TextWrapped(Value(_selectedResource, "Path"));
                Gui.Spacing(); Gui.Separator(); Gui.Spacing();
                ActionButton(_selectedResource, engines ? "Lancer le moteur" : "Ouvrir le projet", "LaunchCommand", true);
                ActionButton(_selectedResource, "Fiche / configuration", "OpenDetailsCommand");
                ActionButton(_selectedResource, "Ouvrir le dossier", "OpenFolderCommand");
                Gui.Spacing(); Gui.TextDisabled(engines ? "TAILLE SUR DISQUE" : "TYPE");
                Gui.TextWrapped(Value(_selectedResource, engines ? "EffectiveSizeLabel" : "TypeLabel"));
                Gui.TextWrapped(Value(_selectedResource, "IssueSummary"));
            }
            Gui.EndChild(); Gui.EndTable();
        }
    }
    private static string ResourceName(object item) => Value(item, item is EngineCardViewModel ? "DisplayName" : "Name");
    private static string ResourceVersion(object item) => Value(item, item is EngineCardViewModel ? "VersionLabel" : item is ProjectCardViewModel ? "EngineLabel" : "Version");
    private static string ResourceType(object item) => item is ProjectCardViewModel project
        ? project.ModuleCount > 0 ? "C++" : "Blueprint"
        : Value(item, "Family");
    private void DrawResourceTable(object[] items, bool engines, string id, float height)
    {
        if (!Gui.BeginTable(id, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp, new(0, height))) return;
        Gui.TableSetupColumn(engines ? "Moteur" : "Projet", ImGuiTableColumnFlags.WidthStretch, 2);
        Gui.TableSetupColumn("Version", ImGuiTableColumnFlags.WidthFixed, 85);
        Gui.TableSetupColumn("Emplacement", ImGuiTableColumnFlags.WidthStretch, 3);
        Gui.TableSetupColumn(engines ? "Taille" : "Type", ImGuiTableColumnFlags.WidthFixed, 105);
        Gui.TableSetupScrollFreeze(0, 1); Gui.TableHeadersRow();
        Clipped(items.Length, 30, index =>
        {
            var item = items[index]; Gui.PushID(index); Gui.TableNextRow(ImGuiTableRowFlags.None, 30);
            Gui.TableNextColumn();
            if (Gui.Selectable(ResourceName(item) + "###item", ReferenceEquals(item, _selectedResource),
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick, new(0, 26)))
            {
                _selectedResource = item;
                if (Gui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Execute(item, "{Binding OpenDetailsCommand}");
            }
            Record("row/" + ResourceName(item));
            Gui.TableNextColumn(); Gui.TextUnformatted(ResourceVersion(item));
            Gui.TableNextColumn(); Gui.TextUnformatted(Elide(Value(item, "Path"), Gui.GetContentRegionAvail().X));
            if (Gui.IsItemHovered()) Gui.SetTooltip(Value(item, "Path"));
            Gui.TableNextColumn(); Gui.TextDisabled(engines ? Value(item, "EffectiveSizeLabel") : ResourceType(item));
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
            if (vm.StorageOverviewTab == index) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Rgb(0x51445F));
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
            Gui.TableSetupScrollFreeze(0, 1); Gui.TableHeadersRow();
            foreach (var entry in vm.FilteredStorageEntries)
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
        Gui.TextUnformatted(vm.Tasks.Count.ToString(CultureInfo.CurrentCulture) + " opération(s) · la file continue pendant la navigation");
        if (!Gui.BeginTable("task-table", 5, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable,
            new(0, Math.Max(100, Gui.GetContentRegionAvail().Y - 10)))) return;
        Gui.TableSetupColumn("Opération", ImGuiTableColumnFlags.WidthStretch, 4);
        Gui.TableSetupColumn("État", ImGuiTableColumnFlags.WidthFixed, 90);
        Gui.TableSetupColumn("Progression", ImGuiTableColumnFlags.WidthStretch, 1);
        Gui.TableSetupColumn("Durée", ImGuiTableColumnFlags.WidthFixed, 65);
        Gui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 140);
        Gui.TableSetupScrollFreeze(0, 1); Gui.TableHeadersRow();
        foreach (var task in vm.Tasks)
        {
            Gui.PushID(task.Id.ToString()); Gui.TableNextRow(); Gui.TableNextColumn();
            Gui.TextUnformatted(Elide(task.Title, Gui.GetContentRegionAvail().X));
            Gui.TextDisabled(Elide(task.Message, Gui.GetContentRegionAvail().X));
            if (Gui.IsItemHovered()) Gui.SetTooltip(task.Message);
            Gui.TableNextColumn(); Gui.TextUnformatted(task.Status); Gui.TextDisabled(task.Category);
            Gui.TableNextColumn(); Gui.ProgressBar((float)task.ProgressPercent / 100, new(-1, 5), "");
            Gui.TextDisabled(task.ProgressPercent.ToString("0.#", CultureInfo.CurrentCulture) + " %");
            Gui.TableNextColumn(); Gui.TextUnformatted(task.Duration);
            Gui.TableNextColumn();
            if (task.CanCancel) ActionButton(task, "Annuler", "CancelCommand");
            var linkIndex = 0;
            foreach (var link in task.Links) { Gui.PushID(linkIndex++); ActionButton(link, link.Label, "OpenCommand"); Gui.PopID(); }
            Gui.PopID();
        }
        if (vm.Tasks.Count == 0) { Gui.TableNextRow(); Gui.TableNextColumn(); Gui.TextDisabled("Aucune opération pour le moment."); }
        Gui.EndTable();
    }
}

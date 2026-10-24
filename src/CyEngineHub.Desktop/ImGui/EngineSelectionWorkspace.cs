using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private readonly HashSet<string> _selectedEnginePaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _engineSelectionAnchor;
    private bool _engineContextRequested;
    private string[]? _engineRemovalPaths;
    private bool _engineRemovalRequested;

    private bool IsResourceSelected(object item, bool engines) => engines
        ? _selectedEnginePaths.Contains(Value(item, "Path")) : IsItemSelected(item);

    private void SelectResource(object item, object[] items, bool engines)
    {
        _selectedResource = item;
        if (!engines) { SelectItem(item); return; }
        var path = Value(item, "Path");
        var io = Gui.GetIO();
        if (io.KeyShift && _engineSelectionAnchor is not null)
        {
            var anchor = Array.FindIndex(items, entry => Value(entry, "Path") == _engineSelectionAnchor);
            var current = Array.IndexOf(items, item);
            if (!io.KeyCtrl) _selectedEnginePaths.Clear();
            if (anchor >= 0 && current >= 0)
                for (var index = Math.Min(anchor, current); index <= Math.Max(anchor, current); index++)
                    _selectedEnginePaths.Add(Value(items[index], "Path"));
            else _selectedEnginePaths.Add(path);
        }
        else
        {
            if (!io.KeyCtrl) _selectedEnginePaths.Clear();
            if (!_selectedEnginePaths.Add(path) && io.KeyCtrl) _selectedEnginePaths.Remove(path);
            _engineSelectionAnchor = path;
        }
    }

    private void RequestEngineContext(object item)
    {
        var path = Value(item, "Path");
        if (!_selectedEnginePaths.Contains(path))
        {
            _selectedEnginePaths.Clear(); _selectedEnginePaths.Add(path);
            _engineSelectionAnchor = path;
        }
        _selectedResource = item;
        _engineContextRequested = true;
    }

    private void RecordEngineSelection(object item, bool engines)
    {
        if (engines && IsResourceSelected(item, true)) Record("engine/selected/" + Value(item, "Path"));
    }

    private void DrawEngineSelectionMenus()
    {
        if (_root is not MainWindowViewModel vm) return;
        if (_engineContextRequested) { Gui.OpenPopup("engine-context"); _engineContextRequested = false; }
        if (Gui.BeginPopup("engine-context"))
        {
            var paths = _selectedEnginePaths.ToArray();
            Gui.TextDisabled(paths.Length + " moteur(s) sélectionné(s)"); Gui.Separator();
            if (_selectedResource is { } favoriteEngine)
            {
                var addFavorite = !vm.IsResourceFavorite(favoriteEngine);
                if (Gui.MenuItem(addFavorite ? "Ajouter aux favoris" : "Retirer des favoris"))
                    _actions.Enqueue(() => vm.SetResourceFavorites(vm.Engines.Cast<object>().Concat(vm.LocalEngineInstallations).Where(item => paths.Contains(Value(item, "Path"))), addFavorite));
                Record("engine-context/favorite");
            }
            var resumableDownloads = vm.EpicEngineDownloads.Where(item => item.CanRetry && paths.Contains(item.Path)).ToArray();
            if (resumableDownloads.Length > 0 && Gui.MenuItem("Reprendre les téléchargements sélectionnés"))
                _actions.Enqueue(() => { foreach (var download in resumableDownloads) if (download.RetryCommand.CanExecute(null)) download.RetryCommand.Execute(null); });
            if (resumableDownloads.Length > 0) Record("engine-context/retry-downloads");
            var activeDownload = vm.EpicEngineDownloads.Any(item => item.CanCancel && paths.Contains(item.Path));
            if (activeDownload)
            {
                if (Gui.MenuItem("Annuler les téléchargements sélectionnés")) _actions.Enqueue(() => vm.RunEngineSelectionAction(paths, "cancel-downloads"));
                Record("engine-context/cancel-downloads"); Gui.Separator();
            }
            Gui.BeginDisabled(vm.IsEngineSelectionBusy);
            Gui.BeginDisabled(activeDownload);
            if (Gui.MenuItem("Vérifier les installations")) _actions.Enqueue(() => vm.RunEngineSelectionAction(paths, "check"));
            Record("engine-context/check");
            if (Gui.IsItemHovered()) Gui.SetTooltip("Vérifie les dossiers, Build.version, l’éditeur et les outils locaux. Aucun téléchargement.");
            if (Gui.MenuItem("Analyser la taille des moteurs sélectionnés")) _actions.Enqueue(() => vm.RunEngineSelectionAction(paths, "size"));
            Record("engine-context/size");
            if (Gui.IsItemHovered()) Gui.SetTooltip("Analyse ciblée en arrière-plan, annulable dans Actions. Aucune analyse des autres dossiers.");
            Gui.BeginDisabled(vm.IsCheckingOfficialEngineUpdates || !vm.Engines.Any(engine => paths.Contains(engine.Path) && engine.IsOfficialEpic));
            if (Gui.MenuItem("Rechercher les mises à jour officielles")) _actions.Enqueue(() => vm.RunEngineSelectionAction(paths, "updates"));
            Record("engine-context/updates");
            if (Gui.IsItemHovered()) Gui.SetTooltip("Compare les révisions officielles de la même branche. Aucun remplacement automatique.");
            if (paths.Length == 1 && vm.Engines.FirstOrDefault(engine => paths.Contains(engine.Path)) is { IsOfficialEpic: true, OfficialUpdateAvailable: true } updateEngine)
            {
                if (Gui.MenuItem("Mettre à jour le moteur…")) _actions.Enqueue(() => vm.RequestOfficialEngineUpdate(updateEngine));
                Record("engine-context/apply-update");
            }
            Gui.EndDisabled();
            if (paths.Length == 1 && Gui.MenuItem("Ouvrir la fiche / options")) Execute(_selectedResource, "{Binding OpenDetailsCommand}");
            Gui.Separator();
            if (Gui.MenuItem("Retirer du catalogue…")) { _engineRemovalPaths = paths; _engineRemovalRequested = true; }
            Record("engine-context/remove");
            Gui.EndDisabled();
            Gui.EndDisabled(); Gui.EndPopup();
        }
        if (_engineRemovalRequested) { Gui.OpenPopup("Retirer les moteurs###remove-engines"); _engineRemovalRequested = false; }
        Gui.SetNextWindowSize(new(510 * UiScale, 0), ImGuiCond.Appearing);
        if (Gui.BeginPopupModal("Retirer les moteurs###remove-engines", ImGuiWindowFlags.AlwaysAutoResize))
        {
            Gui.TextWrapped($"Retirer {_engineRemovalPaths?.Length ?? 0} moteur(s) du catalogue ? Aucun fichier ne sera supprimé.");
            Gui.TextWrapped("Ils pourront être réintégrés depuis les paramètres de bibliothèque.");
            Gui.Spacing();
            if (Gui.Button("Annuler")) { _engineRemovalPaths = null; Gui.CloseCurrentPopup(); }
            Record("engine-remove/cancel"); Gui.SameLine();
            if (Gui.Button("Retirer du catalogue"))
            {
                var paths = _engineRemovalPaths ?? [];
                _actions.Enqueue(() => vm.RunEngineSelectionAction(paths, "remove"));
                _selectedEnginePaths.Clear(); _engineRemovalPaths = null; Gui.CloseCurrentPopup();
            }
            Record("engine-remove/confirm"); Gui.EndPopup();
        }
    }
}

using CyEngineHub.Unreal.Detection;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _engineSelectionBusy;
    public bool IsEngineSelectionBusy => _engineSelectionBusy;

    public void RunEngineSelectionAction(IReadOnlyList<string> paths, string action) =>
        _ = RunEngineSelectionActionAsync(paths.ToArray(), action);

    private async Task RunEngineSelectionActionAsync(string[] paths, string action)
    {
        if (_engineSelectionBusy || paths.Length == 0) return;
        SetProperty(ref _engineSelectionBusy, true, nameof(IsEngineSelectionBusy));
        try
        {
            var selected = paths.ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            if (action == "cancel-downloads")
            {
                foreach (var item in EpicEngineDownloads.Where(item => selected.Contains(item.Path) && item.CancelCommand.CanExecute(null)))
                    item.CancelCommand.Execute(null);
            }
            else if (action == "updates")
                await CheckOfficialEngineUpdatesAsync(true, selected).ConfigureAwait(true);
            else if (action == "size")
                await AnalyzeEngineSizesAsync(selected).ConfigureAwait(true);
            else if (action == "check")
            {
                StatusMessage = $"Vérification de {selected.Count} moteur(s)…";
                var candidates = _currentEngines.Where(engine => selected.Contains(engine.RootPath)).ToArray();
                var checkedEngines = await EngineCatalogValidation.ValidateAsync(candidates).ConfigureAwait(true);
                foreach (var engine in checkedEngines) await _engineCatalog.UpsertAsync(engine).ConfigureAwait(true);
                // Revalidate the full cached snapshot too, so unrelated stale rows cannot reappear.
                await LoadCachedCatalogAsync().ConfigureAwait(true);
                StatusMessage = $"Vérification terminée : {checkedEngines.Count(engine => engine.Health != CyEngineHub.Unreal.Models.EngineHealth.Broken)} disponible(s), {candidates.Length - checkedEngines.Count} absent(s), {checkedEngines.Count(engine => engine.Health == CyEngineHub.Unreal.Models.EngineHealth.Broken)} à vérifier.";
                if (selected.Any(path => _currentLocalEngineInstallations.Any(engine => SameWorkspaceKey(engine.RootPath, path))))
                    await RefreshCatalogAsync().ConfigureAwait(true);
            }
            else if (action == "remove")
            {
                if (EpicEngineDownloads.Any(item => item.CanCancel && selected.Contains(item.Path)))
                    throw new InvalidOperationException("Annulez le téléchargement avant de retirer le moteur du catalogue.");
                foreach (var path in selected) await SetWorkspaceEngineHiddenAsync(path, true).ConfigureAwait(true);
                RebuildCatalogCardsFromCurrentSnapshots();
                StatusMessage = $"{selected.Count} moteur(s) retiré(s) du catalogue. Les fichiers sont conservés.";
            }
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetProperty(ref _engineSelectionBusy, false, nameof(IsEngineSelectionBusy)); }
    }
}

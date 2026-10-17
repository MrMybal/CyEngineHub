using System.Collections.ObjectModel;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Core.Settings;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Unreal.Detection;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<EngineDownloadCardViewModel> EpicEngineDownloads { get; } = [];
    public IEnumerable<EngineDownloadCardViewModel> VisibleEpicEngineDownloads => IsUnrealEngineTabSelected
        ? EpicEngineDownloads.Where(item => (item.CanCancel || FindWorkspacePreference(
            WorkspaceItemKind.Engine, item.Path)?.IsHidden != true) &&
            !Engines.Any(engine => SameWorkspaceKey(engine.Path, item.Path) && item.IsCompleted))
        : [];

    private async Task CompleteEpicEngineDownloadAsync(HubTaskHandle handle, EngineDownloadCardViewModel download,
        Func<EpicBinaryEngineDownloadResult?> getResult)
    {
        try
        {
            var completed = await handle.Completion.ConfigureAwait(true);
            download.ApplySnapshot(completed);
            IsEpicEngineDownloadActive = EpicEngineDownloads.Any(item => item.CanCancel);
            var result = getResult();
            if (completed.Status == HubTaskStatus.Canceled)
            {
                EngineBinaryDownloadSummary = "Téléchargement annulé · le cache de chunks est conservé pour une prochaine tentative.";
                return;
            }
            if (completed.Status == HubTaskStatus.Failed || result is null)
            {
                EngineBinaryDownloadSummary = $"Échec du téléchargement Unreal : {completed.Error}";
                return;
            }
            await AddUniquePathAsync(EngineRootsSetting, result.DestinationDirectory).ConfigureAwait(true);
            await SetWorkspaceEngineHiddenAsync(result.DestinationDirectory, false).ConfigureAwait(true);
            var inspected = await Task.Run(() => new EngineInspector().InspectAsync(new EngineCandidate(
                result.DestinationDirectory, "Epic download", download.Version, download.Name))).ConfigureAwait(true)
                ?? throw new InvalidOperationException("Le téléchargement est terminé, mais le dossier Unreal n’a pas pu être validé.");
            await _engineCatalog.UpsertAsync(inspected with { Type = EngineInstallationType.EpicBinary }).ConfigureAwait(true);
            await LoadCachedCatalogAsync().ConfigureAwait(true);
            // Never navigate on completion: the user may be working on another page.
            if (Engines.Any(engine => SameWorkspaceKey(engine.Path, result.DestinationDirectory))) EpicEngineDownloads.Remove(download);
            EngineBinaryDownloadSummary = $"{download.Name} installé · build {result.BuildVersion} · {result.FileCount:N0} fichier(s).";
            StatusMessage = EngineBinaryDownloadSummary;
        }
        catch (Exception exception)
        {
            download.SetRegistrationFailure(exception.Message);
            ShowError(exception);
        }
    }
}

using System.Windows.Input;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class TaskLinkViewModel(HubTaskLink link, Action<HubTaskLink> navigate)
{
    public string Kind { get; } = link.Kind;
    public string Id { get; } = link.Id;
    public string Label { get; } = link.Label;
    public ICommand OpenCommand { get; } = new RelayCommand(() => navigate(link));
}

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string, RemoteFabAsset> _taskProductSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private int _taskRefreshPending;

    private HubTaskLink[] ProductTaskLinks(RemoteFabAsset asset,
        IEnumerable<HubTaskLink>? targets = null)
    {
        var source = NormalizeAssetSource(asset);
        _taskProductSnapshots[source + ":" + asset.AssetId] = asset;
        return new[] { new HubTaskLink("product", asset.AssetId, "Voir le produit", source) }
            .Concat(targets ?? []).Distinct().ToArray();
    }

    private static HubTaskLink ProjectTaskLink(string path, string name) => new("project", path, "Projet · " + name);
    private static HubTaskLink EngineTaskLink(string path, string name) => new("engine", path, "Moteur · " + name);

    private void NavigateTaskLink(HubTaskLink link)
    {
        try
        {
            if (link.Kind == "product")
            {
                var asset = _remoteFabCatalog.FirstOrDefault(item =>
                    item.AssetId.Equals(link.Id, StringComparison.OrdinalIgnoreCase) &&
                    NormalizeAssetSource(item).Equals(link.Source, StringComparison.OrdinalIgnoreCase));
                asset ??= _taskProductSnapshots.GetValueOrDefault(link.Source + ":" + link.Id);
                if (asset is null) { StatusMessage = "La fiche produit n’est plus disponible dans le cache."; return; }
                LibrarySource = IsQuixelAsset(asset) ? "Quixel" : "Fab";
                ShowSection(PageSection.Epic);
                using var card = CreateRemoteAssetCard(asset);
                OpenRemoteFabProduct(card);
            }
            else if (link.Kind == "project")
            {
                var project = Projects.FirstOrDefault(item => SameWorkspaceKey(item.Project.FilePath, link.Id) || SameWorkspaceKey(item.Path, link.Id));
                if (project is not null) { OpenUnrealProjectDetails(project); return; }
                var local = LocalEngineProjects.FirstOrDefault(item => SameWorkspaceKey(item.Path, link.Id) || SameWorkspaceKey(item.Project.MarkerPath, link.Id));
                if (local is not null) { OpenLocalProjectDetails(local); return; }
                ShowSection(PageSection.Projects);
                StatusMessage = "Le projet n’est pas encore présent dans le catalogue : " + link.Id;
            }
            else if (link.Kind == "engine")
            {
                var engine = Engines.FirstOrDefault(item => SameWorkspaceKey(item.Path, link.Id));
                if (engine is not null) { OpenUnrealEngineDetails(engine); return; }
                var local = LocalEngineInstallations.FirstOrDefault(item => SameWorkspaceKey(item.Installation.RootPath, link.Id));
                if (local is not null) { OpenLocalEngineDetails(local); return; }
                var pending = EpicEngineDownloads.FirstOrDefault(item => SameWorkspaceKey(item.Path, link.Id));
                if (pending is not null)
                {
                    SelectedEngineTab = EngineTabs.FirstOrDefault(item => item.Family == CyEngineHub.Engines.EngineFamily.Unreal);
                    ShowSection(PageSection.Engines); StatusMessage = pending.StatusLabel; return;
                }
                ShowSection(PageSection.Engines);
                StatusMessage = "Installation du moteur en cours ou moteur absent du catalogue : " + link.Id;
            }
            else if (link.Kind == "library")
            {
                ShowSection(PageSection.Epic);
                SelectLibrarySource(link.Id);
            }
        }
        catch (Exception exception) { ShowError(exception); }
    }
}

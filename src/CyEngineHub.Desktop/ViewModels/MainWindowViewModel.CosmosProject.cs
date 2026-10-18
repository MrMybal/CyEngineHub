using System.Windows.Input;
using CyEngineHub.Core.Settings;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _cosmosProjectDestination = "Cosmos";
    public string CosmosProjectDestination { get => _cosmosProjectDestination; set => SetProperty(ref _cosmosProjectDestination, value); }
    public bool IsCosmosProjectSettingsVisible => SelectedProjectDetails is not null &&
        MarketplaceConnectors.Any(item => item.Id == "cyenginehub.marketplace-cosmos" && item.IsEnabled);
    private ICommand? _saveCosmosProjectSettingsCommand;
    public ICommand SaveCosmosProjectSettingsCommand => _saveCosmosProjectSettingsCommand ??= new AsyncRelayCommand(async () =>
    {
        var project = SelectedProjectDetails ?? throw new InvalidOperationException("Sélectionnez un projet Unreal.");
        await new CosmosProjectSettingsStore(_settings).SaveAsync(project.Project.RootPath, CosmosProjectDestination);
        StatusMessage = "Dossier Cosmos du projet enregistré. Les imports Cosmos utilisent le déplacement Unreal avec validation des références.";
    }, onError: ShowError);
    private async Task LoadCosmosProjectSettingsAsync(ProjectCardViewModel project)
    {
        try
        {
            var folder = await new CosmosProjectSettingsStore(_settings).LoadAsync(project.Project.RootPath);
            if (ReferenceEquals(SelectedProjectDetails, project)) CosmosProjectDestination = folder;
            OnPropertyChanged(nameof(IsCosmosProjectSettingsVisible));
        }
        catch (Exception ex) { ShowError(ex); }
    }
}

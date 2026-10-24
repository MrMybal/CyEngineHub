using System.Windows.Input;
using CyEngineHub.Core.Settings;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _cosmosProjectDestination = "Cosmos";
    public string CosmosProjectDestination { get => _cosmosProjectDestination; set => SetProperty(ref _cosmosProjectDestination, value); }
    private ProjectCardViewModel? _selectedCosmosImportProject;
    private bool _cosmosProjectSettingsReady;
    public ProjectCardViewModel? SelectedCosmosImportProject
    {
        get => _selectedCosmosImportProject;
        set
        {
            if (!SetProperty(ref _selectedCosmosImportProject, value)) return;
            _cosmosProjectSettingsReady = false;
            if (value is not null) _ = LoadCosmosProjectSettingsAsync(value);
            (_saveCosmosProjectSettingsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        }
    }
    private ICommand? _saveCosmosProjectSettingsCommand;
    public ICommand SaveCosmosProjectSettingsCommand => _saveCosmosProjectSettingsCommand ??= new AsyncRelayCommand(async () =>
    {
        var project = SelectedCosmosImportProject ?? throw new InvalidOperationException("Sélectionnez un projet Unreal dans les paramètres Cosmos.");
        await new CosmosProjectSettingsStore(_settings).SaveAsync(project.Project.RootPath, CosmosProjectDestination);
        StatusMessage = "Dossier Cosmos du projet enregistré. Les imports Cosmos utilisent le déplacement Unreal avec validation des références.";
    }, () => SelectedCosmosImportProject is not null && _cosmosProjectSettingsReady, onError: ShowError);
    private async Task LoadCosmosProjectSettingsAsync(ProjectCardViewModel project)
    {
        try
        {
            var folder = await new CosmosProjectSettingsStore(_settings).LoadAsync(project.Project.RootPath);
            if (ReferenceEquals(SelectedCosmosImportProject, project))
            {
                CosmosProjectDestination = folder;
                _cosmosProjectSettingsReady = true;
                (_saveCosmosProjectSettingsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }
}

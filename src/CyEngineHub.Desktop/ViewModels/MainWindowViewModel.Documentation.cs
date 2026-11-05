namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public event EventHandler<DocumentationRequest>? DocumentationRequested;
    public RelayCommand ShowDocumentationCommand => new(() => OpenDocumentation("welcome"));
    public RelayCommand ShowContextualDocumentationCommand => new(() =>
    {
        if (IsProjectCreatorVisible) OpenDocumentation("projects", "required-choices", "project-creation");
        else if (IsSettingsVisible && SelectedSettingsCategory?.Id == "appearance") OpenDocumentation("settings", "appearance-and-language");
        else if (IsSettingsVisible && SelectedSettingsCategory?.Id == "connections") OpenDocumentation("settings", "connections");
        else OpenDocumentation(ContextualDocumentationPage);
    });
    public void OpenDocumentation(string page, string? anchor = null, string? media = null) =>
        DocumentationRequested?.Invoke(this, new(page, anchor, media));
    public void ReportDocumentationError() => StatusMessage = "The bundled documentation is missing or this help link is invalid. Reinstall CyEngineHub. / Documentation indisponible.";
    public string ContextualDocumentationPage => IsFabInstallOptionsVisible || IsFabInstallationScriptConfigurationVisible ? "install" :
        IsFabDownloadOptionsVisible ? "downloads" : IsProjectCreatorVisible ? "projects" :
        IsBuildsVisible ? "builds" : IsPluginsVisible ? "plugins" : IsGitVisible ? "git" :
        IsEngineDetailsVisible || IsEnginesVisible ? "engines" : IsProjectDetailsVisible || IsProjectsVisible ? "projects" :
        IsHubExtensionsVisible ? "extensions-sdk" : IsStorageVisible ? "storage" : IsToolchainsVisible ? "builds" :
        IsLibraryVisible ? IsLibraryQuixelSelected || IsLibraryFabWebSelected ? "megascans" : IsLibraryFabSelected ? "fab" :
        IsConnectorLibrarySelected || IsLibraryUnitySelected ? "connectors" : "library" : IsTasksVisible ? "downloads" : IsActionsVisible ? "navigation" :
        IsSettingsVisible ? SelectedSettingsCategory?.Id switch
        {
            "ai" => "ai-mcp", "project-profiles" => "profiles", "downloads" => "downloads",
            "marketplaces" => "connectors", "engines" => "engines", _ => "settings"
        } : "troubleshooting";
    private string _documentationLanguage = "en";
    private Task _documentationLanguageWrite = Task.CompletedTask;
    public string DocumentationLanguage
    {
        get => _documentationLanguage;
        set
        {
            if (value is not ("en" or "fr") || !SetProperty(ref _documentationLanguage, value)) return;
            _documentationLanguageWrite = SaveDocumentationLanguageAsync(_documentationLanguageWrite, value);
        }
    }
    private async Task SaveDocumentationLanguageAsync(Task previous, string value)
    {
        try { await previous.ConfigureAwait(true); } catch (Exception) { }
        try { await _settings.SetAsync("documentation.language.v1", value).ConfigureAwait(true); }
        catch (Exception) { StatusMessage = value == "fr" ? "La langue de l’aide n’a pas pu être enregistrée." : "The help language could not be saved."; }
    }
    private async Task LoadDocumentationLanguageAsync()
    {
        _documentationLanguage = Documentation.DocumentationLocation.Language(await _settings.GetAsync("documentation.language.v1").ConfigureAwait(true));
        OnPropertyChanged(nameof(DocumentationLanguage));
    }
}

public sealed record DocumentationRequest(string Page, string? Anchor = null, string? Media = null);

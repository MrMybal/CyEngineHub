using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class ProjectCreationEngineOptionViewModel(UnrealEngineInstallation engine, string? alias = null) : ObservableObject
{
    private string? _alias = alias;
    public UnrealEngineInstallation Engine { get; } = engine;
    public string Name => string.IsNullOrWhiteSpace(_alias) ? Engine.DisplayName : _alias.Trim();
    public string Label => Engine.Version.IsKnown ? $"{Name} · {Engine.Version.Major}.{Engine.Version.Minor}.{Engine.Version.Patch}" : Name;
    public string ToolTip => $"{Name}\n{Version}\n{Path}\nAssociation : {Association}";
    public string Version => Engine.Version.IsKnown
        ? $"Unreal Engine {Engine.Version.Major}.{Engine.Version.Minor}.{Engine.Version.Patch}"
        : Engine.AssociationId;
    public string Association => Engine.AssociationId;
    public string Path => Engine.RootPath;

    public void UpdateAlias(string? value)
    {
        if (!SetProperty(ref _alias, value, nameof(Name))) return;
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(ToolTip));
    }
}

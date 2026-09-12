using System.Windows.Input;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class DerivedContentKindOptionViewModel(
    LibraryPackageContentKind value,
    string label)
{
    public LibraryPackageContentKind Value { get; } = value;
    public string Label { get; } = label;
}

public sealed class FabDerivedWorkspaceViewModel
{
    public FabDerivedWorkspaceViewModel(
        Guid id,
        string name,
        string assetTitle,
        string engineVersion,
        string projectPath,
        LibraryPackageContentKind contentKind,
        string status,
        bool canPackage,
        string workspaceKindLabel,
        Func<FabDerivedWorkspaceViewModel, Task> open,
        Func<FabDerivedWorkspaceViewModel, Task> package,
        Action<FabDerivedWorkspaceViewModel> requestDelete,
        Action<Exception> onError)
    {
        Id = id;
        Name = name;
        AssetTitle = assetTitle;
        EngineVersion = engineVersion;
        ProjectPath = projectPath;
        ContentKind = contentKind;
        Status = status;
        CanPackage = canPackage;
        WorkspaceKindLabel = workspaceKindLabel;
        OpenCommand = new AsyncRelayCommand(() => open(this), onError: onError);
        PackageCommand = new AsyncRelayCommand(() => package(this), onError: onError);
        RequestDeleteCommand = new RelayCommand(() => requestDelete(this));
    }

    public Guid Id { get; }
    public string Name { get; }
    public string AssetTitle { get; }
    public string EngineVersion { get; }
    public string ProjectPath { get; }
    public LibraryPackageContentKind ContentKind { get; }
    public string ContentKindLabel => ContentKind switch
    {
        LibraryPackageContentKind.ContentPack => "AssetPack",
        LibraryPackageContentKind.CompleteProject => "Projet complet",
        LibraryPackageContentKind.CodePlugin => "Plugin code",
        LibraryPackageContentKind.EnginePackage => "Package moteur",
        LibraryPackageContentKind.RawAsset => "RAW",
        _ => "Automatique",
    };
    public string Status { get; }
    public bool CanPackage { get; }
    public string WorkspaceKindLabel { get; }
    public ICommand OpenCommand { get; }
    public ICommand PackageCommand { get; }
    public ICommand RequestDeleteCommand { get; }
}

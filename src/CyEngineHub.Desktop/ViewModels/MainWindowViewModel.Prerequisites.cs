using System.Windows.Input;
using CyEngineHub.Toolchains.Compatibility;
using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string? _prerequisiteEngineId;
    public ToolchainCompatibilityCheck? PrerequisiteCheck { get; private set; }
    public ToolchainRemediation? PrerequisiteRemediation { get; private set; }
    public string PrerequisiteEngineName { get; private set; } = "";
    private ICommand? _openPrerequisiteDownloadCommand, _repairPrerequisiteEngineCommand, _closePrerequisiteCommand;
    public ICommand OpenPrerequisiteDownloadCommand => _openPrerequisiteDownloadCommand ??= new AsyncRelayCommand(async () =>
    {
        if (PrerequisiteRemediation?.DownloadPage is { } uri)
            await OpenOfficialUriAsync(uri, "Prérequis · " + PrerequisiteCheck?.Label).ConfigureAwait(true);
    }, () => PrerequisiteRemediation?.DownloadPage is not null, ShowError);
    public ICommand RepairPrerequisiteEngineCommand => _repairPrerequisiteEngineCommand ??= new RelayCommand(() =>
    {
        if (Engines.FirstOrDefault(item => item.Engine.Id == _prerequisiteEngineId) is not { } engine) return;
        OpenUnrealEngineDetails(engine);
        EngineDetailsTab = 1;
    }, () => Engines.Any(item => item.Engine.Id == _prerequisiteEngineId));
    public ICommand ClosePrerequisiteCommand => _closePrerequisiteCommand ??= new RelayCommand(() =>
    {
        PrerequisiteCheck = null;
        PrerequisiteRemediation = null;
    });
    private void ShowPrerequisiteRemediation(string engineId, ToolchainCompatibilityCheck check)
    {
        if (check.Health == ToolchainHealth.Ready) return;
        var engine = Engines.FirstOrDefault(item => item.Engine.Id == engineId);
        if (engine is null) { StatusMessage = "Ce moteur n’est plus dans le catalogue. Actualisez l’analyse."; return; }
        _prerequisiteEngineId = engineId;
        PrerequisiteCheck = check;
        PrerequisiteEngineName = engine.DisplayName + " · " + engine.VersionLabel;
        PrerequisiteRemediation = ToolchainRemediation.For(check, engine.Engine.Version.ToString());
        OpenTools();
        SelectedToolsEngine = engine;
        ToolsTabIndex = 1;
    }

    private void RefreshPrerequisiteRemediation(ToolchainSnapshot snapshot)
    {
        if (PrerequisiteCheck is not { } previous) return;
        var engine = snapshot.Engines.FirstOrDefault(item => item.EngineId == _prerequisiteEngineId);
        var check = engine?.Checks.FirstOrDefault(item => item.Kind == previous.Kind);
        if (check is null || check.Health == ToolchainHealth.Ready)
        {
            PrerequisiteCheck = null;
            PrerequisiteRemediation = null;
            return;
        }
        PrerequisiteCheck = check;
        PrerequisiteRemediation = ToolchainRemediation.For(check, engine!.EngineVersion);
    }
}

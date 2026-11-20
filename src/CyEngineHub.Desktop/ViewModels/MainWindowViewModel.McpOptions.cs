using CyEngineHub.Core.Ai;
using System.Text.Json;
using System.Windows.Input;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly string[] McpProfileNames = ["Free", "Local contrôlé", "Jeton seul", "Protégé"];
    private HubMcpOptions _mcpOptions = new();
    private string _mcpOptionsStatus = "Les changements prennent effet après Enregistrer.";
    public string McpOptionsStatus { get => _mcpOptionsStatus; private set => SetProperty(ref _mcpOptionsStatus, value); }
    private ICommand? _saveMcpOptionsCommand;
    public ICommand SaveMcpOptionsCommand => _saveMcpOptionsCommand ??= new AsyncRelayCommand(
        () => ChangeMcpAsync(SaveMcpOptionsAsync), onError: exception => McpOptionsStatus = "Options MCP · " + exception.Message);
    public IReadOnlyList<string> McpAccessProfiles { get; } = Array.AsReadOnly(McpProfileNames);
    private HubMcpAccessProfile _mcpDraftAccessProfile;
    public string McpSelectedAccessProfile
    {
        get => McpProfileName(_mcpDraftAccessProfile);
        set
        {
            var index = Array.IndexOf(McpProfileNames, value);
            if (index >= 0 && SetProperty(ref _mcpDraftAccessProfile, (HubMcpAccessProfile)index))
                OnPropertyChanged(nameof(McpEditsControlledOperations));
        }
    }
    public bool McpEditsControlledOperations =>
        _mcpDraftAccessProfile is HubMcpAccessProfile.LocalControlled or HubMcpAccessProfile.Protected;
    private int _mcpLocalPort = HubMcpOptions.DefaultLocalPort;
    public int McpLocalPort { get => _mcpLocalPort; set => SetProperty(ref _mcpLocalPort, value); }
    private static string McpProfileName(HubMcpAccessProfile profile) => McpProfileNames[(int)profile];
    private bool _McpAutoCategorize;
    public bool McpAutoCategorize { get => _McpAutoCategorize; set => SetProperty(ref _McpAutoCategorize, value); }
    private bool _McpAutoDownload;
    public bool McpAutoDownload { get => _McpAutoDownload; set => SetProperty(ref _McpAutoDownload, value); }
    private bool _McpAutoInstall;
    public bool McpAutoInstall { get => _McpAutoInstall; set => SetProperty(ref _McpAutoInstall, value); }
    private int _McpDefaultPageSize = 20;
    public int McpDefaultPageSize { get => _McpDefaultPageSize; set => SetProperty(ref _McpDefaultPageSize, value); }
    private int _McpMaximumPageSize = 100;
    public int McpMaximumPageSize { get => _McpMaximumPageSize; set => SetProperty(ref _McpMaximumPageSize, value); }
    private int _McpDescriptionLength = 600;
    public int McpDescriptionLength { get => _McpDescriptionLength; set => SetProperty(ref _McpDescriptionLength, value); }
    private int _McpScanLimit = 50_000;
    public int McpScanLimit { get => _McpScanLimit; set => SetProperty(ref _McpScanLimit, value); }
    private int _McpRegisteredPathsLimit = 100;
    public int McpRegisteredPathsLimit { get => _McpRegisteredPathsLimit; set => SetProperty(ref _McpRegisteredPathsLimit, value); }
    private int _McpTaskHistoryLimit = 100;
    public int McpTaskHistoryLimit { get => _McpTaskHistoryLimit; set => SetProperty(ref _McpTaskHistoryLimit, value); }
    private int _McpPendingProposalLimit;
    public int McpPendingProposalLimit { get => _McpPendingProposalLimit; set => SetProperty(ref _McpPendingProposalLimit, value); }
    private int _McpProposalHistoryLimit = 100;
    public int McpProposalHistoryLimit { get => _McpProposalHistoryLimit; set => SetProperty(ref _McpProposalHistoryLimit, value); }
    private async Task SaveMcpOptionsAsync()
    {
        var options = new HubMcpOptions
        {
            AccessProfile = _mcpDraftAccessProfile,
            LocalPort = McpLocalPort,
            AutoCategorize = McpAutoCategorize,
            AutoDownload = McpAutoDownload,
            AutoInstall = McpAutoInstall,
            DefaultPageSize = McpDefaultPageSize,
            MaximumPageSize = McpMaximumPageSize,
            DescriptionLength = McpDescriptionLength,
            ScanLimit = McpScanLimit,
            RegisteredPathsLimit = McpRegisteredPathsLimit,
            TaskHistoryLimit = McpTaskHistoryLimit,
            PendingProposalLimit = McpPendingProposalLimit,
            ProposalHistoryLimit = McpProposalHistoryLimit,
        }.Validate();
        await _settings.SetAsync("ai.mcp.options.v1", JsonSerializer.Serialize(options)).ConfigureAwait(true);
        var restart = McpEnabled && (options.AccessProfile != _mcpOptions.AccessProfile || options.LocalPort != _mcpOptions.LocalPort);
        if (restart)
        {
            StopMcp();
            if (_mcpServer is { } server) await server.ConfigureAwait(true);
        }
        ApplyMcpOptions(options);
        if (restart) StartMcp();
        McpOptionsStatus = restart ? "Enregistré · connexion MCP redémarrée." : "Enregistré · options appliquées aux prochaines requêtes.";
    }
    private void ApplyMcpOptions(HubMcpOptions options)
    {
        _mcpOptions = options.Validate();
        McpSelectedAccessProfile = McpProfileName(options.AccessProfile);
        McpLocalPort = options.LocalPort;
        McpAutoCategorize = options.AutoCategorize;
        McpAutoDownload = options.AutoDownload;
        McpAutoInstall = options.AutoInstall;
        McpDefaultPageSize = options.DefaultPageSize;
        McpMaximumPageSize = options.MaximumPageSize;
        McpDescriptionLength = options.DescriptionLength;
        McpScanLimit = options.ScanLimit;
        McpRegisteredPathsLimit = options.RegisteredPathsLimit;
        McpTaskHistoryLimit = options.TaskHistoryLimit;
        McpPendingProposalLimit = options.PendingProposalLimit;
        McpProposalHistoryLimit = options.ProposalHistoryLimit;
    }
}

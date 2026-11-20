using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyEngineHub.Core.Ai;

[JsonConverter(typeof(HubMcpAccessProfileConverter))]
public enum HubMcpAccessProfile
{
    Free = 0,
    LocalControlled = 1,
    TokenOnly = 2,
    Protected = 3,
}

internal sealed class HubMcpAccessProfileConverter() :
    JsonStringEnumConverter<HubMcpAccessProfile>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

public sealed record HubMcpOptions
{
    public const int DefaultLocalPort = 47842;
    public HubMcpAccessProfile AccessProfile { get; init; } = HubMcpAccessProfile.Free;
    public int LocalPort { get; init; } = DefaultLocalPort;
    public bool RequiresToken => AccessProfile is HubMcpAccessProfile.TokenOnly or HubMcpAccessProfile.Protected;
    public bool ControlsOperations => AccessProfile is HubMcpAccessProfile.LocalControlled or HubMcpAccessProfile.Protected;
    public bool AutoCategorize { get; init; }
    public bool AutoDownload { get; init; }
    public bool AutoInstall { get; init; }
    public int DefaultPageSize { get; init; } = 20;
    public int MaximumPageSize { get; init; } = 100;
    public int DescriptionLength { get; init; } = 600;
    public int ScanLimit { get; init; } = 50_000;
    public int RegisteredPathsLimit { get; init; } = 100;
    public int TaskHistoryLimit { get; init; } = 100;
    public int PendingProposalLimit { get; init; }
    public int ProposalHistoryLimit { get; init; } = 100;
    public static int Bound(int value) => value == 0 ? int.MaxValue : value;
    public bool IsAutomatic(string action) => action switch
    {
        "categorize" => !ControlsOperations || AutoCategorize,
        "download" => !ControlsOperations || AutoDownload,
        "install" => !ControlsOperations || AutoInstall,
        _ => false,
    };
    public HubMcpOptions Validate()
    {
        if (!Enum.IsDefined(AccessProfile))
            throw new ArgumentException("Profil MCP inconnu.");
        if (LocalPort is < 0 or > 65535)
            throw new ArgumentException("Le port MCP doit être compris entre 0 et 65 535.");
        if (DefaultPageSize < 0 || MaximumPageSize < 0 || DescriptionLength < 0 || ScanLimit < 0
            || RegisteredPathsLimit < 0 || TaskHistoryLimit < 0 || PendingProposalLimit < 0 || ProposalHistoryLimit < 0)
            throw new ArgumentException("Les limites MCP doivent être positives ou nulles (0 = illimité).");
        if (Bound(DefaultPageSize) > Bound(MaximumPageSize))
            throw new ArgumentException("La taille par défaut dépasse le maximum.");
        return this;
    }
}

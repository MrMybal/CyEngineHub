namespace CyEngineHub.Core.Ai;

public sealed record HubMcpOptions
{
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
        "categorize" => AutoCategorize, "download" => AutoDownload, "install" => AutoInstall, _ => false
    };
    public HubMcpOptions Validate()
    {
        if (DefaultPageSize < 0 || MaximumPageSize < 0 || DescriptionLength < 0 || ScanLimit < 0
            || RegisteredPathsLimit < 0 || TaskHistoryLimit < 0 || PendingProposalLimit < 0 || ProposalHistoryLimit < 0)
            throw new ArgumentException("Les limites MCP doivent être positives ou nulles (0 = illimité).");
        if (Bound(DefaultPageSize) > Bound(MaximumPageSize))
            throw new ArgumentException("La taille par défaut dépasse le maximum.");
        return this;
    }
}

namespace CyEngineHub.Desktop.ViewModels;

// Stable storage keys are kept separate from their user-facing labels.
public sealed record MarketplaceCompressionChoice(string StorageMode, string Label, string Description)
{
    public static IReadOnlyList<MarketplaceCompressionChoice> ZipOptions { get; } =
    [
        new("ZipStore", "Stockage", "ZIP sans recompression"),
        new("ZipFastest", "Rapide", "Priorité à la vitesse"),
        new("ZipBalanced", "Moyen", "Équilibre vitesse et taille"),
        new("ZipHigh", "Élevé", "Réduction de taille renforcée"),
        new("ZipUltra", "Ultra", "Taille minimale, plus lent"),
    ];

    public static IReadOnlyList<MarketplaceCompressionChoice> AllOptions { get; } =
    [new("Uncompressed", "Non compressé", "Fichiers en dossier, sans archive ZIP"), .. ZipOptions];
}

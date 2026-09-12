namespace CyEngineHub.Desktop.Views;

internal static class MarketplaceDownloadStorage
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    };
    public static string ValidateDirectory(string value)
    {
        var path = value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Choisissez un dossier de téléchargement absolu dans Paramètres → Marketplaces.");
        var result = Path.GetFullPath(path);
        if (File.Exists(result)) throw new IOException("Le dossier de téléchargement désigne un fichier.");
        return result;
    }

    public static string PrepareDestination(string directory, string suggestedPath)
    {
        var root = ValidateDirectory(directory);
        // Browser-supplied names are untrusted. Never reuse their directory or permit ADS/device names.
        var name = suggestedPath.Replace('\\', '/').Split('/')[^1];
        name = new string(name.Select(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character) ? '_' : character).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 180) name = name[..160] + Path.GetExtension(name)[..Math.Min(20, Path.GetExtension(name).Length)];
        if (string.IsNullOrWhiteSpace(name)) name = "download.bin";
        var stem = name.Split('.')[0].TrimEnd(' ');
        if (ReservedNames.Contains(stem))
            name = "_" + name;
        var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, name);
        // Fail immediately if the selected disk cannot be written, without falling back to Windows Downloads.
        using var reservation = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return destination;
    }
}

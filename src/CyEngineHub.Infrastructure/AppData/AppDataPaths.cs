namespace CyEngineHub.Infrastructure.AppData;

public sealed record AppDataPaths(
    string RootDirectory,
    string DatabasePath,
    string LogsDirectory,
    bool IsPortable)
{
    public static AppDataPaths Resolve(
        string? executableDirectory = null,
        string? localApplicationDataDirectory = null)
    {
        var applicationDirectory = executableDirectory ?? AppContext.BaseDirectory;
        var isPortable = File.Exists(Path.Combine(applicationDirectory, "portable.flag"));
        var localApplicationData = localApplicationDataDirectory ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var preferredRoot = isPortable
            ? Path.Combine(applicationDirectory, "Data")
            : Path.Combine(localApplicationData, "CyEngineHub");
        var root = isPortable
            ? preferredRoot
            : ResolveProfileRoot(
                preferredRoot,
                Path.Combine(localApplicationData, "CyUEHub"));
        var logs = Path.Combine(root, "Logs");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(logs);
        return new AppDataPaths(
            root,
            ResolveDatabasePath(root),
            logs,
            isPortable);
    }

    private static string ResolveProfileRoot(string preferredRoot, string legacyRoot)
    {
        if (Directory.Exists(preferredRoot) ||
            !Directory.Exists(legacyRoot) ||
            IsReparsePoint(legacyRoot))
        {
            return preferredRoot;
        }

        try
        {
            Directory.Move(legacyRoot, preferredRoot);
            return preferredRoot;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Garder l'ancien profil utilisable si Windows bloque le déplacement.
            return legacyRoot;
        }
    }

    private static string ResolveDatabasePath(string root)
    {
        var databasePath = Path.Combine(root, "cyenginehub.db");
        var legacyDatabasePath = Path.Combine(root, "cyuehub.db");
        if (File.Exists(databasePath) || !File.Exists(legacyDatabasePath))
        {
            return databasePath;
        }

        try
        {
            File.Move(legacyDatabasePath, databasePath);
            return databasePath;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Ne jamais démarrer sur une base vide si l'ancienne reste lisible.
            return legacyDatabasePath;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}

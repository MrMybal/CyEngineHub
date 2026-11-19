namespace CyEngineHub.Fab.Packaging;

internal sealed record PackageFileTree(List<FileInfo> Files, List<string> Directories)
{
    internal static PackageFileTree Read(string root, CancellationToken cancellationToken)
    {
        var files = new List<FileInfo>();
        var directories = new List<string> { root };
        for (var index = 0; index < directories.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories[index];
            RequirePlainPath(root, directory);
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Le contenu du package contient un lien ou point de jonction non autorisé.");
                if (entry is DirectoryInfo) directories.Add(entry.FullName);
                else files.Add((FileInfo)entry);
            }
        }
        return new(files, directories);
    }

    // Call with absolute paths from the inventory. Parents may have changed during asynchronous I/O.
    internal static void RequirePlainPath(string root, string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Le dossier de package contient un lien ou une jonction.");
            if (string.Equals(current, root, StringComparison.Ordinal)) return;
        }
        throw new InvalidDataException("Le chemin sort du dossier de package.");
    }

    internal void RemoveCopiedContent(string root)
    {
        foreach (var file in Files)
        {
            RequirePlainPath(root, file.FullName);
            var current = new FileInfo(file.FullName);
            if (current.Length != file.Length || current.LastWriteTimeUtc != file.LastWriteTimeUtc)
                throw new IOException($"Le fichier {file.FullName} a changé depuis la copie.");
            current.Delete();
        }
        // Parents precede children in the inventory. Leave newly added files and directories intact.
        for (var index = Directories.Count - 1; index > 0; index--)
        {
            var directory = Directories[index];
            RequirePlainPath(root, directory);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }
}

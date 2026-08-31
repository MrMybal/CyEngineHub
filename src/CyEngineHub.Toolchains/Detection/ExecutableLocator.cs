namespace CyEngineHub.Toolchains.Detection;

internal static class ExecutableLocator
{
    public static string? Find(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (Path.IsPathRooted(fileName))
        {
            return File.Exists(fileName) ? Path.GetFullPath(fileName) : null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];
        if (Path.HasExtension(fileName))
        {
            extensions = [string.Empty];
        }

        foreach (var rawDirectory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = rawDirectory.Trim('"');
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(directory, fileName + extension.ToLowerInvariant());
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }

                    candidate = Path.Combine(directory, fileName + extension.ToUpperInvariant());
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch (Exception exception) when (
                    exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                }
            }
        }

        return null;
    }
}

using System.Text;
using CyEngineHub.Build.Models;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Build.Commands;

public static class UnrealBuildCommandFactory
{
    public static ProcessRequest CreateSetup(SetupEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var script = Path.Combine(request.Engine.RootPath, "Setup.bat");
        EnsureFileExists(script, "Setup", request.Engine.RootPath);

        var arguments = new List<string>();
        AppendAdditionalArguments(arguments, request.AdditionalArguments);
        return CreateBatchRequest(
            script,
            arguments,
            request.Engine.RootPath,
            maxCapturedLines: 30_000);
    }

    public static ProcessRequest CreateGenerateProjectFiles(GenerateProjectFilesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var engine = request.Engine;
        var rootScript = Path.Combine(engine.RootPath, "GenerateProjectFiles.bat");
        var script = File.Exists(rootScript)
            ? rootScript
            : Path.Combine(
                engine.RootPath,
                "Engine",
                "Build",
                "BatchFiles",
                "GenerateProjectFiles.bat");
        var arguments = new List<string>();
        if (!File.Exists(script))
        {
            // Epic installed builds often omit both GenerateProjectFiles.bat wrappers.
            // Build.bat forwards its arguments to the bundled UnrealBuildTool.
            script = Path.Combine(engine.RootPath, "Engine", "Build", "BatchFiles", "Build.bat");
            arguments.Add("-ProjectFiles");
        }
        EnsureFileExists(script, "Generate project files (UnrealBuildTool)", engine.RootPath);
        if (request.Project is not null)
        {
            arguments.Add($"-Project={request.Project.FilePath}");
            arguments.Add("-Game");
            if (!request.IncludeEngineSource)
            {
                arguments.Add("-NoEngine");
            }
        }

        arguments.Add("-Progress");
        arguments.Add("-WaitMutex");

        switch (request.Format)
        {
            case UnrealProjectFileFormat.VisualStudio2022:
                arguments.Add("-2022");
                break;
            case UnrealProjectFileFormat.Rider:
                arguments.Add("-Rider");
                break;
            case UnrealProjectFileFormat.Default:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Format, "Unknown IDE format.");
        }

        return CreateBatchRequest(script, arguments, engine.RootPath, maxCapturedLines: 10_000);
    }

    public static ProcessRequest CreateTargetBuild(UnrealTargetBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateToken(request.Target, nameof(request.Target));
        ValidateToken(request.Platform, nameof(request.Platform));
        ValidateToken(request.Configuration, nameof(request.Configuration));
        if (request.MaximumParallelActions is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "MaximumParallelActions must be positive.");
        }

        var scriptName = request.Action switch
        {
            UnrealBuildAction.Build => "Build.bat",
            UnrealBuildAction.Clean => "Clean.bat",
            UnrealBuildAction.Rebuild => "Rebuild.bat",
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Action, "Unknown build action."),
        };
        var script = Path.Combine(
            request.Engine.RootPath,
            "Engine",
            "Build",
            "BatchFiles",
            scriptName);
        EnsureFileExists(script, request.Action.ToString(), request.Engine.RootPath);

        var arguments = new List<string>
        {
            request.Target,
            request.Platform,
            request.Configuration,
        };
        if (request.Project is not null)
        {
            arguments.Add($"-Project={request.Project.FilePath}");
        }

        arguments.Add("-WaitMutex");
        arguments.Add("-NoHotReloadFromIDE");
        arguments.Add("-Progress");
        if (request.MaximumParallelActions is not null)
        {
            arguments.Add($"-MaxParallelActions={request.MaximumParallelActions.Value}");
        }

        AppendAdditionalArguments(arguments, request.AdditionalArguments);
        return CreateBatchRequest(script, arguments, request.Engine.RootPath, maxCapturedLines: 20_000);
    }

    public static ProcessRequest CreatePackage(UnrealPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateToken(request.TargetPlatform, nameof(request.TargetPlatform));
        ValidateToken(request.Configuration, nameof(request.Configuration));
        if (string.IsNullOrWhiteSpace(request.ArchiveDirectory))
        {
            throw new ArgumentException("An archive directory is required.", nameof(request));
        }

        var script = Path.Combine(
            request.Engine.RootPath,
            "Engine",
            "Build",
            "BatchFiles",
            "RunUAT.bat");
        EnsureFileExists(script, "Package", request.Engine.RootPath);

        var arguments = new List<string>
        {
            "BuildCookRun",
            $"-project={request.Project.FilePath}",
            "-noP4",
            "-utf8output",
            "-unattended",
            $"-targetplatform={request.TargetPlatform}",
            $"-clientconfig={request.Configuration}",
        };
        AddSwitch(arguments, "-build", request.Build);
        AddSwitch(arguments, "-cook", request.Cook);
        AddSwitch(arguments, "-stage", request.Stage);
        AddSwitch(arguments, "-package", request.Package);
        AddSwitch(arguments, "-archive", request.Archive);
        if (request.Archive)
        {
            arguments.Add($"-archivedirectory={Path.GetFullPath(request.ArchiveDirectory)}");
        }

        AddSwitch(arguments, "-pak", request.Pak);
        AddSwitch(arguments, "-iostore", request.IoStore);
        AddSwitch(arguments, "-compressed", request.Compressed);
        AddSwitch(arguments, "-distribution", request.Distribution);
        AddSwitch(arguments, "-prereqs", request.IncludePrerequisites);
        if (!string.IsNullOrWhiteSpace(request.CookFlavor))
        {
            ValidateToken(request.CookFlavor, nameof(request.CookFlavor));
            arguments.Add($"-cookflavor={request.CookFlavor}");
        }

        AppendAdditionalArguments(arguments, request.AdditionalArguments);
        return CreateBatchRequest(script, arguments, request.Engine.RootPath, maxCapturedLines: 30_000);
    }

    public static ProcessRequest CreateBuildPlugin(BuildPluginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PluginDescriptorPath))
        {
            throw new ArgumentException("A .uplugin descriptor is required.", nameof(request));
        }

        var descriptorPath = Path.GetFullPath(request.PluginDescriptorPath);
        if (!Path.GetExtension(descriptorPath).Equals(".uplugin", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(descriptorPath))
        {
            throw new BuildOperationException(
                $"BuildPlugin is unavailable because '{descriptorPath}' is not a valid .uplugin file.",
                "BuildPlugin",
                request.Engine.RootPath);
        }

        if (string.IsNullOrWhiteSpace(request.PackageDirectory))
        {
            throw new ArgumentException("A package directory is required.", nameof(request));
        }

        if (request.TargetPlatforms.Count == 0)
        {
            throw new ArgumentException("At least one target platform is required.", nameof(request));
        }

        var targetPlatforms = request.TargetPlatforms
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targetPlatforms.Length == 0)
        {
            throw new ArgumentException("At least one target platform is required.", nameof(request));
        }

        foreach (var platform in targetPlatforms)
        {
            ValidateToken(platform, nameof(request.TargetPlatforms));
        }

        var engineRoot = Path.GetFullPath(request.Engine.RootPath);
        var pluginRoot = Path.GetDirectoryName(descriptorPath)!;
        var packageDirectory = Path.GetFullPath(request.PackageDirectory);
        if (PathsOverlap(engineRoot, packageDirectory) || PathsOverlap(pluginRoot, packageDirectory))
        {
            throw new BuildOperationException(
                "BuildPlugin package output must not overlap the engine or plugin source directory.",
                "BuildPlugin",
                request.Engine.RootPath);
        }

        if (File.Exists(packageDirectory) ||
            (Directory.Exists(packageDirectory) && Directory.EnumerateFileSystemEntries(packageDirectory).Any()))
        {
            throw new BuildOperationException(
                "BuildPlugin package output must be absent or empty because AutomationTool clears it.",
                "BuildPlugin",
                request.Engine.RootPath);
        }

        var script = Path.Combine(
            engineRoot,
            "Engine",
            "Build",
            "BatchFiles",
            "RunUAT.bat");
        EnsureFileExists(script, "BuildPlugin", request.Engine.RootPath);
        var arguments = new List<string>
        {
            "BuildPlugin",
            $"-Plugin={descriptorPath}",
            $"-Package={packageDirectory}",
            $"-TargetPlatforms={string.Join('+', targetPlatforms)}",
            "-utf8output",
            "-unattended",
        };
        AddSwitch(arguments, "-NoHostPlatform", request.NoHostPlatform);
        AddSwitch(arguments, "-StrictIncludes", request.StrictIncludes);
        AddSwitch(arguments, "-Unversioned", request.Unversioned);
        return CreateBatchRequest(script, arguments, engineRoot, maxCapturedLines: 30_000);
    }

    public static string FormatPreview(ProcessRequest request)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteForDisplay(request.FileName));
        if (request.RawArguments is not null)
        {
            builder.Append(' ');
            builder.Append(request.RawArguments);
            return builder.ToString();
        }

        foreach (var argument in request.Arguments)
        {
            builder.Append(' ');
            builder.Append(QuoteForDisplay(argument));
        }

        return builder.ToString();
    }

    private static ProcessRequest CreateBatchRequest(
        string script,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int maxCapturedLines)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows batch workflows are not available on this OS.");
        }

        var command = new StringBuilder();
        command.Append(QuoteForCmd(script));
        foreach (var argument in arguments)
        {
            command.Append(' ');
            command.Append(QuoteForCmd(argument));
        }

        return new ProcessRequest
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            RawArguments = $"/d /v:off /s /c \"{command}\"",
            WorkingDirectory = workingDirectory,
            MaxCapturedLines = maxCapturedLines,
        };
    }

    private static void AddSwitch(List<string> arguments, string value, bool enabled)
    {
        if (enabled)
        {
            arguments.Add(value);
        }
    }

    private static void AppendAdditionalArguments(
        List<string> arguments,
        IReadOnlyList<string>? additionalArguments)
    {
        foreach (var argument in additionalArguments ?? [])
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (argument.Contains('\r', StringComparison.Ordinal) ||
                argument.Contains('\n', StringComparison.Ordinal))
            {
                throw new ArgumentException("Additional arguments cannot contain newlines.");
            }

            arguments.Add(argument);
        }
    }

    private static void ValidateToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(character => !char.IsLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new ArgumentException(
                "The value may only contain letters, numbers, underscores and hyphens.",
                parameterName);
        }
    }

    private static string QuoteForCmd(string value)
    {
        if (value.Contains('"', StringComparison.Ordinal) ||
            value.Contains('%', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("Command arguments cannot contain quotes, percent signs or newlines.");
        }

        return $"\"{value}\"";
    }

    private static string QuoteForDisplay(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;

    private static bool PathsOverlap(string first, string second) =>
        IsDescendantOrEqual(first, second) || IsDescendantOrEqual(second, first);

    private static bool IsDescendantOrEqual(string parent, string candidate)
    {
        var parentFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var candidateFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(parentFull, candidateFull, comparison) ||
               candidateFull.StartsWith(parentFull + Path.DirectorySeparatorChar, comparison);
    }

    private static void EnsureFileExists(string path, string operation, string engineRoot)
    {
        if (!File.Exists(path))
        {
            throw new BuildOperationException(
                $"{operation} is unavailable because '{path}' is missing.",
                operation,
                engineRoot);
        }
    }
}

public sealed class BuildOperationException : Exception
{
    public BuildOperationException(
        string message,
        string operation,
        string engineRoot,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        EngineRoot = engineRoot;
    }

    public string Operation { get; }

    public string EngineRoot { get; }
}

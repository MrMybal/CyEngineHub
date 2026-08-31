using System.Globalization;
using System.Text.RegularExpressions;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Unreal.Acquisition;

public interface IUnrealSourceDownloadService
{
    Uri AccessGuideUri { get; }

    Uri OfficialRepositoryUri { get; }

    UnrealSourceDownloadPlan Prepare(UnrealSourceDownloadRequest request);

    Task<UnrealSourceDownloadResult> ExecuteAsync(
        UnrealSourceDownloadPlan plan,
        IProgress<UnrealSourceDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed partial class UnrealSourceDownloadService : IUnrealSourceDownloadService
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(30);
    private readonly IProcessRunner _processRunner;

    public UnrealSourceDownloadService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public Uri AccessGuideUri { get; } = new(
        "https://dev.epicgames.com/documentation/en-us/unreal-engine/downloading-source-code-in-unreal-engine");

    public Uri OfficialRepositoryUri { get; } = new(
        "https://github.com/EpicGames/UnrealEngine");

    public UnrealSourceDownloadPlan Prepare(UnrealSourceDownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var repository = ValidateRepository(request.RepositoryUri);
        var branch = ValidateBranch(request.Branch);
        var target = ValidateTarget(request.TargetDirectory);
        var parent = Path.GetDirectoryName(target)!;
        var arguments = new List<string>
        {
            "-c",
            "color.ui=false",
            "clone",
            "--progress",
            "--branch",
            branch,
            "--single-branch",
        };
        if (request.ShallowClone)
        {
            arguments.Add("--depth");
            arguments.Add("1");
        }

        arguments.Add(repository.AbsoluteUri);
        arguments.Add(target);
        var process = new ProcessRequest
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = parent,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["LC_ALL"] = "C",
            },
            MaxCapturedLines = 5_000,
        };
        var createdAt = DateTimeOffset.UtcNow;
        return new UnrealSourceDownloadPlan(
            Guid.NewGuid(),
            new UnrealSourceDownloadRequest(repository, branch, target, request.ShallowClone),
            process,
            createdAt,
            createdAt.Add(PlanLifetime));
    }

    public async Task<UnrealSourceDownloadResult> ExecuteAsync(
        UnrealSourceDownloadPlan plan,
        IProgress<UnrealSourceDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (DateTimeOffset.UtcNow > plan.ExpiresAt)
        {
            throw new UnrealSourceDownloadException(
                "Le plan de téléchargement du moteur a expiré.",
                "Clone",
                plan.Download.TargetDirectory);
        }

        ValidateTarget(plan.Download.TargetDirectory);
        var output = new InlineProgress<ProcessOutputLine>(line =>
        {
            var match = GitPercentRegex().Match(line.Text);
            double? ratio = match.Success && int.TryParse(
                match.Groups["percent"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var percent)
                ? Math.Clamp(percent / 100d, 0, 1)
                : null;
            progress?.Report(new UnrealSourceDownloadProgress(ratio, line.Text));
        });

        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(plan.Process, output, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new UnrealSourceDownloadException(
                $"Impossible de lancer git clone : {exception.Message}",
                "Clone",
                plan.Download.TargetDirectory,
                innerException: exception);
        }

        if (!result.Succeeded)
        {
            var lastError = result.CapturedOutput
                .LastOrDefault(line => line.Channel == ProcessOutputChannel.StandardError)
                ?.Text;
            throw new UnrealSourceDownloadException(
                (lastError ?? $"git clone a retourné le code {result.ExitCode}.") +
                " Le dossier partiel est conservé pour inspection.",
                "Clone",
                plan.Download.TargetDirectory,
                result.ExitCode);
        }

        var setupPath = Path.Combine(plan.Download.TargetDirectory, "Setup.bat");
        var engineDirectory = Path.Combine(plan.Download.TargetDirectory, "Engine");
        if (!File.Exists(setupPath) || !Directory.Exists(engineDirectory))
        {
            throw new UnrealSourceDownloadException(
                "Le clone a terminé mais ne ressemble pas à une source Unreal Windows valide.",
                "ValidateClone",
                plan.Download.TargetDirectory,
                result.ExitCode);
        }

        progress?.Report(new UnrealSourceDownloadProgress(1, "Sources Unreal téléchargées"));
        return new UnrealSourceDownloadResult(
            plan.Download.TargetDirectory,
            plan.Download.Branch,
            plan.Download.ShallowClone,
            result.Duration);
    }

    private static Uri ValidateRepository(Uri repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (!repository.IsAbsoluteUri ||
            !string.Equals(repository.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(repository.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(repository.UserInfo) ||
            !string.IsNullOrEmpty(repository.Query) ||
            !string.IsNullOrEmpty(repository.Fragment))
        {
            throw new ArgumentException(
                "Le dépôt moteur doit être une URL HTTPS GitHub sans identifiants intégrés.",
                nameof(repository));
        }

        var repositoryName = repository.Segments.LastOrDefault()?.TrimEnd('/');
        if (!string.Equals(repositoryName, "UnrealEngine.git", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(repositoryName, "UnrealEngine", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Le dépôt doit cibler EpicGames/UnrealEngine ou un fork GitHub nommé UnrealEngine.",
                nameof(repository));
        }

        return repository;
    }

    private static string ValidateBranch(string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        var normalized = branch.Trim();
        if (normalized.Length > 200 ||
            normalized[0] == '-' ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains("@{", StringComparison.Ordinal) ||
            normalized.Any(character =>
                char.IsControl(character) ||
                char.IsWhiteSpace(character) ||
                character is '\\' or '~' or '^' or ':' or '?' or '*'))
        {
            throw new ArgumentException("Nom de branche Git invalide.", nameof(branch));
        }

        return normalized;
    }

    private static string ValidateTarget(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        var target = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(targetDirectory.Trim().Trim('"')));
        var root = Path.GetPathRoot(target);
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(target, Path.TrimEndingDirectorySeparator(root), PathComparison))
        {
            throw new ArgumentException(
                "La racine d’un volume ne peut pas être utilisée comme cible.",
                nameof(targetDirectory));
        }

        var parent = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                $"Le dossier parent de la cible n’existe pas : {parent}");
        }

        if (Directory.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    "Une cible liée ou redirigée n’est pas autorisée.",
                    nameof(targetDirectory));
            }

            if (Directory.EnumerateFileSystemEntries(target).Any())
            {
                throw new ArgumentException(
                    "La cible du clone doit être absente ou vide.",
                    nameof(targetDirectory));
            }
        }
        else if (File.Exists(target))
        {
            throw new ArgumentException(
                "La cible du clone existe déjà comme fichier.",
                nameof(targetDirectory));
        }

        return target;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    [GeneratedRegex(@"(?<percent>\d{1,3})%", RegexOptions.CultureInvariant)]
    private static partial Regex GitPercentRegex();
}

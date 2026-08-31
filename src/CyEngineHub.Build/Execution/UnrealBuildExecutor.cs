using CyEngineHub.Build.Models;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Build.Execution;

public sealed class UnrealBuildExecutor
{
    private readonly IProcessRunner _processRunner;

    public UnrealBuildExecutor(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<UnrealBuildExecutionResult> ExecuteAsync(
        ProcessRequest request,
        IProgress<BuildLogLine>? output = null,
        BuildLogFileWriter? persistentLog = null,
        CancellationToken cancellationToken = default)
    {
        var warningCount = 0;
        var errorCount = 0;
        var tail = new Queue<BuildLogLine>();
        var gate = new object();
        var processProgress = new InlineProgress<ProcessOutputLine>(line =>
        {
            var classified = BuildLogClassifier.Classify(line);
            lock (gate)
            {
                if (classified.Severity == BuildLogSeverity.Warning)
                {
                    warningCount++;
                }
                else if (classified.Severity is BuildLogSeverity.Error or BuildLogSeverity.Fatal)
                {
                    errorCount++;
                }

                tail.Enqueue(classified);
                while (tail.Count > 500)
                {
                    tail.Dequeue();
                }
            }

            persistentLog?.Write(classified);
            output?.Report(classified);
        });

        var result = await _processRunner.RunAsync(request, processProgress, cancellationToken)
            .ConfigureAwait(false);
        lock (gate)
        {
            return new UnrealBuildExecutionResult(
                result.ExitCode,
                result.Duration,
                warningCount,
                errorCount,
                tail.ToArray());
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

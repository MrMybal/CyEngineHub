using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Infrastructure.Processes;
using Microsoft.Data.Sqlite;

namespace CyEngineHub.VersionBridge.Tool;

internal sealed class RepairJobs : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] TerminalStates = ["Completed", "Failed", "Cancelled"];
    private readonly object _gate = new();
    private readonly SemaphoreSlim _worker = new(1);
    private readonly Dictionary<string, ActiveJob> _active = new();
    private readonly string _workspace;
    private readonly string _runtime;
    private readonly string _legacyDatabase;
    private readonly FileStream _lease;
    private readonly DurableDownloadJournal<Job> _journal;
    private readonly UnrealVersionBridgeAgentWorkspace _service = new(new SystemProcessRunner());

    private sealed record Job(string JobId, string OperationId, string State, string Workspace,
        DateTimeOffset CreatedAt, DateTimeOffset? StartedAt = null, DateTimeOffset? FinishedAt = null,
        JsonElement? Outputs = null, JobError? Error = null, bool ArtifactsDeleted = false);
    private sealed record JobError(string Code, string Message);
    private sealed class ActiveJob
    {
        public CancellationTokenSource Stop { get; } = new();
        public Task Work { get; set; } = Task.CompletedTask;
    }

    internal RepairJobs(string workspace)
    {
        _workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        UnrealVersionBridgeAgentWorkspace.ValidateAgentWorkspace(_workspace);
        var legacy = Path.Combine(_workspace, "McpRuntime");
        _legacyDatabase = Path.Combine(legacy, "state.sqlite3");
        _runtime = Path.Combine(legacy, "CyMCPSDK");
        CheckPlainDirectory(legacy);
        CheckPlainDirectory(_runtime);
        Directory.CreateDirectory(_runtime);
        _lease = new FileStream(Path.Combine(_runtime, "runtime.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _journal = new(Path.Combine(_runtime, "jobs.json"));
        try
        {
            foreach (var job in _journal.Read().Values.Where(job => !TerminalStates.Contains(job.State)))
                Save(job with { State = "Failed", FinishedAt = DateTimeOffset.UtcNow,
                    Error = new("Interrupted", "Le serveur a été arrêté. Inspectez la copie avant de relancer.") });
        }
        catch { _lease.Dispose(); throw; }
    }

    internal static object[] Definitions()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../operations.json"));
        var operations = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        var definitions = new List<object>();
        foreach (var operation in operations)
        {
            var id = operation!["id"]!.GetValue<string>();
            var schema = Schema(new JsonObject
            {
                ["parameters"] = operation["inputSchema"]!.DeepClone(),
                ["executionTimeout"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 7200 },
                ["queueTimeout"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 7200 },
            }, "parameters");
            definitions.Add(Definition("op_" + id, operation["description"]!.GetValue<string>() +
                " Enqueues a repair job. Retrieve outputs with cy_get_job.", schema, JobSchema(operation["outputSchema"]), false));
        }
        var jobId = Schema(new JsonObject { ["jobId"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 } }, "jobId");
        definitions.Add(Definition("cy_get_job", "Read a repair job, its final outputs or its failure, by jobId.",
            jobId, JobSchema(), true));
        definitions.Add(Definition("cy_cancel_job", "Request cancellation of a queued or running repair job. Poll cy_get_job until it reaches a terminal state.",
            jobId, JobSchema(), false));
        definitions.Add(Definition("cy_list_jobs", "List the 100 most recent repair jobs, including imported historical results.",
            Schema(new()), Schema(new JsonObject { ["jobs"] = new JsonObject { ["type"] = "array" } }, "jobs"), true));
        definitions.Add(Definition("cy_finish_task", "Clear temporary outputs of completed jobs of this server. Retrieve any required outputs first. The repair copy and historical metadata remain.",
            Schema(new()), Schema(new JsonObject { ["removed"] = new JsonObject { ["type"] = "integer" } }, "removed"), false));
        return definitions.ToArray();
    }

    private static JsonObject Schema(JsonObject properties, params string[] required) => new()
    {
        ["type"] = "object", ["properties"] = properties,
        ["required"] = new JsonArray(required.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
        ["additionalProperties"] = false,
    };

    private static JsonObject JobSchema(JsonNode? output = null) => new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray("jobId", "state"),
        ["properties"] = new JsonObject
        {
            ["jobId"] = new JsonObject { ["type"] = "string" },
            ["state"] = new JsonObject { ["type"] = "string" },
            ["workspace"] = new JsonObject { ["type"] = "string" },
            ["outputs"] = new JsonObject
            {
                ["anyOf"] = new JsonArray(output?.DeepClone() ?? new JsonObject { ["type"] = "object" },
                    new JsonObject { ["type"] = "null" }),
            },
        },
    };

    private static object Definition(string name, string description, JsonObject inputSchema, JsonObject outputSchema, bool readOnly) =>
        new { name, description, inputSchema, outputSchema, annotations = new { readOnlyHint = readOnly } };

    internal Task<object> CallAsync(string name, JsonElement arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            object value = name switch
            {
                "cy_get_job" => Get(arguments.GetProperty("jobId").GetString()!),
                "cy_cancel_job" => Cancel(arguments.GetProperty("jobId").GetString()!),
                "cy_list_jobs" => new { jobs = Recent() },
                "cy_finish_task" => new { removed = CleanCompleted() },
                _ when name.StartsWith("op_", StringComparison.Ordinal) => Submit(name[3..], arguments),
                _ => throw new ArgumentException("Opération de réparation inconnue."),
            };
            return Task.FromResult(value);
        }
    }

    private Job Submit(string operation, JsonElement request)
    {
        if (_active.Count >= 16) throw new InvalidOperationException("La file de réparation est pleine.");
        var id = Guid.NewGuid().ToString("N");
        var job = new Job(id, operation, "Queued", Path.Combine(_runtime, "job-" + id), DateTimeOffset.UtcNow);
        var parameters = request.GetProperty("parameters").Clone();
        var queue = TimeSpan.FromSeconds(Timeout(request, "queueTimeout", 600));
        var execution = TimeSpan.FromSeconds(Timeout(request, "executionTimeout", 7200));
        Save(job);
        var active = new ActiveJob();
        _active.Add(id, active);
        active.Work = Task.Run(() => RunAsync(job, active, parameters, queue, execution));
        return job;
    }

    private static int Timeout(JsonElement request, string key, int fallback) =>
        request.TryGetProperty(key, out var timeout) ? timeout.GetInt32() : fallback;

    private async Task RunAsync(Job job, ActiveJob active, JsonElement parameters, TimeSpan queue, TimeSpan execution)
    {
        var entered = false;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(active.Stop.Token);
        try
        {
            stop.CancelAfter(queue);
            await _worker.WaitAsync(stop.Token).ConfigureAwait(false);
            entered = true;
            stop.CancelAfter(execution);
            stop.Token.ThrowIfCancellationRequested();
            job = job with { State = "Running", StartedAt = DateTimeOffset.UtcNow };
            Save(job);
            Directory.CreateDirectory(job.Workspace);
            var result = await _service.ExecuteAsync(_workspace, job.OperationId, parameters, job.Workspace, stop.Token).ConfigureAwait(false);
            job = job with { State = "Completed", Outputs = JsonSerializer.SerializeToElement(result, Json) };
        }
        catch (OperationCanceledException) when (active.Stop.IsCancellationRequested)
        { job = job with { State = "Cancelled" }; }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        { job = job with { State = "Failed", Error = new("Timeout", "Délai de réparation dépassé.") }; }
        catch (Exception error)
        { job = job with { State = "Failed", Error = new("RepairFailed", error.Message) }; }
        finally
        {
            lock (_gate)
            {
                try { Save(job with { FinishedAt = DateTimeOffset.UtcNow }); }
                finally
                {
                    if (entered) _worker.Release();
                    _active.Remove(job.JobId);
                    active.Stop.Dispose();
                }
            }
        }
    }

    private object Cancel(string id)
    {
        if (!_active.TryGetValue(id, out var job)) return Get(id);
        job.Stop.Cancel();
        return new { jobId = id, state = "Cancelling" };
    }

    private object Get(string id)
    {
        if (_journal.Read().TryGetValue(id, out var job)) return job;
        return Legacy(id).FirstOrDefault() is var previous && previous.ValueKind == JsonValueKind.Object
            ? previous : throw new ArgumentException("Tâche de réparation inconnue.");
    }

    private object[] Recent() => _journal.Read().Values.Cast<object>().Concat(Legacy(null).Cast<object>())
        .OrderByDescending(value => value is Job job ? job.CreatedAt :
            ((JsonElement)value).TryGetProperty("createdAt", out var created) &&
            DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var date) ? date : DateTimeOffset.MinValue)
        .Take(100).ToArray();

    private List<JsonElement> Legacy(string? id)
    {
        var records = new List<JsonElement>();
        if (!File.Exists(_legacyDatabase)) return records;
        if ((File.GetAttributes(_legacyDatabase) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("La base des tâches historiques ne doit pas être un lien.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _legacyDatabase, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = id is null
            ? "SELECT value FROM records WHERE kind='jobs' ORDER BY json_extract(value,'$.createdAt') DESC LIMIT 100"
            : "SELECT value FROM records WHERE kind='jobs' AND id=$id";
        if (id is not null) command.Parameters.AddWithValue("$id", id);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var record = JsonNode.Parse(rows.GetString(0))!.AsObject();
            if (!TerminalStates.Contains(record["state"]!.GetValue<string>()))
            {
                record["state"] = "Failed";
                record["error"] = JsonSerializer.SerializeToNode(new JobError("Interrupted", "Tâche historique interrompue."), Json);
            }
            records.Add(JsonSerializer.SerializeToElement(record, Json));
        }
        return records;
    }

    private int CleanCompleted()
    {
        if (_active.Count > 0) throw new InvalidOperationException("Attendez les tâches actives avant le nettoyage.");
        var count = 0;
        foreach (var job in _journal.Read().Values.Where(job => !job.ArtifactsDeleted && TerminalStates.Contains(job.State)))
        {
            if (!Guid.TryParseExact(job.JobId, "N", out _)) continue;
            var directory = Path.Combine(_runtime, "job-" + job.JobId);
            CheckPlainDirectory(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            Save(job with { ArtifactsDeleted = true });
            count++;
        }
        return count;
    }

    private static void CheckPlainDirectory(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Le répertoire des tâches ne doit pas être un lien.");
    }

    private void Save(Job job) => _journal.Upsert(job.JobId, job);

    public async ValueTask DisposeAsync()
    {
        Task[] work;
        lock (_gate)
        {
            foreach (var job in _active.Values) job.Stop.Cancel();
            work = _active.Values.Select(job => job.Work).ToArray();
        }
        try { await Task.WhenAll(work).ConfigureAwait(false); }
        finally { _worker.Dispose(); _lease.Dispose(); }
    }
}

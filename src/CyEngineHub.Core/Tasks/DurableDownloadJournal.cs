using System.Text.Json;

namespace CyEngineHub.Core.Tasks;

/// <summary>Atomic, synchronous write-ahead journal. Enqueue only after Upsert succeeds.</summary>
public sealed class DurableDownloadJournal<T>(string path)
{
    private readonly object _gate = new();
    private Dictionary<string, T>? _entries;

    public IReadOnlyDictionary<string, T> Read()
    {
        lock (_gate)
        {
            Load();
            return new Dictionary<string, T>(_entries!, StringComparer.Ordinal);
        }
    }

    public void Upsert(string id, T entry)
    {
        lock (_gate)
        {
            Load();
            var next = new Dictionary<string, T>(_entries!, StringComparer.Ordinal) { [id] = entry };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, next);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            _entries = next;
        }
    }

    private void Load()
    {
        if (_entries is not null) return;
        _entries = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, T>>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Journal de téléchargement vide ou invalide.")
            : new(StringComparer.Ordinal);
    }
}

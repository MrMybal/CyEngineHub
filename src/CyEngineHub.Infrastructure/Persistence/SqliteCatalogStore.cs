using System.Globalization;
using System.Text.Json;
using CyEngineHub.Core.Settings;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;
using Microsoft.Data.Sqlite;

namespace CyEngineHub.Infrastructure.Persistence;

public sealed class SqliteCatalogStore : ISettingsStore, IEngineCatalog, IProjectCatalog
{
    private readonly string _connectionString;

    public SqliteCatalogStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS engines (
                id TEXT PRIMARY KEY NOT NULL,
                association_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                root_path TEXT NOT NULL UNIQUE,
                editor_path TEXT NOT NULL,
                version_major INTEGER NOT NULL,
                version_minor INTEGER NOT NULL,
                version_patch INTEGER NOT NULL,
                installation_type TEXT NOT NULL,
                branch TEXT NULL,
                changelist INTEGER NULL,
                build_id TEXT NULL,
                discovery_source TEXT NOT NULL,
                health TEXT NOT NULL,
                issues_json TEXT NOT NULL,
                discovered_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY NOT NULL,
                name TEXT NOT NULL,
                file_path TEXT NOT NULL UNIQUE,
                root_path TEXT NOT NULL,
                engine_association TEXT NULL,
                description TEXT NULL,
                category TEXT NULL,
                modules_json TEXT NOT NULL,
                plugins_json TEXT NOT NULL,
                health TEXT NOT NULL,
                issues_json TEXT NOT NULL,
                discovered_at TEXT NOT NULL
            );

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    public async Task SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value)
            VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<UnrealEngineInstallation>> IEngineCatalog.GetAllAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, association_id, display_name, root_path, editor_path,
                   version_major, version_minor, version_patch, installation_type,
                   branch, changelist, build_id, discovery_source, health,
                   issues_json, discovered_at
            FROM engines
            ORDER BY version_major DESC, version_minor DESC, display_name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var engines = new List<UnrealEngineInstallation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            engines.Add(new UnrealEngineInstallation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                new EngineVersion(reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7)),
                ParseEnum(reader.GetString(8), EngineInstallationType.Custom),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetInt64(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetString(12),
                ParseEnum(reader.GetString(13), EngineHealth.Warning),
                DeserializeArray<string>(reader.GetString(14)),
                ParseTimestamp(reader.GetString(15))));
        }

        return engines;
    }

    public async Task UpsertAsync(
        UnrealEngineInstallation engine,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO engines(
                id, association_id, display_name, root_path, editor_path,
                version_major, version_minor, version_patch, installation_type,
                branch, changelist, build_id, discovery_source, health,
                issues_json, discovered_at)
            VALUES(
                $id, $associationId, $displayName, $rootPath, $editorPath,
                $major, $minor, $patch, $type,
                $branch, $changelist, $buildId, $source, $health,
                $issues, $discoveredAt)
            ON CONFLICT(id) DO UPDATE SET
                association_id = excluded.association_id,
                display_name = excluded.display_name,
                root_path = excluded.root_path,
                editor_path = excluded.editor_path,
                version_major = excluded.version_major,
                version_minor = excluded.version_minor,
                version_patch = excluded.version_patch,
                installation_type = excluded.installation_type,
                branch = excluded.branch,
                changelist = excluded.changelist,
                build_id = excluded.build_id,
                discovery_source = excluded.discovery_source,
                health = excluded.health,
                issues_json = excluded.issues_json,
                discovered_at = excluded.discovered_at;
            """;
        command.Parameters.AddWithValue("$id", engine.Id);
        command.Parameters.AddWithValue("$associationId", engine.AssociationId);
        command.Parameters.AddWithValue("$displayName", engine.DisplayName);
        command.Parameters.AddWithValue("$rootPath", engine.RootPath);
        command.Parameters.AddWithValue("$editorPath", engine.EditorPath);
        command.Parameters.AddWithValue("$major", engine.Version.Major);
        command.Parameters.AddWithValue("$minor", engine.Version.Minor);
        command.Parameters.AddWithValue("$patch", engine.Version.Patch);
        command.Parameters.AddWithValue("$type", engine.Type.ToString());
        command.Parameters.AddWithValue("$branch", (object?)engine.Branch ?? DBNull.Value);
        command.Parameters.AddWithValue("$changelist", (object?)engine.Changelist ?? DBNull.Value);
        command.Parameters.AddWithValue("$buildId", (object?)engine.BuildId ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", engine.DiscoverySource);
        command.Parameters.AddWithValue("$health", engine.Health.ToString());
        command.Parameters.AddWithValue("$issues", JsonSerializer.Serialize(engine.HealthIssues));
        command.Parameters.AddWithValue("$discoveredAt", FormatTimestamp(engine.DiscoveredAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<UnrealProject>> IProjectCatalog.GetAllAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, file_path, root_path, engine_association,
                   description, category, modules_json, plugins_json,
                   health, issues_json, discovered_at
            FROM projects
            ORDER BY name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var projects = new List<UnrealProject>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            projects.Add(new UnrealProject(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                DeserializeArray<string>(reader.GetString(7)),
                DeserializeArray<ProjectPluginReference>(reader.GetString(8)),
                ParseEnum(reader.GetString(9), ProjectHealth.Warning),
                DeserializeArray<string>(reader.GetString(10)),
                ParseTimestamp(reader.GetString(11))));
        }

        return projects;
    }

    public async Task UpsertAsync(
        UnrealProject project,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects(
                id, name, file_path, root_path, engine_association,
                description, category, modules_json, plugins_json,
                health, issues_json, discovered_at)
            VALUES(
                $id, $name, $filePath, $rootPath, $association,
                $description, $category, $modules, $plugins,
                $health, $issues, $discoveredAt)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                file_path = excluded.file_path,
                root_path = excluded.root_path,
                engine_association = excluded.engine_association,
                description = excluded.description,
                category = excluded.category,
                modules_json = excluded.modules_json,
                plugins_json = excluded.plugins_json,
                health = excluded.health,
                issues_json = excluded.issues_json,
                discovered_at = excluded.discovered_at;
            """;
        command.Parameters.AddWithValue("$id", project.Id);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$filePath", project.FilePath);
        command.Parameters.AddWithValue("$rootPath", project.RootPath);
        command.Parameters.AddWithValue(
            "$association",
            (object?)project.EngineAssociation ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", (object?)project.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$category", (object?)project.Category ?? DBNull.Value);
        command.Parameters.AddWithValue("$modules", JsonSerializer.Serialize(project.Modules));
        command.Parameters.AddWithValue("$plugins", JsonSerializer.Serialize(project.Plugins));
        command.Parameters.AddWithValue("$health", project.Health.ToString());
        command.Parameters.AddWithValue("$issues", JsonSerializer.Serialize(project.HealthIssues));
        command.Parameters.AddWithValue("$discoveredAt", FormatTimestamp(project.DiscoveredAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static T[] DeserializeArray<T>(string json) =>
        JsonSerializer.Deserialize<T[]>(json) ?? [];

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}

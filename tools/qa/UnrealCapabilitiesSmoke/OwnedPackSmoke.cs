using System.Security.Cryptography;
using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Projects.Creation;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

static class OwnedPackSmoke
{
    public static async Task<int> RunAsync(string root, string source, UnrealEngineInstallation[] engines, IProcessRunner runner)
    {
        Console.WriteLine("QA_ROOT " + root);
        var sourceFiles = Directory.GetFiles(source).ToDictionary(path => Path.GetFileName(path)!, path => SHA256.HashData(File.ReadAllBytes(path)));
        List<object> reports = [];
        try
        {
            var creator = new ProjectCreationService();
            async Task<ProjectCreationResult> Create(string name, UnrealEngineInstallation engine) =>
                await creator.CreateAsync(new(creator.Presets.Single(p => p.Id == "unreal-blueprint"), name, root,
                    engine.AssociationId, ["PythonScriptPlugin", "EditorScriptingUtilities"]));
            var src = await Create("OwnedSource57", engines[0]);
            await DerivedLibraryPackageService.MaterializeAsync(source, Path.Combine(root, "Materialized"), LibraryPackageContentKind.ContentPack);
            var asset = new RemoteFabAsset("owned-qa", "ue", "Owned pack isolated QA", "", "Fab", "ASSET_PACK", null, null, [], []);
            var version = new RemoteFabProjectVersion("owned-57", [], ["5.7"], ["Windows"]);
            FabInstallationScriptContext Context(string directory, ProjectCreationResult project, UnrealEngineInstallation engine) =>
                new(asset, version, LibraryPackageContentKind.ContentPack, directory,
                    new("qa", project.ProjectRoot, FabInstallationTargetKind.Project, project.ProjectRoot, engine.Version.ToString(),
                        new UnrealProject("qa", "QA", project.ProjectMarker, project.ProjectRoot, engine.AssociationId, null, null, [], [], ProjectHealth.Healthy, [], DateTimeOffset.UtcNow), engine), new Dictionary<string, string>());
            await new UnrealContentPackInstallationScript(new FabProjectContentImportService()).InstallAsync(Context(Path.Combine(root, "Materialized"), src, engines[0]));
            var saved = await runner.RunAsync(new ProcessRequest { FileName = engines[0].EditorPath, WorkingDirectory = src.ProjectRoot,
                Arguments = [src.ProjectMarker, "-run=ResavePackages", "-ProjectOnly", "-PackageFolder=" + Path.Combine(src.ProjectRoot, "Content"), "-Unattended", "-NullRHI", "-NoP4", "-abslog=" + Path.Combine(root, "resave57.log")] });
            if (!saved.Succeeded) throw new InvalidOperationException("Réenregistrement UE 5.7 échoué.");
            Console.WriteLine("PASS Owned pack resaved in actual UE 5.7");
            var store = new SqliteCatalogStore(Path.Combine(root, "qa.db")); await store.InitializeAsync();
            foreach (var engine in engines) await ((IEngineCatalog)store).UpsertAsync(engine);
            foreach (var engine in engines.Skip(1))
            {
                var target = await Create("Target" + engine.Version.Minor, engine);
                var script = new UnrealVersionBridgeInstallationScript(store, store, runner, new FabProjectContentImportService(), Path.Combine(root, "Bridge" + engine.Version.Minor));
                var result = await script.InstallAsync(Context(src.ProjectRoot, target, engine) with
                { Configuration = new Dictionary<string, string> { ["sourceVersion"] = "5.7", ["analyzeOnly"] = "true", ["keepStaging"] = "true" } });
                reports.Add(new { target = engine.Version.ToString(), result.Summary, result.Outputs });
                Console.WriteLine("RESULT " + engine.Version + " " + result.Summary);
                try
                {
                    await script.InstallAsync(Context(src.ProjectRoot, target, engine) with
                    { Configuration = new Dictionary<string, string> { ["sourceVersion"] = "5.7", ["keepStaging"] = "true" } });
                    throw new InvalidOperationException("La conversion de ce pack sans profils ne devait pas être autorisée.");
                }
                catch (InvalidOperationException error) when (error.Message.StartsWith("Conversion bloquée par les profils", StringComparison.Ordinal))
                {
                    reports.Add(new { downgrade = "blocked", target = engine.Version.ToString(), error = error.Message });
                    Console.WriteLine("CONFIRMED downgrade safely blocked for " + engine.Version);
                }
                if (Directory.Exists(Path.Combine(target.ProjectRoot, "Content")) && Directory.EnumerateFiles(Path.Combine(target.ProjectRoot, "Content"), "*.uasset", SearchOption.AllDirectories).Any())
                    throw new InvalidOperationException("Analyse seule a modifié le projet cible.");
            }
            var moved = await Create("Relocated57", engines[0]);
            try
            {
                var result = await new UnrealRelocatedContentInstallationScript(runner, new FabProjectContentImportService(), Path.Combine(root, "Relocation"))
                    .InstallAsync(Context(src.ProjectRoot, moved, engines[0]) with { Configuration = new Dictionary<string, string> { ["destinationFolder"] = "QA/Owned", ["keepStaging"] = "true" } });
                reports.Add(new { relocation = "passed", result.Summary, result.Outputs });
                Console.WriteLine("PASS Owned pack relocated and references validated");
            }
            catch (Exception ex) { reports.Add(new { relocation = "failed", error = ex.Message }); Console.WriteLine("FAIL relocation " + ex.Message); }
            return 0;
        }
        catch (Exception ex) { reports.Add(new { error = ex.ToString() }); Console.WriteLine(ex); return 1; }
        finally
        {
            var preserved = sourceFiles.All(file => SHA256.HashData(File.ReadAllBytes(Path.Combine(source, file.Key!))).SequenceEqual(file.Value));
            Console.WriteLine((preserved ? "PASS" : "FAIL") + " Original pack hashes unchanged");
            await File.WriteAllTextAsync(Path.Combine(root, "summary.json"), JsonSerializer.Serialize(new { preserved, reports }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

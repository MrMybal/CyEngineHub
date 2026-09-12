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

internal static class ComplexDowngrade
{
    public static async Task<int> RunAsync(string root, UnrealEngineInstallation sourceEngine, UnrealEngineInstallation targetEngine, IsolatedRunner runner)
    {
        if (sourceEngine.Version.Major != 5 || sourceEngine.Version.Minor != 6 || targetEngine.Version.Major != 5 || targetEngine.Version.Minor != 5)
            throw new InvalidOperationException("This regression specifically requires UE 5.6 -> 5.5");
        Console.WriteLine("COMPLEX_QA_ROOT " + root);
        var outcomes = new List<object>();
        var creator = new ProjectCreationService();
        var store = new SqliteCatalogStore(Path.Combine(root, "complex.db")); await store.InitializeAsync();
        await ((IEngineCatalog)store).UpsertAsync(sourceEngine); await ((IEngineCatalog)store).UpsertAsync(targetEngine);
        var source = await Project("ComplexSource", sourceEngine);
        var tga = new byte[18 + 4 * 4 * 3]; tga[2] = 2; tga[12] = 4; tga[14] = 4; tga[16] = 24;
        Array.Fill<byte>(tga, 180, 18, tga.Length - 18); await File.WriteAllBytesAsync(Path.Combine(root, "T_Complex.tga"), tga);
        try
        {
            await Inspect(source, sourceEngine, "create", "source-create");
            // A separate process proves the fixtures and their references survived saving.
            await Inspect(source, sourceEngine, "inspect", "source-reopen");
            var before = Fingerprint(Path.Combine(source.ProjectRoot, "Content"));
            foreach (var profile in new[] { "native-analysis", "safe", "extended" })
            {
                var target = await Project("Target_" + profile.Replace('-', '_'), targetEngine);
                var script = new UnrealVersionBridgeInstallationScript(store, store, runner, new FabProjectContentImportService(), Path.Combine(root, "Work_" + profile));
                var context = new FabInstallationScriptContext(new RemoteFabAsset("qa-complex-" + profile, "ue", "QA complex", "", "Fab", "ASSET_PACK", null, null, [], []),
                    new RemoteFabProjectVersion("qa-complex", [], ["5.6"], ["Windows"]), LibraryPackageContentKind.ContentPack, source.ProjectRoot,
                    new("qa", "QA", FabInstallationTargetKind.Project, target.ProjectRoot, "5.5", new UnrealProject("qa", "QA", target.ProjectMarker, target.ProjectRoot, "5.5", null, null, [], [], ProjectHealth.Healthy, [], DateTimeOffset.UtcNow), targetEngine),
                    new Dictionary<string, string> { ["profile"] = profile, ["keepStaging"] = "true", ["validateOutput"] = "true",
                        ["conversionMode"] = profile == "native-analysis" ? "native" : "portable",
                        ["analyzeOnly"] = (profile == "native-analysis").ToString(), ["allowLossyConversion"] = "true" });
                string? error = null;
                FabInstallationScriptResult? installed = null;
                try { installed = await script.InstallAsync(context); }
                catch (Exception exception) { error = exception.Message; Console.WriteLine("CONVERSION_REJECTED " + profile + ": " + error); }
                if (profile == "native-analysis" && (installed is null || !File.Exists(installed.Outputs["report"])))
                    throw new InvalidOperationException("Native analysis did not produce a report: " + error);
                if (profile != "native-analysis" && installed is not null)
                    throw new InvalidOperationException("Complex pack unexpectedly installed despite unsupported types");
                var plans = Directory.GetFiles(Path.Combine(root, "Work_" + profile), "asset-conversion-plan.json", SearchOption.AllDirectories);
                if (plans.Length != 1) throw new InvalidOperationException("Expected one per-asset plan before export");
                using var planReport = JsonDocument.Parse(await File.ReadAllTextAsync(plans[0]));
                if (planReport.RootElement.GetProperty("assets").GetArrayLength() != 9 || planReport.RootElement.GetProperty("canConvert").GetBoolean())
                    throw new InvalidOperationException("Expected nine inspected assets and a blocked conversion plan");
                if (Directory.GetFiles(Path.Combine(root, "Work_" + profile), "source-editor.log", SearchOption.AllDirectories).Length != 0)
                    throw new InvalidOperationException("Export started despite blocked preflight");
                await Inspect(target, targetEngine, "inspect", "target-" + profile);
                var sourceStable = before == Fingerprint(Path.Combine(source.ProjectRoot, "Content"));
                if (!sourceStable) throw new InvalidOperationException("Source fixture changed during downgrade");
                using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "source-reopen.json")));
                using var actual = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "target-" + profile + ".json")));
                var missing = baseline.RootElement.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("path").GetString())
                    .Except(actual.RootElement.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("path").GetString())).ToArray();
                var fullFidelity = missing.Length == 0 && baseline.RootElement.GetProperty("assets").GetRawText() == actual.RootElement.GetProperty("assets").GetRawText();
                outcomes.Add(new { profile, analysisOnly = profile == "native-analysis", operationSucceeded = installed is not null, error, fullFidelity, missing, sourceStable, plan = plans[0], installationSummary = installed?.Summary, outputs = installed?.Outputs });
                Console.WriteLine($"RESULT {profile}: operation={installed is not null}, fullFidelity={fullFidelity}, missing={missing.Length}");
            }
            await File.WriteAllTextAsync(Path.Combine(root, "complex-summary.json"), JsonSerializer.Serialize(new { outcomes, runner.PeakBytes, diskBytes = runner.Measure(), source = sourceEngine.Version, target = targetEngine.Version }, new JsonSerializerOptions { WriteIndented = true }));
            return 0; // Characterization completed, not a claim that the downgrade is lossless.
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "complex-harness-error.txt"), error.ToString()); Console.WriteLine(error); return 1;
        }

        Task<ProjectCreationResult> Project(string name, UnrealEngineInstallation engine) => creator.CreateAsync(new(creator.Presets.Single(p => p.Id == "unreal-blueprint"), name, root, engine.AssociationId,
            ["PythonScriptPlugin", "EditorScriptingUtilities"], creator.ConfigurationProfiles.Single(p => p.Id == "unreal-ue5-lite")));
        async Task Inspect(ProjectCreationResult project, UnrealEngineInstallation engine, string mode, string report)
        {
            var result = await runner.RunAsync(new ProcessRequest { FileName = engine.EditorPath, WorkingDirectory = project.ProjectRoot,
                Arguments = [project.ProjectMarker, "-run=pythonscript", "-script=" + Path.Combine(AppContext.BaseDirectory, "scripts", "complex.py"), "-Unattended", "-NullRHI", "-AllowCommandletAudio", "-NoP4", "-abslog=" + Path.Combine(root, report + ".log")],
                EnvironmentVariables = new Dictionary<string, string?> { ["CYHUB_COMPLEX_MODE"] = mode, ["CYHUB_COMPLEX_REPORT"] = Path.Combine(root, report + ".json"), ["CYHUB_COMPLEX_TEXTURE"] = Path.Combine(root, "T_Complex.tga") } });
            if (!result.Succeeded) throw new InvalidOperationException("QA fixture commandlet failed: " + report);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, report + ".json")));
            if (json.RootElement.GetProperty("errors").GetArrayLength() != 0) throw new InvalidOperationException("QA fixture error: " + json.RootElement.GetProperty("errors"));
            Console.WriteLine("INSPECT_OK " + report);
        }
    }
    private static string Fingerprint(string directory) => string.Join("|", Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file => Path.GetRelativePath(directory, file) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))));
}

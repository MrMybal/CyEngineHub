using CyEngineHub.Engines;
using CyEngineHub.Godot.Integration;
using CyEngineHub.Unity.Integration;

namespace CyEngineHub.Engines.Tests;

public sealed class LocalEnginePackageInstallationTests
{
    [Fact]
    public async Task UnityInstallsEmbeddedPackageAndInventoryFindsIt()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        var source = workspace.CreateUnityPackage("Source/com.cyenginehub.tools", "com.cyenginehub.tools", "1.2.3");
        workspace.Write("Source/com.cyenginehub.tools/Runtime/Tool.cs", "namespace CyEngineHub.Tools;\n");
        var provider = new UnityEngineModule();

        var plan = await provider.PreparePackageInstallAsync(source, project);
        var result = await provider.InstallPackageAsync(plan);
        var inventory = await provider.DiscoverPackagesAsync([project]);

        Assert.False(plan.ReplacesExisting);
        Assert.Null(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.DestinationDirectory, "Runtime", "Tool.cs")));
        var installed = Assert.Single(inventory.Packages);
        Assert.Equal("com.cyenginehub.tools", installed.Name);
        Assert.Equal("Package embarqué", installed.State);
        Assert.Equal("1.2.3", installed.Version);
    }

    [Fact]
    public async Task UnityReplacementRetainsPreviousVersionInBackup()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        var existing = workspace.CreateUnityPackage(
            "UnityProject/Packages/com.cyenginehub.tools",
            "com.cyenginehub.tools",
            "1.0.0");
        workspace.Write("UnityProject/Packages/com.cyenginehub.tools/old.txt", "ancienne version");
        var source = workspace.CreateUnityPackage("Source/new-package", "com.cyenginehub.tools", "2.0.0");
        workspace.Write("Source/new-package/new.txt", "nouvelle version");
        var provider = new UnityEngineModule();

        var plan = await provider.PreparePackageInstallAsync(source, project);
        var result = await provider.InstallPackageAsync(plan);

        Assert.True(plan.ReplacesExisting);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.DestinationDirectory, "new.txt")));
        Assert.False(File.Exists(Path.Combine(result.DestinationDirectory, "old.txt")));
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "old.txt")));
        Assert.NotEqual(existing, result.BackupDirectory);
    }

    [Fact]
    public async Task InstallationRefusesSourceChangedAfterConfirmation()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        var source = workspace.CreateUnityPackage("Source/package", "com.cyenginehub.safe", "1.0.0");
        var payload = workspace.Write("Source/package/Runtime/data.txt", "avant");
        var provider = new UnityEngineModule();
        var plan = await provider.PreparePackageInstallAsync(source, project);
        File.WriteAllText(payload, "après confirmation");

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.InstallPackageAsync(plan));

        Assert.Contains("changé", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(plan.DestinationDirectory));
    }

    [Fact]
    public async Task InstallationRefusesExistingTargetChangedAfterConfirmation()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        workspace.CreateUnityPackage(
            "UnityProject/Packages/com.cyenginehub.safe",
            "com.cyenginehub.safe",
            "1.0.0");
        var existingPayload = workspace.Write(
            "UnityProject/Packages/com.cyenginehub.safe/state.txt",
            "état initial");
        var source = workspace.CreateUnityPackage("Source/package", "com.cyenginehub.safe", "2.0.0");
        var provider = new UnityEngineModule();
        var plan = await provider.PreparePackageInstallAsync(source, project);
        File.WriteAllText(existingPayload, "modification locale");

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.InstallPackageAsync(plan));

        Assert.Contains("déjà installé a changé", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("modification locale", File.ReadAllText(existingPayload));
    }

    [Fact]
    public async Task UnityRejectsPackageRequiringNewerEditor()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        var source = workspace.CreateUnityPackage("Source/future", "com.cyenginehub.future", "1.0.0");
        workspace.Write(
            "Source/future/package.json",
            """
            {
              "name": "com.cyenginehub.future",
              "version": "1.0.0",
              "unity": "7000.0"
            }
            """);
        var provider = new UnityEngineModule();

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageInstallAsync(source, project));

        Assert.Contains("requiert Unity 7000.0", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GodotInstallsSingleAddonFromRepositoryLayoutWithoutEnablingIt()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateGodotProject();
        var source = workspace.CreateDirectory("Repository");
        workspace.CreateGodotAddon("Repository/addons/cy_tools", "Cy Tools", "1.5.0", "plugin.gd");
        var provider = new GodotEngineModule();

        var plan = await provider.PreparePackageInstallAsync(source, project);
        var result = await provider.InstallPackageAsync(plan);
        var inventory = await provider.DiscoverPackagesAsync([project]);

        Assert.Equal("cy_tools", plan.PackageId);
        Assert.True(File.Exists(Path.Combine(result.DestinationDirectory, "plugin.cfg")));
        Assert.DoesNotContain(
            "res://addons/cy_tools/plugin.cfg",
            File.ReadAllText(project.MarkerPath),
            StringComparison.OrdinalIgnoreCase);
        var installed = Assert.Single(inventory.Packages);
        Assert.Equal("Cy Tools", installed.Name);
        Assert.Equal("Plugin éditeur · désactivé", installed.State);
    }

    [Fact]
    public async Task GodotRejectsAddonWhoseScriptEscapesItsFolder()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateGodotProject();
        var source = workspace.CreateDirectory("UnsafeAddon");
        workspace.Write(
            "UnsafeAddon/plugin.cfg",
            "[plugin]\nname=\"Unsafe\"\nversion=\"1.0\"\nscript=\"../outside.gd\"\n");
        workspace.Write("outside.gd", "@tool\nextends EditorPlugin\n");
        var provider = new GodotEngineModule();

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageInstallAsync(source, project));

        Assert.Contains("externe ou non sûr", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(project.RootPath, "addons", "UnsafeAddon")));
    }

    [Fact]
    public async Task ReplacementRollbackRestoresPreviousVersionAndPersistsHistory()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        workspace.CreateUnityPackage(
            "UnityProject/Packages/com.cyenginehub.rollback",
            "com.cyenginehub.rollback",
            "1.0.0");
        workspace.Write("UnityProject/Packages/com.cyenginehub.rollback/old.txt", "version 1");
        var source = workspace.CreateUnityPackage(
            "Source/rollback-v2",
            "com.cyenginehub.rollback",
            "2.0.0");
        workspace.Write("Source/rollback-v2/new.txt", "version 2");
        var provider = new UnityEngineModule();
        var installPlan = await provider.PreparePackageInstallAsync(source, project);
        var installed = await provider.InstallPackageAsync(installPlan);

        var rollbackPlan = await provider.PreparePackageRollbackAsync(installed.Receipt);
        var rolledBack = await provider.RollbackPackageAsync(rollbackPlan);
        var history = await new UnityEngineModule().LoadPackageInstallHistoryAsync([project]);

        Assert.True(rolledBack.RestoredPreviousVersion);
        Assert.True(File.Exists(Path.Combine(installed.DestinationDirectory, "old.txt")));
        Assert.False(File.Exists(Path.Combine(installed.DestinationDirectory, "new.txt")));
        Assert.True(File.Exists(Path.Combine(rolledBack.RecoveryDirectory, "new.txt")));
        Assert.False(Directory.Exists(installed.BackupDirectory));
        var receipt = Assert.Single(history);
        Assert.NotNull(receipt.RolledBackAt);
        Assert.Equal(rolledBack.RecoveryDirectory, receipt.RolledBackPackageDirectory);
    }

    [Fact]
    public async Task NewGodotAddonRollbackRemovesTargetButKeepsRecoveryCopy()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateGodotProject();
        var source = workspace.CreateGodotAddon(
            "Source/cy_rollback",
            "Cy Rollback",
            "1.0.0",
            "plugin.gd");
        var provider = new GodotEngineModule();
        var installPlan = await provider.PreparePackageInstallAsync(source, project);
        var installed = await provider.InstallPackageAsync(installPlan);

        var rollbackPlan = await provider.PreparePackageRollbackAsync(installed.Receipt);
        var rolledBack = await provider.RollbackPackageAsync(rollbackPlan);

        Assert.False(rolledBack.RestoredPreviousVersion);
        Assert.False(Directory.Exists(installed.DestinationDirectory));
        Assert.True(File.Exists(Path.Combine(rolledBack.RecoveryDirectory, "plugin.cfg")));
        await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageRollbackAsync(rolledBack.Receipt));
    }

    [Fact]
    public async Task RollbackRefusesInstalledPackageModifiedAfterInstallation()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        var source = workspace.CreateUnityPackage(
            "Source/modified",
            "com.cyenginehub.modified",
            "1.0.0");
        workspace.Write("Source/modified/state.txt", "installé");
        var provider = new UnityEngineModule();
        var plan = await provider.PreparePackageInstallAsync(source, project);
        var installed = await provider.InstallPackageAsync(plan);
        var targetFile = Path.Combine(installed.DestinationDirectory, "state.txt");
        File.WriteAllText(targetFile, "modification utilisateur");

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageRollbackAsync(installed.Receipt));

        Assert.Contains("modifié", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("modification utilisateur", File.ReadAllText(targetFile));
    }

    [Fact]
    public async Task RollbackRefusesModifiedPreviousVersionBackup()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        workspace.CreateUnityPackage(
            "UnityProject/Packages/com.cyenginehub.backup",
            "com.cyenginehub.backup",
            "1.0.0");
        workspace.Write("UnityProject/Packages/com.cyenginehub.backup/state.txt", "version 1");
        var source = workspace.CreateUnityPackage(
            "Source/backup-v2",
            "com.cyenginehub.backup",
            "2.0.0");
        var provider = new UnityEngineModule();
        var plan = await provider.PreparePackageInstallAsync(source, project);
        var installed = await provider.InstallPackageAsync(plan);
        var backupFile = Path.Combine(installed.BackupDirectory!, "state.txt");
        File.WriteAllText(backupFile, "backup modifié");

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageRollbackAsync(installed.Receipt));

        Assert.Contains("sauvegardée a été modifiée", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(installed.DestinationDirectory));
    }

    [Fact]
    public async Task UnityRemovalPersistsHistoryAndCanRestoreEmbeddedPackage()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        var root = workspace.CreateUnityPackage(
            "UnityProject/Packages/com.cyenginehub.removable",
            "com.cyenginehub.removable",
            "1.4.0");
        workspace.Write("UnityProject/Packages/com.cyenginehub.removable/Runtime/tool.cs", "public class Tool {}\n");
        var provider = new UnityEngineModule();
        var package = Assert.Single((await provider.DiscoverPackagesAsync([project])).Packages);

        var removalPlan = await provider.PreparePackageRemovalAsync(package, project);
        var removed = await provider.RemovePackageAsync(removalPlan);
        var history = await new UnityEngineModule().LoadPackageRemovalHistoryAsync([project]);

        Assert.False(Directory.Exists(root));
        Assert.True(File.Exists(Path.Combine(removed.RecoveryDirectory, "Runtime", "tool.cs")));
        Assert.Null(Assert.Single(history).RestoredAt);

        var restorePlan = await provider.PreparePackageRestoreAsync(removed.Receipt);
        var restored = await provider.RestorePackageAsync(restorePlan);
        var restoredHistory = await new UnityEngineModule().LoadPackageRemovalHistoryAsync([project]);

        Assert.Equal(root, restored.DestinationDirectory);
        Assert.True(File.Exists(Path.Combine(root, "Runtime", "tool.cs")));
        Assert.False(Directory.Exists(removed.RecoveryDirectory));
        Assert.NotNull(Assert.Single(restoredHistory).RestoredAt);
    }

    [Fact]
    public async Task RemovalRefusesPackageModifiedAfterConfirmation()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        workspace.CreateUnityPackage(
            "UnityProject/Packages/com.cyenginehub.changed",
            "com.cyenginehub.changed",
            "1.0.0");
        var payload = workspace.Write(
            "UnityProject/Packages/com.cyenginehub.changed/state.txt",
            "avant");
        var provider = new UnityEngineModule();
        var package = Assert.Single((await provider.DiscoverPackagesAsync([project])).Packages);
        var plan = await provider.PreparePackageRemovalAsync(package, project);
        File.WriteAllText(payload, "après confirmation");

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.RemovePackageAsync(plan));

        Assert.Contains("modifié", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("après confirmation", File.ReadAllText(payload));
    }

    [Fact]
    public async Task RestoreRefusesModifiedRecoveryCopy()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateGodotProject();
        workspace.CreateGodotAddon(
            "GodotProject/addons/cy_modified",
            "Cy Modified",
            "1.0.0",
            "plugin.gd");
        var provider = new GodotEngineModule();
        var package = Assert.Single((await provider.DiscoverPackagesAsync([project])).Packages);
        var removed = await provider.RemovePackageAsync(
            await provider.PreparePackageRemovalAsync(package, project));
        var recoveryScript = Path.Combine(removed.RecoveryDirectory, "plugin.gd");
        File.WriteAllText(recoveryScript, "contenu modifié");

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageRestoreAsync(removed.Receipt));

        Assert.Contains("modifiée", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(removed.Receipt.DestinationDirectory));
    }

    [Fact]
    public async Task RestoreRefusesReoccupiedDestination()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateGodotProject();
        workspace.CreateGodotAddon(
            "GodotProject/addons/cy_collision",
            "Cy Collision",
            "1.0.0",
            "plugin.gd");
        var provider = new GodotEngineModule();
        var package = Assert.Single((await provider.DiscoverPackagesAsync([project])).Packages);
        var removed = await provider.RemovePackageAsync(
            await provider.PreparePackageRemovalAsync(package, project));
        Directory.CreateDirectory(removed.Receipt.DestinationDirectory);
        File.WriteAllText(Path.Combine(removed.Receipt.DestinationDirectory, "keep.txt"), "nouveau contenu");

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageRestoreAsync(removed.Receipt));

        Assert.Contains("occupée", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(removed.Receipt.DestinationDirectory, "keep.txt")));
        Assert.True(Directory.Exists(removed.RecoveryDirectory));
    }

    [Fact]
    public async Task UnityRefusesRemovalOfManifestDependencyEvenWhenEmbeddedFolderExists()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateUnityProject();
        workspace.CreateUnityPackage(
            "UnityProject/Packages/com.cyenginehub.direct",
            "com.cyenginehub.direct",
            "1.0.0");
        workspace.Write(
            "UnityProject/Packages/manifest.json",
            "{\"dependencies\":{\"com.cyenginehub.direct\":\"1.0.0\"}}");
        var provider = new UnityEngineModule();
        var package = Assert.Single((await provider.DiscoverPackagesAsync([project])).Packages);

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageRemovalAsync(package, project));

        Assert.Contains("manifest.json", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(package.RootPath));
    }

    [Fact]
    public async Task GodotRefusesRemovalWhileEditorPluginIsEnabled()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateGodotProject();
        workspace.CreateGodotAddon(
            "GodotProject/addons/cy_enabled",
            "Cy Enabled",
            "1.0.0",
            "plugin.gd");
        workspace.Write(
            "GodotProject/project.godot",
            "[editor_plugins]\nenabled=PackedStringArray(\"res://addons/cy_enabled/plugin.cfg\")\n");
        var provider = new GodotEngineModule();
        var package = Assert.Single((await provider.DiscoverPackagesAsync([project])).Packages);

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageRemovalAsync(package, project));

        Assert.Contains("Désactivez", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(package.RootPath));
    }

    [Fact]
    public async Task GodotRefusesRemovalWhenEditorPluginActivationStateIsUnknown()
    {
        using var workspace = new InstallWorkspace();
        var project = workspace.CreateGodotProject();
        workspace.CreateGodotAddon(
            "GodotProject/addons/cy_unknown",
            "Cy Unknown",
            "1.0.0",
            "plugin.gd");
        var provider = new GodotEngineModule();
        var discovered = Assert.Single((await provider.DiscoverPackagesAsync([project])).Packages);
        var package = discovered with { State = "Plugin éditeur · état inconnu" };

        var exception = await Assert.ThrowsAsync<LocalEnginePackageOperationException>(
            () => provider.PreparePackageRemovalAsync(package, project));

        Assert.Contains("actualisez", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(package.RootPath));
    }

    private sealed class InstallWorkspace : IDisposable
    {
        public InstallWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "CyEngineHub.EnginePackageInstallTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public LocalEngineProject CreateUnityProject()
        {
            var root = CreateDirectory("UnityProject");
            CreateDirectory("UnityProject", "Assets");
            CreateDirectory("UnityProject", "Packages");
            var marker = Write(
                "UnityProject/ProjectSettings/ProjectVersion.txt",
                "m_EditorVersion: 6000.0.10f1\n");
            Write("UnityProject/Packages/manifest.json", "{\"dependencies\":{}}");
            return CreateProject(EngineFamily.Unity, "unity", root, marker);
        }

        public LocalEngineProject CreateGodotProject()
        {
            var root = CreateDirectory("GodotProject");
            var marker = Write(
                "GodotProject/project.godot",
                "[application]\nconfig/name=\"Package Test\"\n");
            return CreateProject(EngineFamily.Godot, "godot", root, marker);
        }

        public string CreateUnityPackage(
            string relativeRoot,
            string packageName,
            string version)
        {
            var root = CreateDirectory(relativeRoot.Split('/'));
            Write(
                $"{relativeRoot}/package.json",
                $$"""
                {
                  "name": "{{packageName}}",
                  "version": "{{version}}"
                }
                """);
            return root;
        }

        public string CreateGodotAddon(
            string relativeRoot,
            string name,
            string version,
            string script)
        {
            var root = CreateDirectory(relativeRoot.Split('/'));
            Write(
                $"{relativeRoot}/plugin.cfg",
                $$"""
                [plugin]
                name="{{name}}"
                description="Addon de test"
                author="CyEngineHub"
                version="{{version}}"
                script="{{script}}"
                """);
            Write($"{relativeRoot}/{script}", "@tool\nextends EditorPlugin\n");
            return root;
        }

        public string CreateDirectory(params string[] segments)
        {
            var path = segments.Aggregate(Root, Path.Combine);
            Directory.CreateDirectory(path);
            return path;
        }

        public string Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static LocalEngineProject CreateProject(
            EngineFamily family,
            string providerId,
            string root,
            string marker) =>
            new(
                family,
                providerId,
                $"{providerId}:install-test",
                $"Projet {family}",
                root,
                marker,
                string.Empty,
                "Projet d’installation test");
    }
}

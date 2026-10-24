using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Core.Tests;

public sealed class ProjectCreationEngineOptionTests
{
    [Fact]
    public void SavedAliasAndFullVersionIdentifyTheEngineWithoutChangingItsIdentity()
    {
        var engine = Engine();
        var option = new ProjectCreationEngineOptionViewModel(engine, "  Oculus source  ");
        Assert.Equal("Oculus source", option.Name);
        Assert.Equal("Oculus source · 5.5.3", option.Label);
        Assert.Contains(engine.RootPath, option.ToolTip, StringComparison.Ordinal);
        Assert.Contains(engine.AssociationId, option.ToolTip, StringComparison.Ordinal);
        Assert.Same(engine, option.Engine);
        Assert.Equal(engine.AssociationId, option.Association);
    }

    [Fact]
    public void RenamingUpdatesTheExistingOptionAndClearingRestoresTheOriginalName()
    {
        var engine = Engine();
        var option = new ProjectCreationEngineOptionViewModel(engine);
        var changes = new List<string?>();
        option.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        option.UpdateAlias("Custom engine");
        Assert.Equal("Custom engine", option.Name);
        Assert.Contains(nameof(option.Label), changes);
        Assert.Contains(nameof(option.ToolTip), changes);
        option.UpdateAlias(" ");
        Assert.Equal(engine.DisplayName, option.Name);
        Assert.Equal(engine.RootPath, option.Path);
    }

    private static UnrealEngineInstallation Engine() => new("fixture", "Oculus-5.5", "Unreal Engine 5.5",
        Path.Combine(Path.GetTempPath(), "FixtureEngine"), "UnrealEditor.exe", new(5, 5, 3),
        EngineInstallationType.SourceFork, null, null, null, "Fixture", EngineHealth.Healthy, [], DateTimeOffset.UtcNow);
}

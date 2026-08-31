using CyEngineHub.Projects.Models;
using CyEngineHub.Projects.Parsing;

namespace CyEngineHub.Projects.Tests;

public sealed class UprojectDescriptorParserTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Projet démo {Guid.NewGuid():N}");

    [Fact]
    public async Task ParserReadsAssociationModulesAndPlugins()
    {
        Directory.CreateDirectory(_testDirectory);
        var projectFile = Path.Combine(_testDirectory, "Beyond.uproject");
        await File.WriteAllTextAsync(
            projectFile,
            """
            {
              "FileVersion": 3,
              "EngineAssociation": "5.8",
              "Description": "Test project",
              "Modules": [
                { "Name": "Beyond", "Type": "Runtime" },
              ],
              "Plugins": [
                { "Name": "CyTrace", "Enabled": true },
                { "Name": "Unused", "Enabled": false }
              ]
            }
            """);

        var project = await new UprojectDescriptorParser().ParseAsync(projectFile);

        Assert.Equal("Beyond", project.Name);
        Assert.Equal("5.8", project.EngineAssociation);
        Assert.Equal(["Beyond"], project.Modules);
        Assert.Equal(2, project.Plugins.Count);
        Assert.Equal(ProjectHealth.Healthy, project.Health);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}


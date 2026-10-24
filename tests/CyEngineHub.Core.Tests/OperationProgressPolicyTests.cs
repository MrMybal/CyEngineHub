using CyEngineHub.Desktop.ImGui;

namespace CyEngineHub.Core.Tests;

public sealed class OperationProgressPolicyTests
{
    [Theory]
    [InlineData("En cours", .5f, "Active")]
    [InlineData("En cours", 1f, "Active")]
    [InlineData("Téléchargement", 1f, "Active")]
    [InlineData("Terminé", 1f, "Completed")]
    [InlineData("Completed", 1f, "Completed")]
    [InlineData("Échec", 1f, "Failed")]
    [InlineData("Échec de l’ajout", 1f, "Failed")]
    [InlineData("Annulé", .5f, "Canceled")]
    [InlineData("", 1f, "Completed")]
    public void TerminalStateOverridesPercentage(string state, float fraction, string expected) =>
        Assert.Equal(expected, OperationProgressPolicy.Resolve(state, fraction).ToString());
    [Fact]
    public void IndeterminateProgressNeverLooksCompleted() =>
        Assert.Equal(OperationProgressTone.Active, OperationProgressPolicy.Resolve(null, 1, true));
}

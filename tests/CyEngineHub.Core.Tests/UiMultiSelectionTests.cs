using CyEngineHub.Desktop.ImGui;

namespace CyEngineHub.Core.Tests;

public sealed class UiMultiSelectionTests
{
    [Fact]
    public void ControlTogglesAndPlainClickReplacesSelection()
    {
        var model = new UiMultiSelection(); model.Synchronize("Fab", ["a", "b", "c"]);
        model.Select("a", false, false); model.Select("c", true, false);
        Assert.Equal(2, model.Keys.Count);
        model.Select("a", true, false); Assert.Equal("c", Assert.Single(model.Keys));
        model.Select("b", false, false); Assert.Equal("b", Assert.Single(model.Keys));
    }
    [Fact]
    public void ShiftUsesDisplayedOrderAndControlShiftAddsTheRange()
    {
        var model = new UiMultiSelection(); model.Synchronize("projects", ["c", "b", "a", "d"]);
        model.Select("b", false, false); model.Select("d", false, true);
        Assert.Equal(3, model.Keys.Count); Assert.DoesNotContain("c", model.Keys);
        model.Select("c", true, true); Assert.Equal(4, model.Keys.Count);
    }
    [Fact]
    public void RightClickPreservesASelectedGroupButSelectsAnUnselectedItem()
    {
        var model = new UiMultiSelection(); model.Synchronize("Fab", ["a", "b", "c"]);
        model.Select("a", false, false); model.Select("b", true, false);
        model.Select("a", false, false, true); Assert.Equal(2, model.Keys.Count);
        model.Select("c", false, false, true); Assert.Equal("c", Assert.Single(model.Keys));
    }
    [Fact]
    public void ViewChangesPreserveStableKeysWhileFiltersAndSourcesPruneThem()
    {
        var model = new UiMultiSelection(); model.Synchronize("Fab", ["a", "b", "c"]);
        model.Select("a", false, false); model.Select("c", true, false);
        model.Synchronize("Fab", ["C", "b", "A"]); Assert.Equal(2, model.Keys.Count);
        model.Synchronize("Fab", ["a", "b"]); Assert.Equal("a", Assert.Single(model.Keys), ignoreCase: true);
        model.Synchronize("Megascans", ["a", "b"]); Assert.Empty(model.Keys);
    }
}

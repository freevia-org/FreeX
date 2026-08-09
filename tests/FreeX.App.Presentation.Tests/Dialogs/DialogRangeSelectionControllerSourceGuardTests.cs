using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class DialogRangeSelectionControllerSourceGuardTests
{
    [Fact]
    public void ControllerAndFormatter_RemainRendererNeutral()
    {
        var root = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "Dialogs");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(root, "DialogRangeSelection*.cs").Select(File.ReadAllText));

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia.");
        source.Should().NotContain("FreeX.App.Host");
        source.Should().NotContain("FreeX.App.Avalonia");
    }

    [Theory]
    [InlineData("FreeX.App.Host")]
    [InlineData("FreeX.App.Avalonia")]
    public void Renderers_DelegateLifecycleAndKeyDecisionsToPortableController(string projectName)
    {
        var root = RepositoryFileLocator.FindDirectory("src", projectName);
        var source = File.ReadAllText(Path.Combine(root, "MainWindow.DialogRangeSelection.cs"));

        source.Should().Contain("DialogRangeSelectionController<DialogRangePickerContext>");
        source.Should().Contain("_dialogRangeSelectionController.Begin(");
        source.Should().Contain("_dialogRangeSelectionController.HandleKey(");
        source.Should().Contain("_dialogRangeSelectionController.Complete(");
        source.Should().Contain("_dialogRangeSelectionController.Cancel(");
        source.Should().Contain("_dialogRangeSelectionController.FinishTransition(");
        source.Should().Contain("DialogRangeSelectionGeometryPlanner.ResolveDimension(");
        source.Should().Contain("DialogRangeSelectionTransition<DialogRangePickerContext>");
        source.Should().NotContain("_dialogRangeSelectionController.DecideKey(");
        source.Should().NotContain("EffectiveDialogRangeSelectionDimension(");
        source.Should().NotContain("transition.ApplySelection");
        source.Should().NotContain("transition.RestoreOriginalText");
        source.Should().NotContain("transition.RestoreDialog");
        source.Should().NotContain("DialogRangePickerSession");
        source.Should().NotContain("private enum DialogRangeSelectionFormat");
        source.Should().NotContain("format switch");
    }
}

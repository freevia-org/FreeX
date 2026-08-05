using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class StyleDialogWorkflowSourceGuardTests
{
    [Fact]
    public void StyleDialog_DelegatesOptionProjectionAndAcceptanceToPresentationSession()
    {
        var source = ReadAvaloniaSource("StyleDialog.cs");

        source.Should().Contain("_session = StyleDialogPlanner.CreateSession(");
        source.Should().Contain("_session.InitialState");
        source.Should().Contain("_session.PlanAcceptance(new StyleDialogControlState(");
        source.Should().Contain("StyleDialogPlanner.BuildStyleNamesById(document)");
        source.Should().Contain("StyleDialogPlanner.CreateManageStylesSession(");
        source.Should().Contain("_session.PlanSort(sortIndex)");
        source.Should().Contain("_session.PlanAction(ManageStyleActionKind.Apply");
        source.Should().Contain("StyleDialogPlanner.ManageStyleSortLabels");
        source.Should().NotContain("StyleDialogPlanner.BuildStyleOptions(");
        source.Should().NotContain("StyleDialogPlanner.TryBuildDefinition(");
        source.Should().NotContain("private static string? SelectedId(");
        source.Should().NotContain("private static int IndexOfId(");
        source.Should().NotContain("SelectedIndex switch");
        source.Should().NotContain("_rows.FindIndex(");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", fileName));
    }
}

using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class StyleDialogWorkflowSourceGuardTests
{
    [Fact]
    public void StyleDialog_DelegatesOptionProjectionAndAcceptanceToPresentationSession()
    {
        var source = ReadHostSource("StyleDialog.cs");

        source.Should().Contain("StyleDialogPlanner.CreateSession(");
        source.Should().Contain("session.InitialState");
        source.Should().Contain("session.PlanAcceptance(new StyleDialogControlState(");
        source.Should().Contain("StyleDialogPlanner.CreateManageStylesSession(");
        source.Should().Contain("session.PlanSort(sortIndex)");
        source.Should().Contain("session.PlanAction(ManageStyleActionKind.Apply");
        source.Should().Contain("StyleDialogPlanner.ManageStyleSortLabels");
        source.Should().NotContain("StyleDialogPlanner.BuildStyleOptions(");
        source.Should().NotContain("StyleDialogPlanner.TryBuildDefinition(");
        source.Should().NotContain("private static string? SelectedId(");
        source.Should().NotContain("SelectedIndex switch");
        source.Should().NotContain("rows.FindIndex(");
    }

    private static string ReadHostSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", fileName));
    }
}

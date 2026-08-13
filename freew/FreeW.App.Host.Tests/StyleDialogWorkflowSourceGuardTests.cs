using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class StyleDialogWorkflowSourceGuardTests
{
    [Fact]
    public void StyleDialog_DelegatesOptionProjectionAndAcceptanceToPresentationSession()
    {
        var source = ReadHostSource("StyleDialog.cs");

        source.Should().Contain("StyleDialogPlanner.CreateNewSession(");
        source.Should().Contain("StyleDialogPlanner.CreateModifySession(");
        source.Should().Contain("StyleDialogPlanner.CreateNewSession(document, defaultBasedOnId)");
        source.Should().Contain("StyleDialogPlanner.CreateModifySession(document, existing)");
        source.Should().Contain("session.InitialState");
        source.Should().Contain("session.ValidationTitle");
        source.Should().Contain("state.InitialFocus == StyleDialogFocusTarget.BasedOn");
        source.Should().Contain("session.PlanAcceptance(StyleDialogPlanner.CaptureControlState(");
        source.Should().Contain("StyleDialogPlanner.CreateManageStylesSession(");
        source.Should().Contain("session.PlanSort(sortIndex)");
        source.Should().Contain("session.PlanAction(actionKind, list.SelectedIndex)");
        source.Should().Contain("session.State.SortIndex");
        source.Should().Contain("var surface = StyleDialogPlanner.Surface;");
        source.Should().Contain("var surface = StyleDialogPlanner.Surface.Manage;");
        source.Should().Contain("foreach (var spec in surface.Fields)");
        source.Should().Contain("foreach (var spec in surface.Effects)");
        source.Should().Contain("foreach (var spec in surface.Actions)");
        source.Should().Contain("AutomationProperties.SetAutomationId(");
        source.Should().Contain("StyleDialogPlanner.ManageStyleSortLabels");
        source.Should().NotContain("StyleDialogPlanner.CreateSession(");
        source.Should().NotContain("StyleDialogPlanner.BuildStyleOptions(");
        source.Should().NotContain("StyleDialogPlanner.TryBuildDefinition(");
        source.Should().NotContain("new StyleDialogControlState(");
        source.Should().NotContain("Content = \"Bold\"");
        source.Should().NotContain("private static string? SelectedId(");
        source.Should().NotContain("SelectedIndex switch");
        source.Should().NotContain("rows.FindIndex(");
        source.Should().NotContain("Show(owner, \"New Style\"");
        source.Should().NotContain("Title = \"Manage Styles\"");
    }

    private static string ReadHostSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", fileName));
    }
}

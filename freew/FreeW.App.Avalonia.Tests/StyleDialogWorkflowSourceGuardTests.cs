using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class StyleDialogWorkflowSourceGuardTests
{
    [Fact]
    public void StyleDialog_DelegatesOptionProjectionAndAcceptanceToPresentationSession()
    {
        var source = ReadAvaloniaSource("StyleDialog.cs");

        source.Should().Contain("StyleDialogPlanner.CreateNewSession(");
        source.Should().Contain("StyleDialogPlanner.CreateModifySession(");
        source.Should().Contain("_session = session;");
        source.Should().Contain("_session.InitialState");
        source.Should().Contain("_session.ValidationTitle");
        source.Should().Contain("state.InitialFocus == StyleDialogFocusTarget.BasedOn");
        source.Should().Contain("_session.PlanAcceptance(StyleDialogPlanner.CaptureControlState(");
        source.Should().Contain("StyleDialogPlanner.CreateNewSession(document, defaultBasedOnId)");
        source.Should().Contain("StyleDialogPlanner.CreateModifySession(document, existing)");
        source.Should().NotContain("StyleNamesById(");
        source.Should().Contain("StyleDialogPlanner.CreateManageStylesSession(");
        source.Should().Contain("_session.PlanSort(sortIndex)");
        source.Should().Contain("_session.PlanAction(ManageStyleActionKind.Apply");
        source.Should().Contain("_session.State.SortIndex");
        source.Should().Contain("private static readonly StyleDialogSurfaceSpec Surface = StyleDialogPlanner.Surface;");
        source.Should().Contain("private static readonly ManageStyleSurfaceSpec Surface = StyleDialogPlanner.Surface.Manage;");
        source.Should().Contain("foreach (var spec in Surface.Fields)");
        source.Should().Contain("foreach (var spec in Surface.Effects)");
        source.Should().Contain("Button(Surface.Action(ManageStyleCommandKind.Apply)");
        source.Should().Contain("AutomationProperties.SetAutomationId(");
        source.Should().Contain("StyleDialogPlanner.ManageStyleSortLabels");
        source.Should().NotContain("StyleDialogPlanner.CreateSession(");
        source.Should().NotContain("StyleDialogPlanner.BuildStyleOptions(");
        source.Should().NotContain("StyleDialogPlanner.TryBuildDefinition(");
        source.Should().NotContain("new StyleDialogControlState(");
        source.Should().NotContain("Content = \"Bold\"");
        source.Should().NotContain("private static string? SelectedId(");
        source.Should().NotContain("private static int IndexOfId(");
        source.Should().NotContain("SelectedIndex switch");
        source.Should().NotContain("_rows.FindIndex(");
        source.Should().NotContain("new StyleDialog(\"New Style\"");
        source.Should().NotContain("Title = \"Manage Styles\"");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", fileName));
    }
}

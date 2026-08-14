using System.IO;

public sealed class DialogTailDedupSourceTests
{
    [Fact]
    public void AvaloniaDialogTailDelegatesPortableWorkflowsToPresentationSessions()
    {
        var motion = ReadSource("MotionPathEditorDialog.cs");
        motion.Should().Contain("MotionPathEditorDialogSession");
        motion.Should().Contain("MotionPathEditorDialogFormSession<Row>");
        motion.Should().Contain("MotionPathEditorNativeRowSession<");
        motion.Should().Contain("_native.Initialize(");
        motion.Should().Contain("_native.RefreshEnablement()");
        motion.Should().Contain("_native.CaptureInput()");
        motion.Should().Contain("_session.Surface");
        motion.Should().Contain("AutomationProperties.SetName(");
        motion.Should().Contain("AutomationProperties.SetAutomationId(");
        motion.Should().NotContain("MotionPathEditingPlanner.");
        motion.Should().NotContain("double.TryParse");
        motion.Should().NotContain("AutomationIdToken.AppendSegment(");
        motion.Should().NotContain("rowIndex.ToString()");
        motion.Should().NotContain("MotionPathEditorRowProjection.BuildPlan(");
        motion.Should().NotContain("MotionPathEditorRowProjection.BuildEnablement(");

        var rotation = ReadSource("RotationOptionsDialog.cs");
        rotation.Should().Contain("RotationOptionsDialogSession");
        rotation.Should().Contain("_session.Surface");
        rotation.Should().Contain("AutomationProperties.SetName(");
        rotation.Should().Contain("AutomationProperties.SetAutomationId(");
        rotation.Should().NotContain("SelectedShapeIds");
        rotation.Should().NotContain("SetSelectedRotation");
        rotation.Should().NotContain("RotationOptionsPlanner.");

        var slideShow = ReadSource("SlideShowSettingsDialog.cs");
        slideShow.Should().Contain("SlideShowSettingsDialogSession");
        slideShow.Should().Contain("SlideShowSettingsDialogFormSession<Control>");
        slideShow.Should().Contain("_formSession.CaptureInput()");
        slideShow.Should().Contain("var initial = _session.InitialInput;");
        slideShow.Should().Contain("SlideShowSettingsDialogSession.ShowTypeOptions");
        slideShow.Should().NotContain("SlideShowSettingsPlanner.");
        slideShow.Should().NotContain("new SlideShowSettingsDialogInput");
        slideShow.Should().NotContain("\"Presented by a speaker\"");
        slideShow.Should().NotContain("Math.Clamp");
        slideShow.Should().NotContain("uint.TryParse");

        var customShow = ReadSource("CustomShowDialog.cs");
        customShow.Should().Contain("SlideShowCustomShowDialogSession");
        customShow.Should().Contain("SlideShowCustomShowDialogNativeComposition<Control, DockPanel>");
        customShow.Should().Contain("_renderer.Controller.Initialize()");
        customShow.Should().Contain("_renderer.Controller.SelectShow()");
        customShow.Should().Contain("_renderer.Controller.SelectSlide()");
        customShow.Should().Contain("_renderer.Actions.Execute(");
        customShow.Should().Contain("_renderer.Controller.Reorder(");
        customShow.Should().NotContain("_formSession.ApplyFullPlan(plan)");
        customShow.Should().NotContain("_formSession.ApplySelectedShowPlan(plan)");
        customShow.Should().NotContain("_formSession.ApplySlideSelection(plan)");
        customShow.Should().NotContain("SlideShowCustomShowPlanner.");
        customShow.Should().NotContain("record CustomShowListItem");
        customShow.Should().NotContain("record CustomShowSlideListItem");
        customShow.Should().NotContain("plan.CanRename");
        customShow.Should().NotContain("plan.SelectedShow?.Name ?? string.Empty");

        var chartEx = ReadSource("ChartExSeriesLayoutDialog.cs");
        chartEx.Should().Contain("ChartExSeriesLayoutDialogSession");
        chartEx.Should().NotContain("BuildOptions(");
        chartEx.Should().NotContain("BuildLayoutChoices(");
        chartEx.Should().NotContain("BuildCommitPlan(");
        chartEx.Should().NotContain("FormatLayoutLabel(");
    }

    private static string ReadSource(string fileName) =>
        File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            fileName));

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}

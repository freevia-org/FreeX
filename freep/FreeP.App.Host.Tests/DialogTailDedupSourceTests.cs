using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class DialogTailDedupSourceTests
{
    [Fact]
    public void WpfDialogTailDelegatesPortableWorkflowsToPresentationSessions()
    {
        var motion = ReadHostSource("MotionPathEditorDialog.cs");
        motion.Should().Contain("MotionPathEditorDialogSession");
        motion.Should().Contain("MotionPathEditorDialogFormSession<Row>");
        motion.Should().Contain("MotionPathEditorNativeRowSession<ComboBox, TextBox>");
        motion.Should().Contain("_native.Initialize(_surface, _value, rowIndex)");
        motion.Should().Contain("_native.RefreshEnablement()");
        motion.Should().Contain("_native.CaptureInput()");
        motion.Should().Contain("_session.Surface");
        motion.Should().Contain("AutomationProperties.SetName(");
        motion.Should().Contain("AutomationProperties.SetAutomationId(");
        motion.Should().NotContain("MotionPathEditingPlanner.");
        motion.Should().NotContain("MotionPathEditorRowProjection.BuildPlan(");
        motion.Should().NotContain("double.TryParse");
        motion.Should().NotContain("AutomationIdToken.AppendSegment(");
        motion.Should().NotContain("rowIndex.ToString()");

        var rotation = ReadHostSource("RotationOptionsDialog.cs");
        rotation.Should().Contain("RotationOptionsDialogSession");
        rotation.Should().Contain("_session.Surface");
        rotation.Should().Contain("AutomationProperties.SetName(");
        rotation.Should().Contain("AutomationProperties.SetAutomationId(");
        rotation.Should().NotContain("SelectedShapeIds");
        rotation.Should().NotContain("SetSelectedRotation");
        rotation.Should().NotContain("RotationOptionsPlanner.");

        var slideShow = ReadHostSource("SlideShowSettingsDialog.cs");
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

        var customShow = ReadHostSource("CustomShowDialog.cs");
        customShow.Should().Contain("SlideShowCustomShowDialogSession");
        customShow.Should().Contain("SlideShowCustomShowDialogNativeComposition<FrameworkElement, DockPanel>");
        customShow.Should().Contain("_renderer.Controller.Initialize()");
        customShow.Should().Contain("_renderer.Controller.SelectShow()");
        customShow.Should().Contain("_renderer.Controller.SelectSlide()");
        customShow.Should().Contain("_renderer.Actions.Execute(SlideShowCustomShowDialogAction.AddSlide, slide.SlideId)");
        customShow.Should().NotContain("_formSession.");
        customShow.Should().NotContain("SlideShowCustomShowPlanner.");
        customShow.Should().NotContain("record CustomShowListItem");
        customShow.Should().NotContain("record CustomShowSlideListItem");
        customShow.Should().NotContain("plan.CanRename");
        customShow.Should().NotContain("plan.SelectedShow?.Name ?? string.Empty");

        var chartEx = ReadHostSource("ChartExSeriesLayoutDialog.cs");
        chartEx.Should().Contain("ChartExSeriesLayoutDialogSession");
        chartEx.Should().NotContain("BuildOptions(");
        chartEx.Should().NotContain("BuildLayoutChoices(");
        chartEx.Should().NotContain("BuildCommitPlan(");
        chartEx.Should().NotContain("FormatLayoutLabel(");
    }

    private static string ReadHostSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory(
            "FreeP.slnx");
        return File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            fileName));
    }
}

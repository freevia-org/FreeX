using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class DialogTailDedupSourceTests
{
    [Fact]
    public void WpfDialogTailDelegatesPortableWorkflowsToPresentationSessions()
    {
        var motion = ReadHostSource("MotionPathEditorDialog.cs");
        motion.Should().Contain("MotionPathEditorDialogSession");
        motion.Should().Contain("MotionPathEditorRowProjection");
        motion.Should().NotContain("MotionPathEditingPlanner.");
        motion.Should().NotContain("double.TryParse");

        var rotation = ReadHostSource("RotationOptionsDialog.cs");
        rotation.Should().Contain("RotationOptionsDialogSession");
        rotation.Should().NotContain("SelectedShapeIds");
        rotation.Should().NotContain("SetSelectedRotation");
        rotation.Should().NotContain("RotationOptionsPlanner.");

        var slideShow = ReadHostSource("SlideShowSettingsDialog.cs");
        slideShow.Should().Contain("SlideShowSettingsDialogSession");
        slideShow.Should().NotContain("SlideShowSettingsPlanner.");
        slideShow.Should().NotContain("Math.Clamp");
        slideShow.Should().NotContain("uint.TryParse");

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

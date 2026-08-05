using System.IO;

public sealed class DialogTailDedupSourceTests
{
    [Fact]
    public void AvaloniaDialogTailDelegatesPortableWorkflowsToPresentationSessions()
    {
        var motion = ReadSource("MotionPathEditorDialog.cs");
        motion.Should().Contain("MotionPathEditorDialogSession");
        motion.Should().Contain("MotionPathEditorRowProjection");
        motion.Should().NotContain("MotionPathEditingPlanner.");
        motion.Should().NotContain("double.TryParse");

        var rotation = ReadSource("RotationOptionsDialog.cs");
        rotation.Should().Contain("RotationOptionsDialogSession");
        rotation.Should().NotContain("SelectedShapeIds");
        rotation.Should().NotContain("SetSelectedRotation");
        rotation.Should().NotContain("RotationOptionsPlanner.");

        var slideShow = ReadSource("SlideShowSettingsDialog.cs");
        slideShow.Should().Contain("SlideShowSettingsDialogSession");
        slideShow.Should().Contain("SlideShowSettingsDialogSession.ShowTypeOptions");
        slideShow.Should().Contain("SlideShowSettingsDialogSession.CreateInput");
        slideShow.Should().NotContain("SlideShowSettingsPlanner.");
        slideShow.Should().NotContain("new SlideShowSettingsDialogInput");
        slideShow.Should().NotContain("\"Presented by a speaker\"");
        slideShow.Should().NotContain("Math.Clamp");
        slideShow.Should().NotContain("uint.TryParse");

        var customShow = ReadSource("CustomShowDialog.cs");
        customShow.Should().Contain("SlideShowCustomShowDialogSession");
        customShow.Should().Contain("SlideShowCustomShowSessionShowItemPlan");
        customShow.Should().Contain("SlideShowCustomShowSessionSlideItemPlan");
        customShow.Should().NotContain("SlideShowCustomShowPlanner.");
        customShow.Should().NotContain("record CustomShowListItem");
        customShow.Should().NotContain("record CustomShowSlideListItem");

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

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var path = Path.Combine(
                new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException(
            $"Could not find repository file: {Path.Combine(parts)}");
    }
}

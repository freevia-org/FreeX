using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ChartAxisDisplayOptionsDialogSessionOwnershipTests
{
    [Theory]
    [InlineData("ChartAxisOptionsDialog.cs", "ChartAxisOptionsDialogSession")]
    [InlineData("ChartDisplayOptionsDialog.cs", "ChartDisplayOptionsDialogSession")]
    public void WpfDialogsOwnOnlyNativeProjectionFeedbackAndLifecycle(
        string fileName,
        string sessionType)
    {
        var source = ReadHostSource(fileName);

        source.Should().Contain($"new {sessionType}(editor");
        source.Should().Contain("_session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()))");
        source.Should().Contain("_session.Submit(ReadInput())");
        source.Should().Contain("ReadInput()");
        source.Should().Contain("ChartOptionsDialogChrome.");
        source.Should().NotContain("private readonly EditingSession");
        source.Should().NotContain("private readonly ChartAxisOptionsPlanner");
        source.Should().NotContain("private readonly ChartDisplayOptionsPlanner");
        source.Should().NotContain("_planner");
        source.Should().NotContain("_editor");
        source.Should().NotContain("UpdatePlannerFromControls");
        source.Should().NotContain("ChartDialogOptionProjection.");
        source.Should().NotContain("ApplyChart");
        source.Should().NotContain("TryParse(");
        source.Should().NotContain("NumberStyles");
    }

    private static string ReadHostSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", fileName));
    }
}

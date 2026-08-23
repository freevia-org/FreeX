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
        var testSupport = ReadChartOptionsTestSupport();

        source.Should().Contain($"new {sessionType}(editor");
        source.Should().Contain($"ChartOptionsDialogHost<{sessionType}>");
        source.Should().Contain("session.Submit(session.BuildInput(values))");
        source.Should().Contain(": base(session, session.BuildDialogPlan(");
        source.Should().NotContain("ReadInput()");
        source.Should().NotContain("ChartOptionsDialogChrome.");
        source.Should().NotContain("DialogMessageHelper.ShowWarning(");
        source.Should().NotContain("ForTests");
        testSupport.Should().Contain($"partial class {Path.GetFileNameWithoutExtension(fileName)}");
        testSupport.Should().Contain("BuildCommitPlanForTests()");
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

    private static string ReadChartOptionsTestSupport()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(
            root,
            "freep",
            "TestSupport",
            "HostAccess.Wpf",
            "ChartOptionsDialogs.TestAccess.cs"));
    }
}

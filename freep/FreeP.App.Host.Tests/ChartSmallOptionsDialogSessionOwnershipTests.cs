using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ChartSmallOptionsDialogSessionOwnershipTests
{
    [Theory]
    [MemberData(nameof(SessionOwnedDialogs))]
    public void WpfDialogLeavesPortableDecisionsAndDispatchWithSession(
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
        source.Should().NotContain("ChartDialogOptionProjection.");
        source.Should().NotContain("_planner");
        source.Should().NotContain("_editor");
        source.Should().NotContain("UpdatePlannerFromControls");
        source.Should().NotContain("ApplyChart");
        source.Should().NotContain("TryParse(");
        source.Should().NotContain("NumberStyles");
    }

    public static TheoryData<string, string> SessionOwnedDialogs => new()
    {
        { "Chart3DViewOptionsDialog.cs", "Chart3DViewOptionsDialogSession" },
        { "ChartBubbleOptionsDialog.cs", "ChartBubbleOptionsDialogSession" },
        { "ChartPlotStyleOptionsDialog.cs", "ChartPlotStyleOptionsDialogSession" },
        { "ChartProtectionOptionsDialog.cs", "ChartProtectionOptionsDialogSession" },
        { "ChartTextOptionsDialog.cs", "ChartTextOptionsDialogSession" },
    };

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

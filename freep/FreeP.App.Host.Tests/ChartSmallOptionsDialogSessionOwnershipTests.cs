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

        source.Should().Contain($"new {sessionType}(editor");
        source.Should().Contain("_session.BuildCommitPlan(ReadInput())");
        source.Should().Contain("_session.Submit(ReadInput())");
        source.Should().Contain("ReadInput()");
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
}

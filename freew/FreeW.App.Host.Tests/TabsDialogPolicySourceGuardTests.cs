using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class TabsDialogPolicySourceGuardTests
{
    [Fact]
    public void TabsDialog_DelegatesTabStopPolicyToPresentationSession()
    {
        var source = ReadHostSource("TabsDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("TabsDialogSession");
        source.Should().Contain("_session.State");
        source.Should().Contain("_session.Alignments");
        source.Should().Contain("_session.Leaders");
        source.Should().Contain("_session.ProjectSelection(");
        source.Should().Contain("new TabsDialogSetRequest(");
        source.Should().Contain("_session.SetStop(");
        source.Should().Contain("_session.ClearStop(");
        source.Should().Contain("_session.ClearAll(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().Contain("TabsDialogResult?");
        source.Should().NotContain("TabsDialogPlanner.TrySetStop(");
        source.Should().NotContain("TabsDialogPlanner.TryBuildResult(");
        source.Should().NotContain("private static readonly string[] Alignments");
        source.Should().NotContain("private static readonly string[] Leaders");
        source.Should().NotContain("new TabStop(");
        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("NumberStyles.");
        source.Should().NotContain("OrderBy(");
        source.Should().NotContain(".Sort(");
        source.Should().NotContain("Math.Abs(");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}

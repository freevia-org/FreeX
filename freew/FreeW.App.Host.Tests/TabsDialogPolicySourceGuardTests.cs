using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class TabsDialogPolicySourceGuardTests
{
    [Fact]
    public void TabsDialog_DelegatesTabStopPolicyToPresentationPlanner()
    {
        var source = ReadHostSource("TabsDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("TabsDialogPlanner.BuildInitialState(");
        source.Should().Contain("TabsDialogPlanner.Alignments");
        source.Should().Contain("TabsDialogPlanner.Leaders");
        source.Should().Contain("TabsDialogPlanner.ProjectSelectedStop(");
        source.Should().Contain("new TabsDialogSetRequest(");
        source.Should().Contain("TabsDialogPlanner.TrySetStop(");
        source.Should().Contain("TabsDialogPlanner.ClearStop(");
        source.Should().Contain("TabsDialogPlanner.ClearAll(");
        source.Should().Contain("TabsDialogPlanner.TryBuildResult(");
        source.Should().Contain("TabsDialogResult?");
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

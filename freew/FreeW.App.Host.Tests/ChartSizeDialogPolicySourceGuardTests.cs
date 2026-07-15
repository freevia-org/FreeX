using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ChartSizeDialogPolicySourceGuardTests
{
    [Fact]
    public void ChartSizeDialog_DelegatesPointFormattingAndValidationToPresentationPlanner()
    {
        var source = ReadHostSource("ChartSizeDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("ChartSizeDialogPlanner.BuildInitialState(");
        source.Should().Contain("new ChartSizeDialogInput(");
        source.Should().Contain("ChartSizeDialogPlanner.TryBuildResult(");
        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("NumberStyles.");
        source.Should().NotContain("widthPt.ToString(\"0.##\"");
        source.Should().NotContain("heightPt.ToString(\"0.##\"");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}

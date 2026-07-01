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
        var path = Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}

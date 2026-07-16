using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class FreeWRibbonCommandMessageSourceTests
{
    [Fact]
    public void RibbonCommands_RouteMessagesThroughDialogMessageHelper()
    {
        var source = ReadRibbonCommandsSource();

        source.Should().Contain("DialogMessageHelper.ShowInfo(");
        source.Should().Contain("DialogMessageHelper.ShowError(");
        source.Should().Contain("\"Select some text first, then choose Change Case.\"");
        source.Should().Contain("\"Could not insert the file:");
        source.Should().Contain("\"Could not insert the image:");
        source.Should().Contain("\"Could not capture the screen clip:");
        source.Should().Contain("\"Could not compare the documents:");
        source.Should().Contain("\"Could not combine the documents:");
        source.Should().Contain("\"Mail Merge\"");
        source.Split("MessageBox.Show(").Length.Should().Be(2,
            "the source-management conflict prompt is the only command requiring a three-way choice");
        source.Should().Contain("SourceManagementDialogPlanner.SourceConflictDialogTitle");
    }

    private static string ReadRibbonCommandsSource()
    {
        var path = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs");
        return File.ReadAllText(path);
    }

}

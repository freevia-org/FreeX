using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MediaDialogParitySourceTests
{
    [Theory]
    [InlineData("ChartTitleDialog.cs", "ChartTitleDialogPlanner.BuildResult(")]
    [InlineData("ChartAxisTitlesDialog.cs", "ChartAxisTitlesDialogPlanner.BuildResult(")]
    [InlineData("InsertChartDialog.cs", "InsertChartDialogPlanner.BuildInitialState(")]
    [InlineData("InsertChartDialog.cs", "InsertChartDialogPlanner.TryBuildResult(")]
    [InlineData("InsertSmartArtDialog.cs", "SmartArtDialogPlanner.BuildInitialState(")]
    [InlineData("InsertSmartArtDialog.cs", "SmartArtDialogPlanner.TryBuildResult(")]
    [InlineData("InsertSmartArtDialog.cs", "SmartArtDialogPlanner.NodeTextLabel")]
    public void WpfMediaDialogsUseSharedPresentationPolicies(string fileName, string call)
    {
        ReadHostSource(fileName).Should().Contain(call);
    }

    [Theory]
    [InlineData("MediaDialogParity.cs", "ImageAdjustDialogPlanner.BuildInitialState(")]
    [InlineData("MediaDialogParity.cs", "ImagePositionDialogPlanner.BuildInitialState(")]
    [InlineData("MediaDialogParity.cs", "ChartSizeDialogPlanner.BuildInitialState(")]
    [InlineData("MediaDialogParity.cs", "InsertChartDialogPlanner.BuildInitialState(")]
    [InlineData("MediaDialogParity.cs", "SmartArtDialogPlanner.TryBuildResult(")]
    [InlineData("MediaDialogParity.cs", "SmartArtDialogPlanner.NodeTextLabel")]
    [InlineData("IconPickerDialog.cs", "IconPickerDialogPlanner.Filter(")]
    public void AvaloniaMediaDialogsUseSharedPresentationPolicies(string fileName, string call)
    {
        ReadAvaloniaSource(fileName).Should().Contain(call);
    }

    [Fact]
    public void AvaloniaMediaDialogSourcesDoNotReachIntoForbiddenShellOwners()
    {
        var source = string.Join("\n", [
            ReadAvaloniaSource("MediaDialogParity.cs"),
            ReadAvaloniaSource("IconPickerDialog.cs"),
        ]);

        source.Should().NotContain("MainWindow");
        source.Should().NotContain("FreeWAvaloniaRibbonCommands");
        source.Should().NotContain("Backstage");
    }

    private static string ReadHostSource(string fileName) =>
        File.ReadAllText(Path.Combine(Workspace(), "freew", "FreeW.App.Host", fileName));

    private static string ReadAvaloniaSource(string fileName) =>
        File.ReadAllText(Path.Combine(Workspace(), "freew", "FreeW.App.Avalonia", fileName));

    private static string Workspace() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
}

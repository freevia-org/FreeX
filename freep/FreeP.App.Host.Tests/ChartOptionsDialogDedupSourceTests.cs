using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ChartOptionsDialogDedupSourceTests
{
    [Fact]
    public void WpfChartOptionDialogsDelegateDecisionsAndChrome()
    {
        foreach (var fileName in ChartOptionDialogFiles)
        {
            var source = ReadHostSource(fileName);
            (source.Contains("ChartDialogOptionProjection.", StringComparison.Ordinal)
                || source.Contains("OptionsDialogSession", StringComparison.Ordinal))
                .Should().BeTrue(fileName);
            source.Should().Contain("ChartOptionsDialogChrome.", fileName);
            source.Should().NotContain("NumberStyles.", fileName);
            source.Should().NotContain("double.TryParse", fileName);
            source.Should().NotContain("int.TryParse", fileName);
            source.Should().NotContain("new Label { Content = label", fileName);
            source.Should().NotContain("new Button { Content = surface.OkLabel", fileName);
        }
    }

    [Fact]
    public void WpfChartOptionChromeRetainsEstablishedMetrics()
    {
        var source = ReadHostSource("ChartOptionsDialogChrome.cs");

        source.Should().Contain("Margin = new Thickness(0, 0, 0, 8)");
        source.Should().Contain("MinWidth = 80");
        source.Should().Contain("Margin = new Thickness(4)");
        source.Should().Contain("IsDefault = true");
        source.Should().Contain("IsCancel = true");
    }

    private static readonly string[] ChartOptionDialogFiles =
    [
        "Chart3DViewOptionsDialog.cs",
        "ChartAreaOptionsDialog.cs",
        "ChartAxisOptionsDialog.cs",
        "ChartBubbleOptionsDialog.cs",
        "ChartDataTableOptionsDialog.cs",
        "ChartDisplayOptionsDialog.cs",
        "ChartLayoutOptionsDialog.cs",
        "ChartPieOptionsDialog.cs",
        "ChartPlotStyleOptionsDialog.cs",
        "ChartPointOptionsDialog.cs",
        "ChartProtectionOptionsDialog.cs",
        "ChartSeriesOptionsDialog.cs",
        "ChartTextOptionsDialog.cs",
    ];

    private static string ReadHostSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", fileName));
    }
}

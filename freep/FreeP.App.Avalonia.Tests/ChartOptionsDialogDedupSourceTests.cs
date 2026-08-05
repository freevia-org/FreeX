using System.IO;

public sealed class ChartOptionsDialogDedupSourceTests
{
    [Fact]
    public void AvaloniaChartOptionDialogsDelegateDecisionsAndChrome()
    {
        foreach (var fileName in ChartOptionDialogFiles)
        {
            var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", fileName));
            source.Should().Contain("ChartDialogOptionProjection.", fileName);
            source.Should().Contain("ChartOptionsDialogChrome.", fileName);
            source.Should().NotContain("NumberStyles.", fileName);
            source.Should().NotContain("double.TryParse", fileName);
            source.Should().NotContain("int.TryParse", fileName);
            source.Should().NotContain("AvaloniaCompactDialogChromeStyle DialogChromeStyle", fileName);
            source.Should().NotContain("private static Button MakeButton", fileName);
            source.Should().NotContain("new TextBlock { Text = label", fileName);
        }
    }

    [Fact]
    public void AvaloniaChartOptionChromeRetainsEstablishedMetrics()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "ChartOptionsDialogChrome.cs"));

        source.Should().Contain("Spacing = 8");
        source.Should().Contain("Margin = new Thickness(0, 12, 0, 0)");
        source.Should().Contain("Margin = new Thickness(0, 0, 8, 0)");
        source.Should().Contain("MinWidth = 80");
        source.Should().Contain("isDefault: true");
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

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(parts)}");
    }
}

using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaChartQuickCommandSourceTests
{
    [Fact]
    public void ChartFormatTextTabQuickCommands_UseSharedCatalogAndPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatTextTabs.cs"));

        source.Should().Contain("ChartQuickCommandCatalog.ComboSeries");
        source.Should().Contain("ChartQuickCommandCatalog.ChartTitleColor");
        source.Should().Contain("ChartQuickCommandCatalog.DataLabelTextColor");
        source.Should().Contain("ChartQuickCommandCatalog.SeriesDash");
        source.Should().Contain("ChartQuickCommandCatalog.SeriesMarkerSize");
        source.Should().Contain("private void ExecuteChartQuickCommand(");
        source.Should().Contain("ChartQuickCommandPlanner.CanApply(chart, command.Command)");
        source.Should().Contain("ChartQuickCommandPlanner.Plan(chart, command.Command)");
        source.Should().Contain("ChartWorkflowUnsupportedStatus(ChartWorkflowCommandCatalog.ComboChart)");
        source.Should().Contain("ChartWorkflowUnsupportedStatus(ChartWorkflowCommandCatalog.FormatDataSeries)");
        source.Should().Contain("ChartQuickUnsupportedStatus(command)");

        source.Should().NotContain("ChartQuickFormatCycler.");
        source.Should().NotContain("ChartTypeSupport.GetDataSeriesCount");
        source.Should().NotContain("UiText.Get(\"ChartLoc_ComboChartsNeed\")");
        source.Should().NotContain("UiText.Get(\"ChartLoc_NoDataSeriesToFormat\")");
        source.Should().NotContain("UiText.Get(\"ChartLoc_MarkersAvailableOn\")");
        source.Should().NotContain("new ChartLayoutOptions(");
        source.Should().NotContain("\"Chart Title Color\"");
        source.Should().NotContain("\"Combo Chart Series\"");
        source.Should().NotContain("\"Series Dash\"");
    }

    [Fact]
    public void ComboChartRibbonRoute_MatchesWpfImmediateToggle()
    {
        var contextualSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ContextualTabs.cs"));
        var quickSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatTextTabs.cs"));

        contextualSource.Should().Contain("[\"chartDesign.comboChart\"] = CycleChartCombo");
        contextualSource.Should().NotContain("[\"chartDesign.comboChart\"] = () => RunGuarded(ShowChartComboDialog)");
        quickSource.Should().Contain("private void CycleChartCombo()");
        quickSource.Should().Contain("ChartQuickCommandCatalog.ComboToggle");
    }

    [Fact]
    public void SeriesColorRibbonRoute_MatchesWpfFullSeriesFormatDialog()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ContextualTabs.cs"));

        source.Should().Contain("[\"chartFormat.seriesColor\"] = () => RunGuarded(ShowChartSeriesFormatDialog)");
        source.Should().Contain("[\"chartFormat.seriesWidth\"] = () => RunGuarded(ShowChartSeriesFormatDialog)");
        source.Should().NotContain("[\"chartFormat.seriesColor\"] = () => RunGuarded(ShowChartSeriesColorDialog)");
        source.Should().Contain("ChartSeriesFormatPlanner");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}

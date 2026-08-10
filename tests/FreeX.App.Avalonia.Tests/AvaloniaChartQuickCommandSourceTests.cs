using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaChartQuickCommandSourceTests
{
    [Fact]
    public void ChartFormatTextTabQuickCommands_UseSharedCatalogAndPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatTextTabs.cs"));
        var workflowSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "Charts",
            "Editing",
            "ChartCommandWorkflowPlanner.cs"));

        source.Should().Contain("ChartQuickCommandCatalog.ComboSeries");
        source.Should().Contain("ChartQuickCommandCatalog.ChartTitleColor");
        source.Should().Contain("ChartQuickCommandCatalog.DataLabelTextColor");
        source.Should().Contain("ChartQuickCommandCatalog.SeriesDash");
        source.Should().Contain("ChartQuickCommandCatalog.SeriesMarkerSize");
        source.Should().Contain("ChartQuickCommandCatalog.SecondaryAxisSeries");
        source.Should().Contain("private void ExecuteChartQuickCommand(");
        source.Should().Contain("ChartCommandWorkflowPlanner.PlanQuickCommand(");
        source.Should().NotContain("ChartQuickCommandPlanner.CanApply(");
        source.Should().NotContain("ChartQuickCommandPlanner.Plan(");
        workflowSource.Should().Contain("ChartQuickCommandPlanner.CanApply(chart, command.Command)");
        workflowSource.Should().Contain("ChartQuickCommandPlanner.Plan(chart, command.Command)");
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

    [Fact]
    public void SecondaryAxisSeriesRibbonRoute_UsesWpfSharedQuickCommand()
    {
        var contextualSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ContextualTabs.cs"));
        var quickSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatTextTabs.cs"));
        var adapterSource = File.ReadAllText(RepoFile(
            "src", "FreeX.App.Presentation", "Ribbon", "FreeXRibbonCommandIdentityCatalog.cs"));

        contextualSource.Should().Contain("[\"chartDesign.secondaryAxisSeries\"] = CycleChartSecondaryAxisSeries");
        quickSource.Should().Contain("private void CycleChartSecondaryAxisSeries()");
        quickSource.Should().Contain("ChartQuickCommandCatalog.SecondaryAxisSeries");
        quickSource.Should().Contain("MainWindowMessage_ChartSecondaryAxisUnsupported");
        adapterSource.Should().Contain("[\"chartDesign.secondaryAxisSeries\"] = \"Secondary Axis Series\"");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}

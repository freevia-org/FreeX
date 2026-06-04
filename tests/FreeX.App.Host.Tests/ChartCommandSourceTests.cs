using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class ChartCommandSourceTests
{
    [Theory]
    [InlineData("Recommended Charts", "Recommended Charts", "RC", "InsertChartPickerBtn_Click")]
    [InlineData("Column Chart", "Column", "CC", "ChartColumnMenuItem_Click")]
    [InlineData("Stacked Column Chart", "Stack Col", "SC", "ChartStackedColumnMenuItem_Click")]
    [InlineData("100% Stacked Column Chart", "100% Col", "PC", "ChartPercentStackedColumnMenuItem_Click")]
    [InlineData("Bar Chart", "Bar", "BC", "ChartBarMenuItem_Click")]
    [InlineData("Line Chart", "Line", "LC", "ChartLineMenuItem_Click")]
    [InlineData("Pie Chart", "Pie", "PY", "ChartPieMenuItem_Click")]
    [InlineData("Doughnut Chart", "Doughnut", "DO", "ChartDoughnutMenuItem_Click")]
    [InlineData("Scatter Chart", "Scatter", "SX", "ChartScatterMenuItem_Click")]
    [InlineData("Stock Chart", "Stock", "ST", "ChartStockMenuItem_Click")]
    public void InsertChartCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ReadMainWindowXaml().ExtractButtonElementByInvariantCommandName(title);

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Treemap Chart", "Treemap", "T7", "ChartTreemapMenuItem_Click")]
    [InlineData("Sunburst Chart", "Sunburst", "SU", "ChartSunburstMenuItem_Click")]
    [InlineData("Histogram Chart", "Histogram", "HI", "ChartHistogramMenuItem_Click")]
    [InlineData("Pareto Chart", "Pareto", "PA", "ChartParetoMenuItem_Click")]
    [InlineData("Box and Whisker Chart", "Box Plot", "BW", "ChartBoxAndWhiskerMenuItem_Click")]
    [InlineData("Waterfall Chart", "Waterfall", "WF", "ChartWaterfallMenuItem_Click")]
    [InlineData("Funnel Chart", "Funnel", "FU", "ChartFunnelMenuItem_Click")]
    public void AdvancedChartCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ReadMainWindowXaml().ExtractButtonElementByInvariantCommandName(title);

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void ChartHandlers_RouteThroughExpectedDialogsCommandsAndDeferredPath()
    {
        var source = ReadHostSourceFile("MainWindow.ChartCommands.cs");

        source.Should().Contain("private void InsertChartPickerBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new InsertChartDialog { Owner = this }");
        source.Should().Contain("InsertChartOfType(dialog.Result.ChartType)");
        source.Should().Contain("private void InsertChartOfType(ChartType type)");
        source.Should().Contain("ChartAuthoringPlanner.CanAuthor(type)");
        source.Should().Contain("ShowDeferredChartFamilyMessage();");
        source.Should().Contain("new AddChartCommand(_currentSheetId, currentRange, type, \"Chart\")");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ChartFamilyDeferred\")");
        source.Should().Contain("private void ChangeChartTypeBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new ChangeChartTypeDialog(chart.Type)");
        source.Should().Contain("new ChangeChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType)");
        source.Should().Contain("private void SelectChartDataSourceBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new SelectDataSourceDialog(");
        source.Should().Contain("new ChangeChartSourceCommand(");
        source.Should().Contain("private void MoveChartBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new MoveChartDialog(currentSheet.Name)");
    }

    [Fact]
    public void MapChartCommand_IsNotSurfacedAsDeferredRibbonButton()
    {
        var xaml = ReadMainWindowXaml();
        var source = ReadHostSourceFile("MainWindow.ChartCommands.cs");

        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Map Chart\"");
        xaml.Should().NotContain("Click=\"DeferredChartFamilyMenuItem_Click\"");
        xaml.Should().NotContain("local:RibbonTooltip.KeyTip=\"MP\"");

        source.Should().Contain("private void ShowDeferredChartFamilyMessage() =>");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ChartFamilyDeferred\")");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ChartFamilyDeferredTitle\")");
    }

}

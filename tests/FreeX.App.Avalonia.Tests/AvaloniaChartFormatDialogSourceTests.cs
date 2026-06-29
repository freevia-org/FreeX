using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaChartFormatDialogSourceTests
{
    [Fact]
    public void ChartContextualTabs_UseSharedWorkflowCommandDescriptorsForCoreDialogs()
    {
        var chartTabsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTabs.cs"));
        var chartRemainingDialogsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartRemainingDialogs.cs"));
        var combined = chartTabsSource + Environment.NewLine + chartRemainingDialogsSource;

        combined.Should().Contain("private bool TryGetSelectedChart(ChartWorkflowCommandDescriptor command");
        combined.Should().Contain("private void ApplyChartLayout(ChartWorkflowCommandDescriptor command");
        combined.Should().Contain("ChartWorkflowCommandCatalog.ChangeChartType");
        combined.Should().Contain("ChartWorkflowCommandCatalog.SelectDataSource");
        combined.Should().Contain("ChartWorkflowCommandCatalog.MoveChart");
        combined.Should().Contain("ChartWorkflowCommandCatalog.FormatChartArea");
        combined.Should().NotContain("TryGetSelectedChart(\"Change Chart Type\"");
        combined.Should().NotContain("TryGetSelectedChart(\"Select Data\"");
        combined.Should().NotContain("TryGetSelectedChart(\"Move Chart\"");
        combined.Should().NotContain("TryGetSelectedChart(\"Format Chart Area\"");
        combined.Should().NotContain("ApplyChartLayout(\"Format Chart Area\"");
    }

    [Fact]
    public void DataLabelsDialog_UsesSharedDescriptorAndFullPlannerSurface()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));

        source.Should().Contain("ChartDataLabelsPlanner.GetDialogField(fieldId)");
        source.Should().Contain("ChartDataLabelsPlanner.GetLabelOptionsSection()");
        source.Should().Contain("ChartDataLabelsPlanner.GetStyleSection()");
        source.Should().Contain("ChartDataLabelsPlanner.TryParseDialogInput(");
        source.Should().Contain("MakeDescriptorCheck(ChartDataLabelsDialogFieldId.Callouts");
        source.Should().Contain("MakeDescriptorNumberBox(");
        source.Should().Contain("MakeColorButton(ChartDataLabelsDialogFieldId.FillColor");
        source.Should().NotContain("UiText.Get(\"ChartDataLabels_Show\")");
        source.Should().NotContain("UiText.Get(\"ChartDataLabels_ContainsLabel\")");
    }

    [Fact]
    public void AxisDialog_UsesSharedDescriptorAndPlannerParser()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));

        source.Should().Contain("ChartAxisPlanner.GetDialogField(fieldId)");
        source.Should().Contain("ChartAxisPlanner.TryParseDialogInput(");
        source.Should().Contain("MakeAxisDescriptorCheck(ChartAxisDialogFieldId.LogScale");
        source.Should().Contain("MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.Minimum");
        source.Should().Contain("MakeAxisDescriptorLabel(ChartAxisDialogFieldId.NumberFormat");
        source.Should().NotContain("AutomationProperties.SetName(minimumBox, \"Axis minimum\")");
        source.Should().NotContain("UiText.Get(\"ChartAxis_MinimumLabel\")");
        source.Should().NotContain("UiText.Get(\"ChartAxis_ShowMajorGridlines\")");
    }

    [Fact]
    public void SeriesDialog_UsesSharedDescriptorAndPlannerParser()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));

        source.Should().Contain("ChartSeriesFormatPlanner.GetDialogField(fieldId)");
        source.Should().Contain("ChartSeriesFormatPlanner.GetSeriesOptionsSection()");
        source.Should().Contain("ChartSeriesFormatPlanner.GetFillLineSection()");
        source.Should().Contain("ChartSeriesFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("MakeSeriesColorButton(ChartSeriesFormatDialogFieldId.FillColor");
        source.Should().Contain("MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.DashStyle");
        source.Should().NotContain("AutomationProperties.SetName(seriesCombo, \"Series\")");
        source.Should().NotContain("UiText.Get(\"ChartSeries_FillAndLineLabel\")");
    }

    [Fact]
    public void TrendlineDialog_UsesSharedDescriptorAndPlannerParser()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));

        source.Should().Contain("ChartTrendlinePlanner.GetDialogField(fieldId)");
        source.Should().Contain("ChartTrendlinePlanner.GetOptionsSection()");
        source.Should().Contain("ChartTrendlinePlanner.GetLineSection()");
        source.Should().Contain("ChartTrendlinePlanner.TryParseDialogInput(");
        source.Should().Contain("MakeTrendlineColorButton(ChartTrendlineDialogFieldId.LineColor");
        source.Should().Contain("MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId.DashStyle");
        source.Should().NotContain("TryParseIntInRange(periodBox.Text, ChartTrendlinePlanner.MinPeriod");
        source.Should().NotContain("UiText.Get(\"ChartTrendline_Show\")");
    }

    [Fact]
    public void ErrorBarsDialog_UsesSharedDescriptorAndPlannerParser()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));

        source.Should().Contain("ChartErrorBarsPlanner.GetDialogField(fieldId)");
        source.Should().Contain("ChartErrorBarsPlanner.GetErrorAmountSection()");
        source.Should().Contain("ChartErrorBarsPlanner.TryParseDialogInput(");
        source.Should().Contain("MakeErrorBarsDescriptorCheck(ChartErrorBarsDialogFieldId.ShowErrorBars");
        source.Should().Contain("MakeErrorBarsDescriptorLabel(ChartErrorBarsDialogFieldId.Value");
        source.Should().NotContain("TryParseAutoDouble(valueBox.Text");
        source.Should().NotContain("UiText.Get(\"ChartErrorBars_KindLabel\")");
    }

    [Fact]
    public void TypeFormatDialogs_UseSharedDescriptorsAndPlannerParsers()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTypeFormatDialogs.cs"));

        source.Should().Contain("ChartBarFormatPlanner.GetDialogField(ChartBarFormatDialogFieldId.GapWidth)");
        source.Should().Contain("ChartPieFormatPlanner.GetDialogField(ChartPieFormatDialogFieldId.FirstSliceAngle)");
        source.Should().Contain("ChartBubbleFormatPlanner.GetDialogField(ChartBubbleFormatDialogFieldId.BubbleScale)");
        source.Should().Contain("ChartStockFormatPlanner.GetDialogField(ChartStockFormatDialogFieldId.GapWidth)");
        source.Should().Contain("ChartBarFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("ChartPieFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("ChartBubbleFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("ChartStockFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("TypeFormatDescriptorLabel(");
        source.Should().Contain("ColorPickerButton(ChartStockFormatDialogFieldDescriptor field");

        source.Should().NotContain("UiText.Get(\"ChartFmt_BarTitle\")");
        source.Should().NotContain("UiText.Get(\"ChartFmt_PieTitle\")");
        source.Should().NotContain("UiText.Get(\"ChartFmt_BubbleTitle\")");
        source.Should().NotContain("UiText.Get(\"ChartFmt_StockTitle\")");
        source.Should().NotContain("TryParseIntInRange(gapWidthBox.Text");
        source.Should().NotContain("double.TryParse((thicknessBox.Text");
        source.Should().NotContain("AutomationProperties.SetName(gapWidthBox, \"Up/down bar gap width\")");
        source.Should().NotContain("AutomationProperties.SetAutomationId(gapWidthBox, \"ChartStockFormatGapWidthBox\")");
    }

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}

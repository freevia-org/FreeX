using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaChartFormatDialogSourceTests
{
    [Fact]
    public void ChartDialogFamily_UsesSharedAvaloniaCompactChrome()
    {
        var chartTabsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTabs.cs"));
        var chartRemainingDialogsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartRemainingDialogs.cs"));
        var chartFormatDialogsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));
        var chartTypeFormatDialogsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTypeFormatDialogs.cs"));
        var combined = string.Join(
            Environment.NewLine,
            chartTabsSource,
            chartRemainingDialogsSource,
            chartFormatDialogsSource,
            chartTypeFormatDialogsSource);

        chartFormatDialogsSource.Should().Contain("using Free.Shared.Shell.Avalonia;");
        chartFormatDialogsSource.Should().Contain("AvaloniaCompactDialogChromeStyle ChartDialogChromeStyle");
        chartFormatDialogsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, ChartDialogChromeStyle, width, isDefault);");
        chartFormatDialogsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, ChartDialogChromeStyle);");
        chartFormatDialogsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, ChartDialogChromeStyle);");
        chartFormatDialogsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, ChartDialogChromeStyle);");
        chartFormatDialogsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, ChartDialogChromeStyle);");
        chartFormatDialogsSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(controls, margin);");

        chartFormatDialogsSource.Should().NotContain("button.Height = 24;");
        chartFormatDialogsSource.Should().NotContain("button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);");
        chartFormatDialogsSource.Should().NotContain("tb.BorderBrush = Brush(130, 130, 130);");
        chartFormatDialogsSource.Should().NotContain("cb.BorderBrush = Brush(130, 130, 130);");
        chartRemainingDialogsSource.Should().NotContain("BorderBrush = Brush(130, 130, 130)");
        chartRemainingDialogsSource.Should().NotContain("Height = 24,");
        combined.Should().NotContain("Children = { okButton, cancelButton },");
    }

    [Fact]
    public void ChartContextualTabs_UseSharedWorkflowCommandDescriptorsForCoreDialogs()
    {
        var chartTabsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTabs.cs"));
        var chartRemainingDialogsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartRemainingDialogs.cs"));
        var chartFormatDialogsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));
        var combined = chartTabsSource + Environment.NewLine + chartRemainingDialogsSource;

        combined.Should().Contain("private bool TryGetSelectedChart(ChartWorkflowCommandDescriptor command");
        combined.Should().Contain("private void ApplyChartLayout(ChartWorkflowCommandDescriptor command");
        combined.Should().Contain("ChartWorkflowCommandCatalog.ChangeChartType");
        combined.Should().Contain("dialog.Width = ChartTypeChangePlanner.DialogWidth;");
        combined.Should().Contain("dialog.Height = ChartTypeChangePlanner.DialogHeight;");
        combined.Should().Contain("dialog.SizeToContent = SizeToContent.Manual;");
        combined.Should().Contain("ChartTypeChangePlanner.PickerCategoryColumnWidth");
        combined.Should().Contain("ChartTypeChangePlanner.PickerSubtypeColumnWidth");
        combined.Should().Contain("ChartTypeChangePlanner.PickerPreviewWidth");
        combined.Should().Contain("ChartTypeChangePlanner.PickerCategoryWidth");
        combined.Should().Contain("ChartTypeChangePlanner.PickerSubtypeWidth");
        combined.Should().Contain("ChartTypeChangePlanner.PickerListHeight");
        combined.Should().Contain("ChartTypeChangePlanner.PickerButtonWidth");
        combined.Should().Contain("Height = ChartTypeChangePlanner.PickerPanelHeight");
        combined.Should().Contain("bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });");
        combined.Should().Contain("Grid.SetRow(categoryList, 1);");
        combined.Should().Contain("Grid.SetRow(subtypeGallery, 1);");
        combined.Should().Contain("Grid.SetRowSpan(preview, 2);");
        combined.Should().Contain("Margin = new Thickness(0, 0, 0, 6),");
        combined.Should().Contain("Margin = new Thickness(0, 0, 0, 8),");
        combined.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, ChartDialogChromeStyle);");
        combined.Should().Contain("ConfigureChartDialogKeyboardLifecycle(dialog, subtypeGallery);");
        chartFormatDialogsSource.Should().Contain("AutomationProperties.SetName(okButton");
        chartFormatDialogsSource.Should().Contain("AutomationProperties.SetName(cancelButton");
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
    public void SelectDataSourceDialog_WiresSwitchRowColumnThroughToCommand()
    {
        var chartTabsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTabs.cs"));

        // The Switch Row/Column checkbox must reflect the chart's current orientation when the
        // dialog opens and reach ChangeChartSourceCommand on confirm — not be a silent no-op.
        chartTabsSource.Should().Contain("chart.SeriesInRows);");
        chartTabsSource.Should().Contain("bool switchRowColumn = false)");
        chartTabsSource.Should().Contain("CreateChartCheckBox(StripDisplayMnemonic(UiText.Get(switchField.LabelResourceKey)), switchRowColumn)");
        chartTabsSource.Should().Contain("switchRowColumnCheck.IsChecked == true));");
        chartTabsSource.Should().Contain("seriesInRows: choice.SwitchRowColumn));");
    }

    [Fact]
    public void FormatChartAreaDialog_UsesSharedDescriptorAndPlannerParser()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartRemainingDialogs.cs"));

        source.Should().Contain("ChartAreaFormatPlanner.GetDialogField(fieldId)");
        source.Should().Contain("ChartAreaFormatPlanner.GetFillLineSection()");
        source.Should().Contain("ChartAreaFormatPlanner.GetLegendSection()");
        source.Should().Contain(".GetLegendPositionChoices()");
        source.Should().Contain("ChartAreaFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("dialog.Width = ChartAreaFormatPlanner.DialogWidth;");
        source.Should().Contain("dialog.Height = ChartAreaFormatPlanner.DialogHeight;");
        source.Should().Contain("MakeAreaDescriptorCheck(ChartAreaFormatDialogFieldId.ShowLegend");
        source.Should().Contain("MakeAreaDescriptorLabel(ChartAreaFormatDialogFieldId.LegendFontSize");
        source.Should().Contain("MakeAreaColorButton(ChartAreaFormatDialogFieldId.ChartAreaFillColor");
        source.Should().NotContain("double.TryParse((borderWidthBox.Text");
        source.Should().NotContain("AutomationProperties.SetName(positionCombo, \"Legend position\")");
        source.Should().NotContain("UiText.Get(\"ChartArea_ChartAreaFill\")");
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
        source.Should().Contain("ChartAxisPlanner.GetAxisOptionsSection()");
        source.Should().Contain("ChartAxisPlanner.GetGridlinesSection()");
        source.Should().Contain("ChartAxisPlanner.GetTickMarksSection()");
        source.Should().Contain("ChartAxisPlanner.TryParseDialogInput(");
        source.Should().Contain("MakeAxisDescriptorCheck(ChartAxisDialogFieldId.LogScale");
        source.Should().Contain("MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.Minimum");
        source.Should().Contain("MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.MinorUnit");
        source.Should().Contain("MakeAxisDescriptorLabel(ChartAxisDialogFieldId.NumberFormat");
        source.Should().Contain("MakeAxisColorButton(ChartAxisDialogFieldId.MajorGridlineColor");
        source.Should().Contain("MakeAxisTickStyleCombo(ChartAxisDialogFieldId.MajorTickMarks");
        source.Should().Contain("MakeAxisDescriptorLabel(ChartAxisDialogFieldId.LineThickness");
        source.Should().Contain("FormatOptionalColorText(state.LineColor)");
        source.Should().Contain("MakeAxisDescriptorGroup(tickMarksSection");
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

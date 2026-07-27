using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ChartDialogTests
{
    [Fact]
    public void ChartTrendlineOptionsDialogResult_BuildsLayoutOptions()
    {
        var result = ChartTrendlineOptionsDialog.CreateResult(
            showTrendline: true,
            type: ChartTrendlineType.Polynomial,
            period: 4,
            order: 5,
            showEquation: true,
            showRSquared: true,
            color: new CellColor(80, 90, 100),
            thickness: 2.25,
            dashStyle: ChartLineDashStyle.Dot);

        result.ToOptions().Should().Be(new ChartLayoutOptions(
            ShowLinearTrendline: true,
            TrendlineType: ChartTrendlineType.Polynomial,
            TrendlinePeriod: 4,
            TrendlineOrder: 5,
            ShowTrendlineEquation: true,
            ShowTrendlineRSquared: true,
            TrendlineColor: new CellColor(80, 90, 100),
            TrendlineThickness: 2.25,
            TrendlineDashStyle: ChartLineDashStyle.Dot));
    }

    [Fact]
    public void ChartTrendlineOptionsDialogResult_DelegatesOptionsToSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartTrendlineOptionsDialog.cs");

        source.Should().Contain("public ChartTrendlineInput ToInput()");
        source.Should().Contain("ChartTrendlinePlanner.Plan(ToInput())");
        source.Should().Contain("ChartTrendlinePlanner.Read(chart)");
        source.Should().Contain("ChartTrendlinePlanner.Normalize(new ChartTrendlineInput(");
        source.Should().Contain("ChartTrendlinePlanner.GetTypeChoices()");
        source.Should().Contain("ChartTrendlinePlanner.GetDashStyleChoices()");
        source.Should().Contain("ChartTrendlinePlanner.GetDialogField(id)");
        source.Should().Contain("ChartTrendlinePlanner.GetOptionsSection()");
        source.Should().Contain("ChartTrendlinePlanner.GetLineSection()");
        source.Should().Contain("ChartTrendlinePlanner.TryParseDialogInput(");
        source.Should().NotContain("ShowLinearTrendline: ShowTrendline");
        source.Should().NotContain("TryReadIntInRange(");
        source.Should().NotContain("TryReadOptionalColor(");
        source.Should().NotContain("TryReadClampedDouble(");
        source.Should().NotContain("int.TryParse(");
    }

    [Fact]
    public void ChartTrendlineOptionsDialogOpenedFromKeyboard_FocusesShowTrendlineChoice()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartTrendlineOptionsDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_showBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_showBox);");
    }

    [Fact]
    public void ChartTrendlineOptionsDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartTrendlineOptionsDialog.cs");

        source.Should().Contain("ChartTrendlineDialogParseIssue.Order => (UiText.Get(\"ChartTrendline_InvalidOrderMessage\"), _orderBox)");
        source.Should().Contain("ChartTrendlineDialogParseIssue.Color => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _colorBox)");
        source.Should().Contain("ChartTrendlineDialogParseIssue.Thickness => (UiText.Get(\"ChartTrendline_InvalidWidthMessage\"), _thicknessBox)");
        source.Should().Contain("_ => (UiText.Get(\"ChartTrendline_InvalidPeriodMessage\"), _periodBox)");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartTrendlineDialogParseIssue issue)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("private void ShowInvalidInputWarning(string message, TextBox target)");
    }

    [Fact]
    public void ChartErrorBarsDialogResult_BuildsLayoutOptions()
    {
        var result = ChartErrorBarsDialog.CreateResult(
            showErrorBars: true,
            kind: ChartErrorBarKind.FixedValue,
            direction: ChartErrorBarDirection.Minus,
            value: 7.5,
            endCaps: false);

        result.ToOptions().Should().Be(new ChartLayoutOptions(
            ShowErrorBars: true,
            ErrorBarKind: ChartErrorBarKind.FixedValue,
            ErrorBarDirection: ChartErrorBarDirection.Minus,
            ErrorBarValue: 7.5,
            ErrorBarEndCaps: false));
    }

    [Fact]
    public void ChartErrorBarsDialogResult_DelegatesOptionsAndDefaultsToSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartErrorBarsDialog.cs");

        source.Should().Contain("public ChartErrorBarsInput ToInput()");
        source.Should().Contain("ChartErrorBarsPlanner.Plan(ToInput())");
        source.Should().Contain("ChartErrorBarsPlanner.Read(chart)");
        source.Should().Contain("ChartErrorBarsPlanner.Normalize(new ChartErrorBarsInput(");
        source.Should().Contain("ChartErrorBarsPlanner.GetKindChoices()");
        source.Should().Contain("ChartErrorBarsPlanner.GetDirectionChoices()");
        source.Should().Contain("ChartErrorBarsPlanner.GetDialogField(id)");
        source.Should().Contain("ChartErrorBarsPlanner.GetErrorAmountSection()");
        source.Should().Contain("ChartErrorBarsPlanner.TryParseDialogInput(");
        source.Should().NotContain("Enum.IsDefined");
        source.Should().NotContain("Math.Clamp");
        source.Should().NotContain("TryReadClampedDouble(");
    }

    [Fact]
    public void ChartErrorBarsDialog_FromChart_UsesCurrentSettingsAndClampsValue()
    {
        var chart = new ChartModel
        {
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.Percentage,
            ErrorBarDirection = ChartErrorBarDirection.Plus,
            ErrorBarValue = 5000,
            ErrorBarEndCaps = false
        };

        ChartErrorBarsDialog.FromChart(chart)
            .Should()
            .Be(new ChartErrorBarsDialogResult(
                true,
                ChartErrorBarKind.Percentage,
                ChartErrorBarDirection.Plus,
                1000,
                false));
    }

    [Fact]
    public void ChartErrorBarsDialogOpenedFromKeyboard_FocusesShowErrorBarsChoice()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartErrorBarsDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_showBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_showBox);");
    }

    [Fact]
    public void ChartErrorBarsDialog_ValueEditorExposesAutomationName()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartErrorBarsDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_valueBox, AutomationNameText(ChartErrorBarsDialogFieldId.Value));");
        source.Should().Contain("Field(id).AutomationNameResourceKey");
    }

    [Fact]
    public void ChartErrorBarsDialogInvalidValue_ShowsOwnedWarningAndRefocusesValueBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartErrorBarsDialog.cs");

        source.Should().Contain("ChartErrorBarsParseIssue.Value => (UiText.Get(\"ChartErrorBars_InvalidValueMessage\"), _valueBox)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartErrorBarsParseIssue issue)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
    }

    [Fact]
    public void ChartAxisFormatDialogResult_BuildsAxisSpecificLayoutOptions()
    {
        var yAxis = ChartAxisFormatDialog.CreateResult(
            useXAxis: false,
            minimum: 0,
            maximum: 100,
            majorUnit: 10,
            minorUnit: 5,
            logScale: true,
            numberFormat: ChartDataLabelNumberFormat.Number,
            showMajorGridlines: true,
            showMinorGridlines: false,
            majorGridlineColor: new CellColor(200, 200, 200),
            minorGridlineColor: new CellColor(220, 220, 220),
            gridlineThickness: 1.25,
            majorTickStyle: ChartAxisTickStyle.Cross,
            minorTickStyle: ChartAxisTickStyle.Inside,
            showLabels: true,
            labelTextColor: new CellColor(1, 2, 3),
            labelFontSize: 13,
            labelAngle: 30,
            lineColor: new CellColor(4, 5, 6),
            lineThickness: 2);

        yAxis.ToOptions().Should().Be(new ChartLayoutOptions(
            YAxisMinimum: 0,
            YAxisMaximum: 100,
            YAxisMajorUnit: 10,
            YAxisMinorUnit: 5,
            YAxisLogScale: true,
            YAxisNumberFormat: ChartDataLabelNumberFormat.Number,
            ShowYAxisMajorGridlines: true,
            ShowYAxisMinorGridlines: false,
            YAxisMajorGridlineColor: new CellColor(200, 200, 200),
            YAxisMinorGridlineColor: new CellColor(220, 220, 220),
            YAxisGridlineThickness: 1.25,
            YAxisMajorTickStyle: ChartAxisTickStyle.Cross,
            YAxisMinorTickStyle: ChartAxisTickStyle.Inside,
            ShowYAxisLabels: true,
            YAxisLabelTextColor: new CellColor(1, 2, 3),
            YAxisLabelFontSize: 13,
            YAxisLabelAngle: 30,
            YAxisLineColor: new CellColor(4, 5, 6),
            YAxisLineThickness: 2));
    }

    [Fact]
    public void ChartAxisFormatDialogResult_DelegatesOptionsDefaultsAndValidationToSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartAxisFormatDialog.cs");

        source.Should().Contain("public ChartAxisInput ToInput()");
        source.Should().Contain("ChartAxisPlanner.Plan(ToInput())");
        source.Should().Contain("ChartAxisPlanner.Read(chart, useXAxis)");
        source.Should().Contain("ChartAxisPlanner.Normalize(new ChartAxisInput(");
        source.Should().Contain("ChartAxisPlanner.GetNumberFormatChoices()");
        source.Should().Contain("ChartAxisPlanner.GetTickStyleChoices()");
        source.Should().Contain("ChartAxisPlanner.GetDialogField(id)");
        source.Should().Contain("ChartAxisPlanner.GetAxisOptionsSection()");
        source.Should().Contain("ChartAxisPlanner.GetGridlinesSection()");
        source.Should().Contain("ChartAxisPlanner.GetTickMarksSection()");
        source.Should().Contain("ChartAxisPlanner.TryParseDialogInput(");
        source.Should().NotContain("TryReadNullableDouble(");
        source.Should().NotContain("TryReadOptionalColor(");
        source.Should().NotContain("TryReadPositiveDouble(");
        source.Should().NotContain("TryReadClampedDouble(");
        source.Should().NotContain("ShowPlannerValidationWarning");
        source.Should().NotContain("ClearYAxisBounds: Minimum is null && Maximum is null");
    }

    [Fact]
    public void ChartAxisFormatDialog_FromChart_UsesPlannerNormalizationForDialogDefaults()
    {
        var chart = new ChartModel
        {
            YAxisMinorUnit = -2,
            YAxisGridlineThickness = 99,
            YAxisLabelFontSize = 100,
            YAxisLabelAngle = -120,
            YAxisLineThickness = 0.1,
        };

        var result = ChartAxisFormatDialog.FromChart(chart, useXAxis: false);

        result.MinorUnit.Should().BeNull();
        result.GridlineThickness.Should().Be(ChartAxisPlanner.MaxGridlineThickness);
        result.LabelFontSize.Should().Be(ChartAxisPlanner.MaxLabelFontSize);
        result.LabelAngle.Should().Be(ChartAxisPlanner.MinLabelAngle);
        result.LineThickness.Should().Be(ChartAxisPlanner.MinLineThickness);
    }

    [Fact]
    public void ChartAxisFormatDialogOpenedFromKeyboard_FocusesMinimumBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartAxisFormatDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_minimumBox.Focus();");
        source.Should().Contain("_minimumBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_minimumBox);");
    }

    [Fact]
    public void ChartAxisFormatDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartAxisFormatDialog.cs");

        source.Should().Contain("_ => (UiText.Get(\"ChartAxisFormat_InvalidMinimumMessage\"), _minimumBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.Maximum => (UiText.Get(\"ChartAxisFormat_InvalidMaximumMessage\"), _maximumBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.MajorUnit => (UiText.Get(\"ChartAxisFormat_InvalidMajorUnitMessage\"), _majorUnitBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.MinorUnit => (UiText.Get(\"ChartAxisFormat_InvalidMinorUnitMessage\"), _minorUnitBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.MajorGridlineColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _majorGridColorBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.MinorGridlineColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _minorGridColorBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.GridlineThickness => (UiText.Get(\"ChartAxisFormat_InvalidGridlineWidthMessage\"), _gridlineThicknessBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.LabelTextColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _labelColorBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.LabelFontSize => (UiText.Get(\"ChartAxisFormat_InvalidLabelFontSizeMessage\"), _labelFontSizeBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.LabelAngle => (UiText.Get(\"ChartAxisFormat_InvalidLabelAngleMessage\"), _labelAngleBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.LineColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _lineColorBox)");
        source.Should().Contain("ChartAxisFormatParseIssue.LineThickness => (UiText.Get(\"ChartAxisFormat_InvalidAxisLineWidthMessage\"), _lineThicknessBox)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartAxisFormatParseIssue issue)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
    }

    [Fact]
    public void ChartSeriesFormatDialogResult_ReplacesSelectedSeriesFormat()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: new CellColor(1, 1, 1)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(
            2,
            FillColor: new CellColor(2, 2, 2),
            StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
            NoLine: true));
        var result = ChartSeriesFormatDialog.CreateResult(
            seriesIndex: 2,
            fillColor: new CellColor(10, 20, 30),
            strokeColor: new CellColor(40, 50, 60),
            strokeThickness: 2.5,
            dashStyle: ChartLineDashStyle.Dash,
            markerStyle: ChartMarkerStyle.Diamond,
            markerSize: 9);

        var options = result.ToOptions(chart);

        options.SeriesFormats.Should().NotBeNull();
        options.SeriesFormats!.Should().ContainSingle(format => format.SeriesIndex == 2)
            .Which.Should().Be(new ChartSeriesFormat(
                2,
                FillColor: new CellColor(10, 20, 30),
                StrokeColor: new CellColor(40, 50, 60),
                StrokeThickness: 2.5,
                DashStyle: ChartLineDashStyle.Dash,
                MarkerStyle: ChartMarkerStyle.Diamond,
                MarkerSize: 9,
                NoLine: true));
        options.SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 0);
    }

    [Fact]
    public void ChartSeriesFormatDialogResult_NullDashStyleClearsDashThroughSharedPlanner()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, DashStyle: ChartLineDashStyle.Dash));

        var result = ChartSeriesFormatDialog.CreateResult(
            seriesIndex: 0,
            fillColor: null,
            strokeColor: null,
            strokeThickness: null,
            dashStyle: null,
            markerStyle: null,
            markerSize: null);

        result.ToOptions(chart).SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 0)
            .Which.DashStyle.Should().BeNull();
    }

    [Fact]
    public void ChartSeriesFormatDialogResult_DelegatesOptionsToSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartSeriesFormatDialog.cs");

        source.Should().Contain("public ChartSeriesFormatInput ToInput()");
        source.Should().Contain("ChartSeriesFormatPlanner.Plan(chart, ToInput())");
        source.Should().Contain("ChartSeriesFormatPlanner.ReadDefault(chart)");
        source.Should().Contain("ChartSeriesFormatPlanner.Normalize(new ChartSeriesFormatInput(");
        source.Should().Contain("ChartSeriesFormatPlanner.GetDashStyleChoices()");
        source.Should().Contain("ChartSeriesFormatPlanner.GetMarkerStyleChoices()");
        source.Should().Contain("ChartSeriesFormatPlanner.GetDialogField(id)");
        source.Should().Contain("ChartSeriesFormatPlanner.GetSeriesOptionsSection()");
        source.Should().Contain("ChartSeriesFormatPlanner.GetFillLineSection()");
        source.Should().Contain("ChartSeriesFormatPlanner.TryParseDialogInput(");
        source.Should().NotContain("IndexOfSeriesFormat");
        source.Should().NotContain("TryReadOptionalColor(");
        source.Should().NotContain("TryReadNullablePositiveDouble(");
    }

    [Fact]
    public void ChartSeriesFormatDialogOpenedFromKeyboard_FocusesSeriesSelector()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartSeriesFormatDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_seriesBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_seriesBox);");
    }

    [Fact]
    public void ChartSeriesColorCommand_UsesTheFullSeriesFormatDialog()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ChartCommands.cs");
        var handlerStart = source.IndexOf("private void ChartSeriesColorBtn_Click", StringComparison.Ordinal);
        handlerStart.Should().BeGreaterThanOrEqualTo(0);
        var nextHandler = source.IndexOf("private void ", handlerStart + 1, StringComparison.Ordinal);
        var handler = source[handlerStart..(nextHandler >= 0 ? nextHandler : source.Length)];

        handler.Should().Contain("ShowChartSeriesFormatDialog();");
        handler.Should().NotContain("ShowMoreColorsDialogAsync");
    }

    [Fact]
    public void ChartSeriesFormatDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartSeriesFormatDialog.cs");

        source.Should().Contain("_ => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _fillBox)");
        source.Should().Contain("ChartSeriesFormatParseIssue.StrokeColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _strokeBox)");
        source.Should().Contain("ChartSeriesFormatParseIssue.StrokeThickness => (UiText.Get(\"ChartSeriesFormat_InvalidLineWidthMessage\"), _strokeThicknessBox)");
        source.Should().Contain("ChartSeriesFormatParseIssue.MarkerSize => (UiText.Get(\"ChartSeriesFormat_InvalidMarkerSizeMessage\"), _markerSizeBox)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartSeriesFormatParseIssue issue)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
    }

}

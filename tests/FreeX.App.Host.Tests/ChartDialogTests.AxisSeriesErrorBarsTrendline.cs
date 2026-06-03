using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
    public void ChartTrendlineOptionsDialogOpenedFromKeyboard_FocusesShowTrendlineChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartTrendlineOptionsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_showBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_showBox);");
    }

    [Fact]
    public void ChartTrendlineOptionsDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartTrendlineOptionsDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartTrendline_InvalidPeriodMessage\"), _periodBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartTrendline_InvalidOrderMessage\"), _orderBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _colorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartTrendline_InvalidWidthMessage\"), _thicknessBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartErrorBarsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_showBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_showBox);");
    }

    [Fact]
    public void ChartErrorBarsDialog_ValueEditorExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartErrorBarsDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_valueBox, UiText.Get(\"ChartErrorBars_ValueAutomationName\"));");
    }

    [Fact]
    public void ChartErrorBarsDialogInvalidValue_ShowsOwnedWarningAndRefocusesValueBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartErrorBarsDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartErrorBars_InvalidValueMessage\"), _valueBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
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
    public void ChartAxisFormatDialogOpenedFromKeyboard_FocusesMinimumBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartAxisFormatDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_minimumBox.Focus();");
        source.Should().Contain("_minimumBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_minimumBox);");
    }

    [Fact]
    public void ChartAxisFormatDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartAxisFormatDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidMinimumMessage\"), _minimumBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidMaximumMessage\"), _maximumBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidMajorUnitMessage\"), _majorUnitBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidMinorUnitMessage\"), _minorUnitBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _majorGridColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _minorGridColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidGridlineWidthMessage\"), _gridlineThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _labelColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidLabelFontSizeMessage\"), _labelFontSizeBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidLabelAngleMessage\"), _labelAngleBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _lineColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAxisFormat_InvalidAxisLineWidthMessage\"), _lineThicknessBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void ChartSeriesFormatDialogResult_ReplacesSelectedSeriesFormat()
    {
        var result = ChartSeriesFormatDialog.CreateResult(
            seriesIndex: 2,
            fillColor: new CellColor(10, 20, 30),
            strokeColor: new CellColor(40, 50, 60),
            strokeThickness: 2.5,
            dashStyle: ChartLineDashStyle.Dash,
            markerStyle: ChartMarkerStyle.Diamond,
            markerSize: 9);

        var options = result.ToOptions([
            new ChartSeriesFormat(0, FillColor: new CellColor(1, 1, 1)),
            new ChartSeriesFormat(2, FillColor: new CellColor(2, 2, 2))
        ]);

        options.SeriesFormats.Should().NotBeNull();
        options.SeriesFormats!.Should().ContainSingle(format => format.SeriesIndex == 2)
            .Which.Should().Be(new ChartSeriesFormat(
                2,
                FillColor: new CellColor(10, 20, 30),
                StrokeColor: new CellColor(40, 50, 60),
                StrokeThickness: 2.5,
                DashStyle: ChartLineDashStyle.Dash,
                MarkerStyle: ChartMarkerStyle.Diamond,
                MarkerSize: 9));
        options.SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 0);
    }

    [Fact]
    public void ChartSeriesFormatDialogOpenedFromKeyboard_FocusesSeriesSelector()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartSeriesFormatDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_seriesBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_seriesBox);");
    }

    [Fact]
    public void ChartSeriesFormatDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartSeriesFormatDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _fillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _strokeBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartSeriesFormat_InvalidLineWidthMessage\"), _strokeThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartSeriesFormat_InvalidMarkerSizeMessage\"), _markerSizeBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

}

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
    public void ChartAxisFormatDialog_ReturnsSharedInputThatBuildsAxisSpecificLayoutOptions()
    {
        var yAxis = ChartAxisPlanner.Normalize(new ChartAxisInput(
            UseXAxis: false,
            Minimum: 0,
            Maximum: 100,
            MajorUnit: 10,
            MinorUnit: 5,
            LogScale: true,
            NumberFormat: ChartDataLabelNumberFormat.Number,
            ShowMajorGridlines: true,
            ShowMinorGridlines: false,
            MajorGridlineColor: new CellColor(200, 200, 200),
            MinorGridlineColor: new CellColor(220, 220, 220),
            GridlineThickness: 1.25,
            MajorTickStyle: ChartAxisTickStyle.Cross,
            MinorTickStyle: ChartAxisTickStyle.Inside,
            ShowLabels: true,
            LabelTextColor: new CellColor(1, 2, 3),
            LabelFontSize: 13,
            LabelAngle: 30,
            LineColor: new CellColor(4, 5, 6),
            LineThickness: 2));

        ChartAxisPlanner.Plan(yAxis).Should().Be(new ChartLayoutOptions(
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
    public void ChartAxisFormatDialog_UsesSharedContractDefaultsAndValidation()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartAxisFormatDialog.cs");

        source.Should().Contain("public ChartAxisInput Result { get; private set; }");
        source.Should().Contain("ChartAxisPlanner.Read(chart, useXAxis)");
        source.Should().NotContain("public static ChartAxisInput CreateResult(");
        source.Should().NotContain("public static ChartAxisInput FromChart(");
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
        source.Should().NotContain("ChartAxisFormatDialogResult");
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

        var result = ChartAxisPlanner.Read(chart, useXAxis: false);

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

        source.Should().Contain("var presentation = ChartValidationPresentationPlanner.Describe(issue);");
        source.Should().Contain("ChartAxisDialogFieldId.Maximum => _maximumBox");
        source.Should().Contain("ChartAxisDialogFieldId.MajorUnit => _majorUnitBox");
        source.Should().Contain("ChartAxisDialogFieldId.MinorUnit => _minorUnitBox");
        source.Should().Contain("ChartAxisDialogFieldId.MajorGridlineColor => _majorGridColorBox");
        source.Should().Contain("ChartAxisDialogFieldId.MinorGridlineColor => _minorGridColorBox");
        source.Should().Contain("ChartAxisDialogFieldId.GridlineThickness => _gridlineThicknessBox");
        source.Should().Contain("ChartAxisDialogFieldId.LabelTextColor => _labelColorBox");
        source.Should().Contain("ChartAxisDialogFieldId.LabelFontSize => _labelFontSizeBox");
        source.Should().Contain("ChartAxisDialogFieldId.LabelAngle => _labelAngleBox");
        source.Should().Contain("ChartAxisDialogFieldId.LineColor => _lineColorBox");
        source.Should().Contain("ChartAxisDialogFieldId.LineThickness => _lineThicknessBox");
        source.Should().Contain("presentation.Message.Resolve(UiText.Get, UiText.Format)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartAxisFormatParseIssue issue)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
    }

    [Fact]
    public void ChartSeriesFormatDialog_ReturnsSharedInputThatReplacesSelectedSeriesFormat()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: new CellColor(1, 1, 1)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(
            2,
            FillColor: new CellColor(2, 2, 2),
            StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
            NoLine: true));
        var result = ChartSeriesFormatPlanner.Normalize(new ChartSeriesFormatInput(
            SeriesIndex: 2,
            FillColor: new CellColor(10, 20, 30),
            StrokeColor: new CellColor(40, 50, 60),
            StrokeThickness: 2.5,
            MarkerStyle: ChartMarkerStyle.Diamond,
            MarkerSize: 9,
            DashStyle: ChartLineDashStyle.Dash));

        var options = ChartSeriesFormatPlanner.Plan(chart, result);

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
    public void ChartSeriesFormatDialog_NullDashStyleClearsDashThroughSharedPlanner()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, DashStyle: ChartLineDashStyle.Dash));

        var result = ChartSeriesFormatPlanner.Normalize(new ChartSeriesFormatInput(
            SeriesIndex: 0,
            FillColor: null,
            StrokeColor: null,
            StrokeThickness: null,
            MarkerStyle: null,
            MarkerSize: null,
            DashStyle: null));

        ChartSeriesFormatPlanner.Plan(chart, result).SeriesFormats.Should().ContainSingle(format => format.SeriesIndex == 0)
            .Which.DashStyle.Should().BeNull();
    }

    [Fact]
    public void ChartSeriesFormatDialog_UsesSharedContractAndPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartSeriesFormatDialog.cs");

        source.Should().Contain("public ChartSeriesFormatInput Result { get; private set; }");
        source.Should().Contain("ChartSeriesFormatPlanner.ReadDefault(chart)");
        source.Should().NotContain("public static ChartSeriesFormatInput CreateResult(");
        source.Should().NotContain("public static ChartSeriesFormatInput FromChart(");
        source.Should().Contain("ChartSeriesFormatPlanner.GetDashStyleChoices()");
        source.Should().Contain("ChartSeriesFormatPlanner.GetMarkerStyleChoices()");
        source.Should().Contain("ChartSeriesFormatPlanner.GetDialogField(id)");
        source.Should().Contain("ChartSeriesFormatPlanner.GetSeriesOptionsSection()");
        source.Should().Contain("ChartSeriesFormatPlanner.GetFillLineSection()");
        source.Should().Contain("ChartSeriesFormatPlanner.TryParseDialogInput(");
        source.Should().NotContain("IndexOfSeriesFormat");
        source.Should().NotContain("TryReadOptionalColor(");
        source.Should().NotContain("TryReadNullablePositiveDouble(");
        source.Should().NotContain("ChartSeriesFormatDialogResult");
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

        source.Should().Contain("var presentation = ChartValidationPresentationPlanner.Describe(issue);");
        source.Should().Contain("ChartSeriesFormatDialogFieldId.StrokeColor => _strokeBox");
        source.Should().Contain("ChartSeriesFormatDialogFieldId.StrokeThickness => _strokeThicknessBox");
        source.Should().Contain("ChartSeriesFormatDialogFieldId.MarkerSize => _markerSizeBox");
        source.Should().Contain("presentation.Message.Resolve(UiText.Get, UiText.Format)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartSeriesFormatParseIssue issue)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
    }

}

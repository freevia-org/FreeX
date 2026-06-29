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
    public void ChartAreaLegendDialogResult_BuildsLayoutOptions()
    {
        var result = ChartAreaLegendDialog.CreateResult(
            chartAreaFillColor: new CellColor(250, 250, 250),
            plotAreaFillColor: new CellColor(245, 250, 255),
            plotAreaBorderColor: new CellColor(120, 120, 120),
            plotAreaBorderThickness: 2.25,
            showLegend: true,
            legendPosition: ChartLegendPosition.Bottom,
            legendOverlay: true,
            legendTextColor: new CellColor(40, 40, 40),
            legendFillColor: new CellColor(248, 248, 248),
            legendBorderColor: new CellColor(180, 180, 180),
            legendBorderThickness: 1.25,
            legendFontSize: 11);

        result.ToOptions().Should().Be(new ChartLayoutOptions(
            ChartAreaFillColor: new CellColor(250, 250, 250),
            PlotAreaFillColor: new CellColor(245, 250, 255),
            PlotAreaBorderColor: new CellColor(120, 120, 120),
            PlotAreaBorderThickness: 2.25,
            LegendTextColor: new CellColor(40, 40, 40),
            LegendFillColor: new CellColor(248, 248, 248),
            LegendBorderColor: new CellColor(180, 180, 180),
            LegendBorderThickness: 1.25,
            LegendFontSize: 11,
            LegendPosition: ChartLegendPosition.Bottom,
            LegendOverlay: true,
            ShowLegend: true));
    }

    [Fact]
    public void ChartAreaLegendDialogResult_DelegatesOptionsDefaultsAndParsingToSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartFormatDialogs.cs");

        source.Should().Contain("public ChartAreaFormatInput ToInput()");
        source.Should().Contain("ChartAreaFormatPlanner.Plan(ToInput())");
        source.Should().Contain("ChartAreaFormatPlanner.Read(chart)");
        source.Should().Contain("ChartAreaFormatPlanner.Normalize(new ChartAreaFormatInput(");
        source.Should().Contain("ChartAreaFormatPlanner.GetLegendPositionChoices()");
        source.Should().Contain("ChartAreaFormatPlanner.TryParseDialogInput(");
        source.Should().NotContain("TryReadOptionalColor(");
        source.Should().NotContain("TryReadClampedDouble(");
        source.Should().NotContain("FiniteOrDefault(");
    }

    [Fact]
    public void ChartAreaLegendDialog_FromChart_UsesCurrentSettingsAndClampsNumbers()
    {
        var chart = new ChartModel
        {
            ChartAreaFillColor = new CellColor(1, 2, 3),
            PlotAreaBorderThickness = 99,
            ShowLegend = false,
            LegendPosition = ChartLegendPosition.Top,
            LegendBorderThickness = -4,
            LegendFontSize = 100
        };

        ChartAreaLegendDialog.FromChart(chart)
            .Should()
            .Be(new ChartAreaLegendDialogResult(
                new CellColor(1, 2, 3),
                null,
                null,
                10,
                false,
                ChartLegendPosition.Top,
                false,
                null,
                null,
                null,
                0,
                72));
    }

    [Fact]
    public void ChartAreaLegendDialogOpenedFromKeyboard_FocusesChartAreaFillBox()
    {
        var dialogSource = DialogSourceTestSupport.ReadHostSources("ChartFormatDialogs.cs");

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_chartAreaFillBox.Focus();");
        dialogSource.Should().Contain("_chartAreaFillBox.SelectAll();");
        dialogSource.Should().Contain("Keyboard.Focus(_chartAreaFillBox);");
    }

    [Fact]
    public void ChartAreaLegendDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartFormatDialogs.cs");

        source.Should().Contain("_ => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _chartAreaFillBox)");
        source.Should().Contain("ChartAreaFormatParseIssue.PlotAreaFillColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _plotAreaFillBox)");
        source.Should().Contain("ChartAreaFormatParseIssue.PlotAreaBorderColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _plotAreaBorderBox)");
        source.Should().Contain("ChartAreaFormatParseIssue.PlotAreaBorderThickness => (UiText.Get(\"ChartAreaLegend_InvalidPlotAreaBorderWidthMessage\"), _plotAreaBorderThicknessBox)");
        source.Should().Contain("ChartAreaFormatParseIssue.LegendTextColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _legendTextBox)");
        source.Should().Contain("ChartAreaFormatParseIssue.LegendFillColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _legendFillBox)");
        source.Should().Contain("ChartAreaFormatParseIssue.LegendBorderColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _legendBorderBox)");
        source.Should().Contain("ChartAreaFormatParseIssue.LegendBorderThickness => (UiText.Get(\"ChartAreaLegend_InvalidLegendBorderWidthMessage\"), _legendBorderThicknessBox)");
        source.Should().Contain("ChartAreaFormatParseIssue.LegendFontSize => (UiText.Get(\"ChartAreaLegend_InvalidLegendFontSizeMessage\"), _legendFontSizeBox)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartAreaFormatParseIssue issue)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void ChartDataLabelsDialogResult_BuildsLayoutOptions()
    {
        var result = ChartDataLabelsDialog.CreateResult(
            showDataLabels: true,
            position: ChartDataLabelPosition.OutsideEnd,
            showValue: false,
            showLegendKey: true,
            showCategoryName: true,
            showSeriesName: false,
            showPercentage: true,
            separator: ChartDataLabelSeparator.NewLine,
            numberFormat: ChartDataLabelNumberFormat.Percent,
            showCallouts: true,
            fillColor: new CellColor(240, 240, 240),
            borderColor: new CellColor(10, 20, 30),
            textColor: new CellColor(40, 50, 60),
            borderThickness: 1.5,
            fontSize: 12,
            angle: -45);

        result.ToOptions().Should().Be(new ChartLayoutOptions(
            ShowDataLabels: true,
            DataLabelPosition: ChartDataLabelPosition.OutsideEnd,
            ShowDataLabelValue: false,
            ShowDataLabelLegendKey: true,
            ShowDataLabelCategoryName: true,
            ShowDataLabelSeriesName: false,
            ShowDataLabelPercentage: true,
            DataLabelSeparator: ChartDataLabelSeparator.NewLine,
            DataLabelNumberFormat: ChartDataLabelNumberFormat.Percent,
            ShowDataLabelCallouts: true,
            DataLabelFillColor: new CellColor(240, 240, 240),
            DataLabelBorderColor: new CellColor(10, 20, 30),
            DataLabelTextColor: new CellColor(40, 50, 60),
            DataLabelBorderThickness: 1.5,
            DataLabelFontSize: 12,
            DataLabelAngle: -45));
    }

    [Fact]
    public void ChartDataLabelsDialogResult_DelegatesOptionsDefaultsAndValidationToSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartDataLabelsDialog.cs");

        source.Should().Contain("public ChartDataLabelsInput ToInput()");
        source.Should().Contain("ChartDataLabelsPlanner.Plan(ToInput())");
        source.Should().Contain("ChartDataLabelsPlanner.Read(chart)");
        source.Should().Contain("ChartDataLabelsPlanner.Normalize(new ChartDataLabelsInput(");
        source.Should().Contain("ChartDataLabelsPlanner.GetPositionChoices()");
        source.Should().Contain("ChartDataLabelsPlanner.GetSeparatorChoices()");
        source.Should().Contain("ChartDataLabelsPlanner.GetNumberFormatChoices()");
        source.Should().Contain("ChartDataLabelsPlanner.GetDialogField(id)");
        source.Should().Contain("ChartDataLabelsPlanner.GetLabelOptionsSection()");
        source.Should().Contain("ChartDataLabelsPlanner.GetStyleSection()");
        source.Should().Contain("ChartDataLabelsPlanner.TryParseDialogInput(");
        source.Should().NotContain("TryReadOptionalColor(");
        source.Should().NotContain("TryReadClampedDouble(");
        source.Should().NotContain("ShowPlannerValidationWarning");
        source.Should().NotContain("ShowDataLabels: ShowDataLabels");
    }

    [Fact]
    public void ChartDataLabelsDialog_FromChart_RoundTripsValueAndLegendKeyToggles()
    {
        var chart = new ChartModel
        {
            ShowDataLabels = true,
            ShowDataLabelValue = false,
            ShowDataLabelLegendKey = true,
            ShowDataLabelCategoryName = true
        };

        var result = ChartDataLabelsDialog.FromChart(chart);

        result.ShowValue.Should().BeFalse();
        result.ShowLegendKey.Should().BeTrue();
        result.ShowCategoryName.Should().BeTrue();
        result.ToOptions().ShowDataLabelValue.Should().BeFalse();
        result.ToOptions().ShowDataLabelLegendKey.Should().BeTrue();
    }

    [Fact]
    public void ChartDataLabelsDialog_FromChart_UsesPlannerNormalizationForDialogDefaults()
    {
        var chart = new ChartModel
        {
            DataLabelPosition = (ChartDataLabelPosition)999,
            DataLabelSeparator = (ChartDataLabelSeparator)999,
            DataLabelNumberFormat = (ChartDataLabelNumberFormat)999,
            DataLabelBorderThickness = 99,
            DataLabelFontSize = 100,
            DataLabelAngle = -120,
        };

        var result = ChartDataLabelsDialog.FromChart(chart);

        result.Position.Should().Be(ChartDataLabelPosition.BestFit);
        result.Separator.Should().Be(ChartDataLabelSeparator.Comma);
        result.NumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        result.BorderThickness.Should().Be(ChartDataLabelsPlanner.MaxBorderThickness);
        result.FontSize.Should().Be(ChartDataLabelsPlanner.MaxFontSize);
        result.Angle.Should().Be(ChartDataLabelsPlanner.MinAngle);
    }

    [Fact]
    public void ChartDataLabelsDialogOpenedFromKeyboard_FocusesShowDataLabelsChoice()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartDataLabelsDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_showBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_showBox);");
    }

    [Fact]
    public void ChartDataLabelsDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartDataLabelsDialog.cs");

        source.Should().Contain("_ => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _fillBox)");
        source.Should().Contain("ChartDataLabelsParseIssue.BorderColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _borderBox)");
        source.Should().Contain("ChartDataLabelsParseIssue.TextColor => (UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _textBox)");
        source.Should().Contain("ChartDataLabelsParseIssue.BorderThickness => (UiText.Get(\"ChartDataLabels_InvalidBorderThicknessMessage\"), _borderThicknessBox)");
        source.Should().Contain("ChartDataLabelsParseIssue.FontSize => (UiText.Get(\"ChartDataLabels_InvalidFontSizeMessage\"), _fontSizeBox)");
        source.Should().Contain("ChartDataLabelsParseIssue.Angle => (UiText.Get(\"ChartDataLabels_InvalidAngleMessage\"), _angleBox)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartDataLabelsParseIssue issue)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

}

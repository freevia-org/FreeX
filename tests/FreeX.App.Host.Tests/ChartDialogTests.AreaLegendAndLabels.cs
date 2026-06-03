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
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartFormatDialogs.cs"));

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_chartAreaFillBox.Focus();");
        dialogSource.Should().Contain("_chartAreaFillBox.SelectAll();");
        dialogSource.Should().Contain("Keyboard.Focus(_chartAreaFillBox);");
    }

    [Fact]
    public void ChartAreaLegendDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartFormatDialogs.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _chartAreaFillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _plotAreaFillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _plotAreaBorderBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAreaLegend_InvalidPlotAreaBorderWidthMessage\"), _plotAreaBorderThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _legendTextBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _legendFillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _legendBorderBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAreaLegend_InvalidLegendBorderWidthMessage\"), _legendBorderThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartAreaLegend_InvalidLegendFontSizeMessage\"), _legendFontSizeBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
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
    public void ChartDataLabelsDialogOpenedFromKeyboard_FocusesShowDataLabelsChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDataLabelsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_showBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_showBox);");
    }

    [Fact]
    public void ChartDataLabelsDialogInvalidInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDataLabelsDialog.cs"));

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _fillBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _borderBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDialog_InvalidOptionalColorMessage\"), _textBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDataLabels_InvalidBorderThicknessMessage\"), _borderThicknessBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDataLabels_InvalidFontSizeMessage\"), _fontSizeBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"ChartDataLabels_InvalidAngleMessage\"), _angleBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

}

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
    public void ChartAreaLegendDialog_ReturnsSharedInputThatBuildsLayoutOptions()
    {
        var result = ChartAreaFormatPlanner.Normalize(new ChartAreaFormatInput(
            ChartAreaFillColor: new CellColor(250, 250, 250),
            PlotAreaFillColor: new CellColor(245, 250, 255),
            PlotAreaBorderColor: new CellColor(120, 120, 120),
            PlotAreaBorderThickness: 2.25,
            ShowLegend: true,
            LegendPosition: ChartLegendPosition.Bottom,
            LegendOverlay: true,
            LegendTextColor: new CellColor(40, 40, 40),
            LegendFillColor: new CellColor(248, 248, 248),
            LegendBorderColor: new CellColor(180, 180, 180),
            LegendBorderThickness: 1.25,
            LegendFontSize: 11));

        ChartAreaFormatPlanner.Plan(result).Should().Be(new ChartLayoutOptions(
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
    public void ChartAreaLegendDialog_UsesSharedContractDefaultsAndParsing()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartFormatDialogs.cs");

        source.Should().Contain("public ChartAreaFormatInput Result { get; private set; }");
        source.Should().Contain("ChartAreaFormatPlanner.Read(chart)");
        source.Should().NotContain("public static ChartAreaFormatInput CreateResult(");
        source.Should().NotContain("public static ChartAreaFormatInput FromChart(");
        source.Should().Contain("ChartAreaFormatPlanner.GetLegendPositionChoices()");
        source.Should().Contain("ChartAreaFormatPlanner.GetFillLineSection()");
        source.Should().Contain("ChartAreaFormatPlanner.GetLegendSection()");
        source.Should().Contain("ChartAreaFormatPlanner.GetDialogField(id)");
        source.Should().Contain("ChartAreaFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("ApplyAutomationIds();");
        source.Should().Contain("LabelText(ChartAreaFormatDialogFieldId.ChartAreaFillColor)");
        source.Should().NotContain("TryReadOptionalColor(");
        source.Should().NotContain("TryReadClampedDouble(");
        source.Should().NotContain("FiniteOrDefault(");
        source.Should().NotContain("ChartAreaLegendDialogResult");
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

        ChartAreaFormatPlanner.Read(chart)
            .Should()
            .Be(new ChartAreaFormatInput(
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

        source.Should().Contain("var presentation = ChartValidationPresentationPlanner.Describe(issue);");
        source.Should().Contain("ChartAreaFormatDialogFieldId.PlotAreaFillColor => _plotAreaFillBox");
        source.Should().Contain("ChartAreaFormatDialogFieldId.PlotAreaBorderColor => _plotAreaBorderBox");
        source.Should().Contain("ChartAreaFormatDialogFieldId.PlotAreaBorderThickness => _plotAreaBorderThicknessBox");
        source.Should().Contain("ChartAreaFormatDialogFieldId.LegendTextColor => _legendTextBox");
        source.Should().Contain("ChartAreaFormatDialogFieldId.LegendFillColor => _legendFillBox");
        source.Should().Contain("ChartAreaFormatDialogFieldId.LegendBorderColor => _legendBorderBox");
        source.Should().Contain("ChartAreaFormatDialogFieldId.LegendBorderThickness => _legendBorderThicknessBox");
        source.Should().Contain("ChartAreaFormatDialogFieldId.LegendFontSize => _legendFontSizeBox");
        source.Should().Contain("presentation.Message.Resolve(UiText.Get, UiText.Format)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartAreaFormatParseIssue issue)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
    }

    [Fact]
    public void ChartDataLabelsDialog_ReturnsSharedInputThatBuildsLayoutOptions()
    {
        var result = ChartDataLabelsPlanner.Normalize(new ChartDataLabelsInput(
            ShowDataLabels: true,
            Position: ChartDataLabelPosition.OutsideEnd,
            ShowValue: false,
            ShowCategoryName: true,
            ShowSeriesName: false,
            ShowPercentage: true,
            ShowLegendKey: true,
            Separator: ChartDataLabelSeparator.NewLine,
            NumberFormat: ChartDataLabelNumberFormat.Percent,
            ShowCallouts: true,
            FillColor: new CellColor(240, 240, 240),
            BorderColor: new CellColor(10, 20, 30),
            TextColor: new CellColor(40, 50, 60),
            BorderThickness: 1.5,
            FontSize: 12,
            Angle: -45));

        ChartDataLabelsPlanner.Plan(result).Should().Be(new ChartLayoutOptions(
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
    public void ChartDataLabelsDialog_UsesSharedContractDefaultsAndValidation()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartDataLabelsDialog.cs");

        source.Should().Contain("public ChartDataLabelsInput Result { get; private set; }");
        source.Should().Contain("ChartDataLabelsPlanner.Read(chart)");
        source.Should().NotContain("public static ChartDataLabelsInput CreateResult(");
        source.Should().NotContain("public static ChartDataLabelsInput FromChart(");
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
        source.Should().NotContain("ChartDataLabelsDialogResult");
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

        var result = ChartDataLabelsPlanner.Read(chart);

        result.ShowValue.Should().BeFalse();
        result.ShowLegendKey.Should().BeTrue();
        result.ShowCategoryName.Should().BeTrue();
        ChartDataLabelsPlanner.Plan(result).ShowDataLabelValue.Should().BeFalse();
        ChartDataLabelsPlanner.Plan(result).ShowDataLabelLegendKey.Should().BeTrue();
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

        var result = ChartDataLabelsPlanner.Read(chart);

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

        source.Should().Contain("var presentation = ChartValidationPresentationPlanner.Describe(issue);");
        source.Should().Contain("ChartDataLabelsDialogFieldId.BorderColor => _borderBox");
        source.Should().Contain("ChartDataLabelsDialogFieldId.TextColor => _textBox");
        source.Should().Contain("ChartDataLabelsDialogFieldId.BorderThickness => _borderThicknessBox");
        source.Should().Contain("ChartDataLabelsDialogFieldId.FontSize => _fontSizeBox");
        source.Should().Contain("ChartDataLabelsDialogFieldId.TextAngle => _angleBox");
        source.Should().Contain("presentation.Message.Resolve(UiText.Get, UiText.Format)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("private void ShowPlannerParseWarning(ChartDataLabelsParseIssue issue)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
    }

}

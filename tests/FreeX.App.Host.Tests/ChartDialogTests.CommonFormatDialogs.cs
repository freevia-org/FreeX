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
    public void ChartFormatDialogs_RouteColorFieldsThroughColorPickerButtons()
    {
        var source = ReadChartFormatDialogSource();
        var helperSource = DialogSourceTestSupport.ReadHostSources("ChartDialogHelpers.cs");

        source.Should().Contain("AddColorText");
        helperSource.Should().Contain("new ColorPickerDialog(initialColor, allowNoColor: true)");
        foreach (var key in new[]
        {
            "ChartAreaLegend_ChartAreaFillColorLabel",
            "ChartAreaLegend_PlotAreaFillColorLabel",
            "ChartAreaLegend_LegendTextColorLabel",
            "ChartSeriesFormat_FillColorLabel",
            "ChartTrendline_LineColorLabel"
        })
        {
            source.Should().Contain($"AddColorText(stack, UiText.Get(\"{key}\")");
        }

        source.Should().Contain("AddColorText(stack, LabelText(ChartAxisDialogFieldId.MajorGridlineColor)");
        source.Should().Contain("AddColorText(stack, LabelText(ChartAxisDialogFieldId.LineColor)");
    }

    [Fact]
    public void ChartFormatDialogs_GroupLongStacksIntoExcelLikeSections()
    {
        var source = ReadChartFormatDialogSource();
        var helperSource = DialogSourceTestSupport.ReadHostSources("ChartDialogHelpers.cs");

        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartDialog_FillLineGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartAreaLegend_LegendGroup\")");
        source.Should().Contain("ChartDataLabelsPlanner.GetLabelOptionsSection()");
        source.Should().Contain("ChartDataLabelsPlanner.GetStyleSection()");
        source.Should().Contain("ChartAxisPlanner.GetAxisOptionsSection()");
        source.Should().Contain("ChartAxisPlanner.GetGridlinesSection()");
        source.Should().Contain("ChartAxisPlanner.GetTickMarksSection()");
        source.Should().Contain("CreateGroupBox(UiText.Get(section.HeaderResourceKey)");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartSeriesFormat_SeriesOptionsGroup\")");
        source.Should().Contain("CreateInlineHelp(");
        source.Should().Contain("AddNumericText");
        helperSource.Should().Contain("AutomationProperties.SetHelpText");
    }

    [Fact]
    public void ChartFormatDialogs_ExposeKeyboardAccessKeysForOptionControls()
    {
        var source = string.Concat(
            ReadChartTypeDialogSource(),
            ReadChartFormatDialogSource());

        foreach (var key in new[]
        {
            "InsertChart_UseRecommendedLayout",
            "ChartAreaLegend_ShowLegend",
            "ChartAreaLegend_OverlayLegend",
            "ChartTrendline_ShowTrendline",
            "ChartTrendline_DisplayEquation",
            "ChartTrendline_DisplayRSquared",
            "ChartErrorBars_ShowErrorBars",
            "ChartErrorBars_EndCaps"
        })
        {
            source.Should().Contain($"UiText.Get(\"{key}\")");
        }

        foreach (var key in new[]
        {
            "ChartAreaLegend_ChartAreaFillColorLabel",
            "ChartAreaLegend_PlotAreaFillColorLabel",
            "ChartAreaLegend_PlotAreaBorderWidthLabel",
            "ChartAreaLegend_LegendPositionLabel",
            "ChartAreaLegend_LegendTextColorLabel",
            "ChartAreaLegend_LegendFontSizeLabel",
            "ChartSeriesFormat_SeriesLabel",
            "ChartSeriesFormat_DashStyleLabel",
            "ChartSeriesFormat_MarkerLabel",
            "ChartTrendline_TypeLabel",
            "ChartErrorBars_DirectionLabel",
            "ChartBarFormat_GapWidthLabel",
            "ChartBarFormat_OverlapLabel",
            "ChartPieFormat_FirstSliceAngleLabel",
            "ChartBubbleFormat_BubbleScaleLabel",
            "ChartBubbleFormat_SizeRepresentsLabel"
        })
        {
            source.Should().Contain($"UiText.Get(\"{key}\")");
        }

        foreach (var fieldId in new[]
        {
            "Minimum",
            "Maximum",
            "MajorUnit",
            "MinorUnit",
            "LogScale",
            "NumberFormat",
            "MajorGridlines",
            "MinorGridlines",
            "MajorGridlineColor",
            "MinorGridlineColor",
            "GridlineThickness",
            "MajorTickMarks",
            "MinorTickMarks",
            "ShowLabels",
            "LabelTextColor",
            "LabelFontSize",
            "LabelAngle",
            "LineColor",
            "LineThickness"
        })
        {
            source.Should().Contain($"LabelText(ChartAxisDialogFieldId.{fieldId})");
        }

        foreach (var fieldId in new[]
        {
            "ShowDataLabels",
            "Position",
            "Value",
            "LegendKey",
            "CategoryName",
            "SeriesName",
            "Percentage",
            "Separator",
            "NumberFormat",
            "Callouts",
            "FillColor",
            "BorderColor",
            "TextColor",
            "BorderThickness",
            "FontSize",
            "TextAngle"
        })
        {
            source.Should().Contain($"LabelText(ChartDataLabelsDialogFieldId.{fieldId})");
        }
    }

    [Fact]
    public void ChartDataLabelsDialog_UsesUniqueAccessKeys()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartDataLabelsDialog.cs");
        var labels = new[]
        {
            "ChartDataLabels_ShowDataLabels",
            "ChartDataLabels_Value",
            "ChartDataLabels_LegendKey",
            "ChartDataLabels_CategoryName",
            "ChartDataLabels_SeriesName",
            "ChartDataLabels_Percentage",
            "ChartDataLabels_Callouts",
            "ChartDataLabels_PositionLabel",
            "ChartDataLabels_SeparatorLabel",
            "ChartDataLabels_NumberFormatLabel",
            "ChartDataLabels_FillColorLabel",
            "ChartDataLabels_BorderColorLabel",
            "ChartDataLabels_TextColorLabel",
            "ChartDataLabels_BorderThicknessLabel",
            "ChartDataLabels_FontSizeLabel",
            "ChartDataLabels_TextAngleLabel"
        }.Select(UiText.Get);
        var duplicateAccessKeys = labels
            .Select(label => new { Label = label, AccessKey = GetAccessKey(label) })
            .GroupBy(item => item.AccessKey)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(item => item.Label))}");

        duplicateAccessKeys.Should().BeEmpty();
    }

    private static string ReadChartFormatDialogSource()
    {
        return DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            "\n",
            "ChartFormatDialogs.cs",
            "ChartAxisFormatDialog.cs",
            "ChartDataLabelsDialog.cs",
            "ChartErrorBarsDialog.cs",
            "ChartTrendlineOptionsDialog.cs",
            "ChartSeriesFormatDialog.cs",
            "ChartTypeFormatDialogs.cs");
    }

    private static char GetAccessKey(string label)
    {
        var index = label.IndexOf('_', StringComparison.Ordinal);
        return char.ToUpperInvariant(label[index + 1]);
    }

}

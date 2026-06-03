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
    public void ChartFormatDialogs_RouteColorFieldsThroughColorPickerButtons()
    {
        var source = ReadChartFormatDialogSource();
        var helperSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDialogHelpers.cs"));

        source.Should().Contain("AddColorText");
        helperSource.Should().Contain("new ColorPickerDialog(initialColor, allowNoColor: true)");
        foreach (var key in new[]
        {
            "ChartAreaLegend_ChartAreaFillColorLabel",
            "ChartAreaLegend_PlotAreaFillColorLabel",
            "ChartAreaLegend_LegendTextColorLabel",
            "ChartSeriesFormat_FillColorLabel",
            "ChartTrendline_LineColorLabel",
            "ChartAxisFormat_MajorGridlineColorLabel",
            "ChartAxisFormat_AxisLineColorLabel"
        })
        {
            source.Should().Contain($"AddColorText(stack, UiText.Get(\"{key}\")");
        }
    }

    [Fact]
    public void ChartFormatDialogs_GroupLongStacksIntoExcelLikeSections()
    {
        var source = ReadChartFormatDialogSource();
        var helperSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDialogHelpers.cs"));

        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartDialog_FillLineGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartAreaLegend_LegendGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartDataLabels_LabelOptionsGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartAxisFormat_AxisOptionsGroup\")");
        source.Should().Contain("CreateGroupBox(UiText.Get(\"ChartAxisFormat_TickMarksGroup\")");
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
            "ChartDataLabels_ShowDataLabels",
            "ChartDataLabels_Value",
            "ChartDataLabels_LegendKey",
            "ChartDataLabels_CategoryName",
            "ChartDataLabels_SeriesName",
            "ChartDataLabels_Percentage",
            "ChartDataLabels_Callouts",
            "ChartTrendline_ShowTrendline",
            "ChartTrendline_DisplayEquation",
            "ChartTrendline_DisplayRSquared",
            "ChartErrorBars_ShowErrorBars",
            "ChartErrorBars_EndCaps",
            "ChartAxisFormat_LogScale",
            "ChartAxisFormat_MajorGridlines",
            "ChartAxisFormat_MinorGridlines",
            "ChartAxisFormat_ShowLabels"
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
            "ChartDataLabels_PositionLabel",
            "ChartDataLabels_SeparatorLabel",
            "ChartDataLabels_NumberFormatLabel",
            "ChartDataLabels_BorderThicknessLabel",
            "ChartAxisFormat_MinimumLabel",
            "ChartAxisFormat_MaximumLabel",
            "ChartAxisFormat_MajorTickMarksLabel",
            "ChartAxisFormat_MinorTickMarksLabel",
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
    }

    [Fact]
    public void ChartDataLabelsDialog_UsesUniqueAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ChartDataLabelsDialog.cs"));
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
        return string.Join(
            "\n",
            new[]
            {
                "ChartFormatDialogs.cs",
                "ChartAxisFormatDialog.cs",
                "ChartDataLabelsDialog.cs",
                "ChartErrorBarsDialog.cs",
                "ChartTrendlineOptionsDialog.cs",
                "ChartSeriesFormatDialog.cs",
                "ChartTypeFormatDialogs.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName))));
    }

    private static char GetAccessKey(string label)
    {
        var index = label.IndexOf('_', StringComparison.Ordinal);
        return char.ToUpperInvariant(label[index + 1]);
    }

}

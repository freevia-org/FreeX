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
        foreach (var fieldId in new[]
        {
            "ChartAreaFillColor",
            "PlotAreaFillColor",
            "LegendTextColor"
        })
        {
            source.Should().Contain($"AddColorText(stack, LabelText(ChartAreaFormatDialogFieldId.{fieldId})");
        }

        source.Should().Contain("AddColorText(stack, LabelText(ChartSeriesFormatDialogFieldId.FillColor)");
        source.Should().Contain("AddColorText(stack, LabelText(ChartTrendlineDialogFieldId.LineColor)");
        source.Should().Contain("AddColorText(stack, LabelText(ChartAxisDialogFieldId.MajorGridlineColor)");
        source.Should().Contain("AddColorText(stack, LabelText(ChartAxisDialogFieldId.LineColor)");
    }

    [Fact]
    public void ChartFormatDialogs_GroupLongStacksIntoExcelLikeSections()
    {
        var source = ReadChartFormatDialogSource();
        var helperSource = DialogSourceTestSupport.ReadHostSources("ChartDialogHelpers.cs");

        source.Should().Contain("ChartAreaFormatPlanner.GetFillLineSection()");
        source.Should().Contain("ChartAreaFormatPlanner.GetLegendSection()");
        source.Should().Contain("ChartDataLabelsPlanner.GetLabelOptionsSection()");
        source.Should().Contain("ChartDataLabelsPlanner.GetStyleSection()");
        source.Should().Contain("ChartAxisPlanner.GetAxisOptionsSection()");
        source.Should().Contain("ChartAxisPlanner.GetGridlinesSection()");
        source.Should().Contain("ChartAxisPlanner.GetTickMarksSection()");
        source.Should().Contain("ChartSeriesFormatPlanner.GetSeriesOptionsSection()");
        source.Should().Contain("ChartSeriesFormatPlanner.GetFillLineSection()");
        source.Should().Contain("ChartTrendlinePlanner.GetOptionsSection()");
        source.Should().Contain("ChartTrendlinePlanner.GetLineSection()");
        source.Should().Contain("ChartErrorBarsPlanner.GetErrorAmountSection()");
        source.Should().Contain("CreateGroupBox(UiText.Get(section.HeaderResourceKey)");
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
            "InsertChart_UseRecommendedLayout"
        })
        {
            source.Should().Contain($"UiText.Get(\"{key}\")");
        }

        foreach (var fieldId in new[]
        {
            "ShowLegend",
            "LegendOverlay"
        })
        {
            source.Should().Contain($"LabelText(ChartAreaFormatDialogFieldId.{fieldId})");
        }

        foreach (var fieldId in new[]
        {
            "ShowTrendline",
            "ShowEquation",
            "ShowRSquared"
        })
        {
            source.Should().Contain($"LabelText(ChartTrendlineDialogFieldId.{fieldId})");
        }

        foreach (var fieldId in new[]
        {
            "ShowErrorBars",
            "EndCaps"
        })
        {
            source.Should().Contain($"LabelText(ChartErrorBarsDialogFieldId.{fieldId})");
        }

        foreach (var fieldId in new[]
        {
            "ChartAreaFillColor",
            "PlotAreaFillColor",
            "PlotAreaBorderThickness",
            "LegendPosition",
            "LegendTextColor",
            "LegendFontSize"
        })
        {
            source.Should().Contain($"LabelText(ChartAreaFormatDialogFieldId.{fieldId})");
        }

        foreach (var key in new[]
        {
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
            "Series",
            "DashStyle",
            "MarkerStyle"
        })
        {
            source.Should().Contain($"LabelText(ChartSeriesFormatDialogFieldId.{fieldId})");
        }

        source.Should().Contain("LabelText(ChartTrendlineDialogFieldId.Type)");
        source.Should().Contain("LabelText(ChartErrorBarsDialogFieldId.Direction)");

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

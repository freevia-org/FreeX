using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisShellActionPlannerTests
{
    [Fact]
    public void Plan_WpfConditionalFormat_OpensDialogWithSharedTitle()
    {
        var action = Plan("format.lessthan", QuickAnalysisShellCapabilities.DialogBacked);

        action.Kind.Should().Be(QuickAnalysisShellActionKind.OpenConditionalFormatDialog);
        action.Route.ConditionalFormat.Should().Be(QuickAnalysisConditionalFormatCommand.LessThan);
        action.ConditionalFormatDialog.Should().NotBeNull();
        action.ConditionalFormatDialog!.Command.Should().Be(QuickAnalysisConditionalFormatCommand.LessThan);
        action.ConditionalFormatDialog.Title.Should().Be("Less Than");
        action.ConditionalFormatDialog.Seed.Should().Be(
            new QuickAnalysisConditionalFormatDialogSeed(CfRuleType.CellValue, CfOperator.LessThan));
    }

    [Fact]
    public void Plan_AvaloniaConditionalFormat_AppliesPresetIntent()
    {
        var action = Plan("format.lessthan", QuickAnalysisShellCapabilities.DirectApplyLimited);

        action.Kind.Should().Be(QuickAnalysisShellActionKind.ApplyConditionalFormat);
        action.Route.ConditionalFormat.Should().Be(QuickAnalysisConditionalFormatCommand.LessThan);
        action.ConditionalFormatPreset.Should().Be(ConditionalFormatPreset.HighlightLessThan);
        action.ConditionalFormatDialog.Should().BeNull();
    }

    [Theory]
    [InlineData("format.databars", ConditionalFormatPreset.DataBar)]
    [InlineData("format.colorscale", ConditionalFormatPreset.ColorScale)]
    [InlineData("format.iconset", ConditionalFormatPreset.IconSet)]
    [InlineData("format.greaterthan", ConditionalFormatPreset.HighlightGreaterThan)]
    [InlineData("format.lessthan", ConditionalFormatPreset.HighlightLessThan)]
    [InlineData("format.between", ConditionalFormatPreset.HighlightBetween)]
    [InlineData("format.equalto", ConditionalFormatPreset.HighlightEqualTo)]
    [InlineData("format.textcontains", ConditionalFormatPreset.HighlightTextContains)]
    [InlineData("format.dateoccurring", ConditionalFormatPreset.HighlightDateOccurring)]
    [InlineData("format.duplicatevalues", ConditionalFormatPreset.HighlightDuplicateValues)]
    [InlineData("format.top10", ConditionalFormatPreset.Top10)]
    [InlineData("format.top10percent", ConditionalFormatPreset.Top10Percent)]
    [InlineData("format.bottom10", ConditionalFormatPreset.Bottom10Items)]
    [InlineData("format.bottom10percent", ConditionalFormatPreset.Bottom10Percent)]
    [InlineData("format.aboveaverage", ConditionalFormatPreset.AboveAverage)]
    [InlineData("format.belowaverage", ConditionalFormatPreset.BelowAverage)]
    public void Plan_AvaloniaConditionalFormat_CarriesSharedPreset(
        string itemId,
        ConditionalFormatPreset expectedPreset)
    {
        var action = Plan(itemId, QuickAnalysisShellCapabilities.DirectApplyLimited);

        action.Kind.Should().Be(QuickAnalysisShellActionKind.ApplyConditionalFormat);
        action.ConditionalFormatPreset.Should().Be(expectedPreset);
    }

    [Theory]
    [InlineData("format.clear", QuickAnalysisShellActionKind.ClearConditionalFormatting)]
    [InlineData("chart.more", QuickAnalysisShellActionKind.OpenChartPicker)]
    [InlineData("total.percenttotal", QuickAnalysisShellActionKind.InsertPercentTotalFormula)]
    [InlineData("total.runningtotal", QuickAnalysisShellActionKind.InsertRunningTotalFormula)]
    [InlineData("table.table", QuickAnalysisShellActionKind.CreateTable)]
    [InlineData("table.pivottable", QuickAnalysisShellActionKind.CreatePivotTable)]
    public void Plan_WpfCapabilities_RoutesFullShellActions(
        string itemId,
        QuickAnalysisShellActionKind expectedKind)
    {
        var action = Plan(itemId, QuickAnalysisShellCapabilities.DialogBacked);

        action.Kind.Should().Be(expectedKind);
    }

    [Fact]
    public void Plan_Chart_CarriesChartType()
    {
        var action = Plan("chart.clusteredcolumn", QuickAnalysisShellCapabilities.DialogBacked);

        action.Kind.Should().Be(QuickAnalysisShellActionKind.InsertChart);
        action.ChartType.Should().Be(ChartType.Column);
    }

    [Fact]
    public void Plan_AggregateTotal_CarriesAutoSumFunction()
    {
        var action = Plan("total.sum", QuickAnalysisShellCapabilities.DirectApplyLimited);

        action.Kind.Should().Be(QuickAnalysisShellActionKind.InsertAggregateTotalFormula);
        action.TotalFunction.Should().Be("SUM");
        action.TotalCommandTitle.Should().Be("Quick Analysis Sum");
    }

    [Theory]
    [InlineData("total.percenttotal", "Quick Analysis % Total")]
    [InlineData("total.runningtotal", "Quick Analysis Running Total")]
    public void Plan_ExpandedTotalsCarryStableCommandTitles(string itemId, string expectedTitle)
    {
        var action = Plan(itemId, QuickAnalysisShellCapabilities.DialogBacked);

        action.TotalCommandTitle.Should().Be(expectedTitle);
    }

    [Theory]
    [InlineData("sparkline.line", SparklineKind.Line, "line")]
    [InlineData("sparkline.column", SparklineKind.Column, "column")]
    [InlineData("sparkline.winloss", SparklineKind.WinLoss, "winloss")]
    public void Plan_Sparkline_CarriesCoreKindAndDialogKind(
        string itemId,
        SparklineKind expectedKind,
        string expectedDialogKind)
    {
        var action = Plan(itemId, QuickAnalysisShellCapabilities.DialogBacked);

        action.Kind.Should().Be(QuickAnalysisShellActionKind.InsertSparkline);
        action.SparklineKind.Should().Be(expectedKind);
        action.SparklineDialogKind.Should().Be(expectedDialogKind);
    }

    [Theory]
    [InlineData("total.percenttotal", "This total is not yet available on macOS.")]
    [InlineData("total.runningtotal", "This total is not yet available on macOS.")]
    [InlineData("table.pivottable", "Converting to a PivotTable is not yet available on macOS.")]
    public void Plan_AvaloniaCapabilities_DefersUnsupportedShellActions(
        string itemId,
        string expectedNote)
    {
        var action = Plan(itemId, QuickAnalysisShellCapabilities.DirectApplyLimited);

        action.Kind.Should().Be(QuickAnalysisShellActionKind.Deferred);
        action.DeferredNote.Should().Be(expectedNote);
    }

    private static QuickAnalysisShellAction Plan(
        string itemId,
        QuickAnalysisShellCapabilities capabilities)
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2));
        var item = QuickAnalysisPlanner.BuildDisplayModel(selection)
            .AllItems()
            .Single(item => item.Id == itemId);

        return QuickAnalysisShellActionPlanner.Plan(item, capabilities);
    }
}

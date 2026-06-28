using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisHostOperationPlannerTests
{
    [Fact]
    public void Plan_DialogBackedConditionalFormat_CarriesDialogTitle()
    {
        var operation = Plan("format.lessthan", QuickAnalysisShellCapabilities.DialogBacked);

        operation.Kind.Should().Be(QuickAnalysisHostOperationKind.OpenConditionalFormatDialog);
        operation.ConditionalFormatDialogTitle.Should().Be("Less Than");
    }

    [Fact]
    public void Plan_DirectApplyConditionalFormat_CarriesPreset()
    {
        var operation = Plan("format.lessthan", QuickAnalysisShellCapabilities.DirectApplyLimited);

        operation.Kind.Should().Be(QuickAnalysisHostOperationKind.ApplyConditionalFormat);
        operation.ConditionalFormatPreset.Should().Be(ConditionalFormatPreset.HighlightLessThan);
    }

    [Theory]
    [InlineData("total.sum", "SUM", "Quick Analysis Sum")]
    [InlineData("total.average", "AVERAGE", "Quick Analysis Average")]
    [InlineData("total.count", "COUNT", "Quick Analysis Count")]
    public void Plan_AggregateTotal_CarriesFunctionAndStableCommandTitle(
        string itemId,
        string expectedFunction,
        string expectedTitle)
    {
        var operation = Plan(itemId, QuickAnalysisShellCapabilities.DialogBacked);

        operation.Kind.Should().Be(QuickAnalysisHostOperationKind.InsertAggregateTotalFormula);
        operation.TotalFunction.Should().Be(expectedFunction);
        operation.TotalCommandTitle.Should().Be(expectedTitle);
    }

    [Theory]
    [InlineData("total.percenttotal", QuickAnalysisHostOperationKind.InsertPercentTotalFormula, "Quick Analysis % Total")]
    [InlineData("total.runningtotal", QuickAnalysisHostOperationKind.InsertRunningTotalFormula, "Quick Analysis Running Total")]
    public void Plan_DialogBackedExpandedTotals_CarryStableCommandTitles(
        string itemId,
        QuickAnalysisHostOperationKind expectedKind,
        string expectedTitle)
    {
        var operation = Plan(itemId, QuickAnalysisShellCapabilities.DialogBacked);

        operation.Kind.Should().Be(expectedKind);
        operation.TotalCommandTitle.Should().Be(expectedTitle);
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
        var operation = Plan(itemId, QuickAnalysisShellCapabilities.DialogBacked);

        operation.Kind.Should().Be(QuickAnalysisHostOperationKind.InsertSparkline);
        operation.SparklineKind.Should().Be(expectedKind);
        operation.SparklineDialogKind.Should().Be(expectedDialogKind);
    }

    [Fact]
    public void Plan_Deferred_CarriesSharedDeferredNote()
    {
        var operation = Plan("table.pivottable", QuickAnalysisShellCapabilities.DirectApplyLimited);

        operation.Kind.Should().Be(QuickAnalysisHostOperationKind.Deferred);
        operation.DeferredNote.Should().Be("Converting to a PivotTable is not yet available on macOS.");
    }

    private static QuickAnalysisHostOperation Plan(
        string itemId,
        QuickAnalysisShellCapabilities capabilities)
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2));
        var displayModel = QuickAnalysisPlanner.BuildDisplayModel(selection);
        var item = QuickAnalysisShellPlanner.BuildMenuPlan(displayModel, capabilities, selection)
            .AllItems()
            .Single(item => item.Id == itemId);

        return QuickAnalysisHostOperationPlanner.Plan(item);
    }
}

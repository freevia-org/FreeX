using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisHostOperationPlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void Plan_DialogBackedConditionalFormat_CarriesDialogTitle()
    {
        var operation = Plan("format.lessthan", QuickAnalysisShellCapabilities.DialogBacked);

        operation.Kind.Should().Be(QuickAnalysisHostOperationKind.OpenConditionalFormatDialog);
        operation.ConditionalFormatDialog.Should().NotBeNull();
        operation.ConditionalFormatDialog!.Command.Should().Be(QuickAnalysisConditionalFormatCommand.LessThan);
        operation.ConditionalFormatDialog.Title.Should().Be("Less Than");
        operation.ConditionalFormatDialog.Seed.Operator.Should().Be(CfOperator.LessThan);
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

    [Fact]
    public void TryBuildSparklineCommands_PlansDirectApplyCommandsWithSharedHeaderDetection()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q2"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        var operation = Plan("sparkline.line", QuickAnalysisShellCapabilities.DirectApplyLimited);

        var planned = QuickAnalysisHostOperationPlanner.TryBuildSparklineCommands(
            operation,
            sheet,
            range,
            out var commands);

        planned.Should().BeTrue();
        commands.Should().HaveCount(3);

        var context = new TestCommandContext(workbook);
        foreach (var command in commands)
            command.Apply(context).Success.Should().BeTrue();

        sheet.Sparklines.Select(sparkline => sparkline.Location)
            .Should()
            .Equal(
                new CellAddress(sheet.Id, 2, 3),
                new CellAddress(sheet.Id, 3, 3),
                new CellAddress(sheet.Id, 4, 3));
        sheet.Sparklines.Select(sparkline => sparkline.DataRange.Start.Row)
            .Should()
            .Equal(2u, 3u, 4u);
        sheet.Sparklines.Should().OnlyContain(sparkline => sparkline.Kind == SparklineKind.Line);
    }

    [Fact]
    public void TryBuildSparklineCommands_ReturnsFalseForNonSparklineOperation()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        var operation = Plan("total.sum", QuickAnalysisShellCapabilities.DirectApplyLimited);

        var planned = QuickAnalysisHostOperationPlanner.TryBuildSparklineCommands(
            operation,
            sheet,
            range,
            out var commands);

        planned.Should().BeFalse();
        commands.Should().BeEmpty();
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

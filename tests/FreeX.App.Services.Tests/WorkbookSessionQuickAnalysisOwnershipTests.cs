using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionQuickAnalysisOwnershipTests
{
    [Fact]
    public void ExecuteQuickAnalysisTotal_OwnsCommandSelectionAndUndoState()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        var sheet = session.ActiveSheet;
        var range = Range(sheet.Id, 1, 1, 3, 2);
        SeedNumericRange(sheet, range);
        session.SelectRange(range);

        var result = session.ExecuteQuickAnalysisTotal(Operation(session, "total.sum"));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.AppliedItemCount.Should().Be(3);
        result.SelectedCell.Should().Be(new CellAddress(sheet.Id, 3, 3));
        result.SelectedCell.Should().Be(session.ActiveCell);
        sheet.GetCell(new CellAddress(sheet.Id, 1, 3))!.FormulaText.Should().Be("SUM(A1:B1)");
        sheet.GetCell(new CellAddress(sheet.Id, 3, 3))!.FormulaText.Should().Be("SUM(A3:B3)");
        session.CanUndo.Should().BeTrue();

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(new CellAddress(sheet.Id, 1, 3)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 3, 3)).Should().BeNull();
    }

    [Fact]
    public void ExecuteQuickAnalysisSparklines_OwnsHeaderDetectionAndCommandSequence()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        var sheet = session.ActiveSheet;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q2"));
        var range = Range(sheet.Id, 1, 1, 3, 2);
        for (uint row = 2; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }
        session.SelectRange(range);

        var result = session.ExecuteQuickAnalysisSparklines(Operation(session, "sparkline.line"));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.AppliedItemCount.Should().Be(2);
        result.SourceRange.Should().Be(range);
        sheet.Sparklines.Select(sparkline => sparkline.Location).Should().Equal(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 3, 3));
        sheet.Sparklines.Should().OnlyContain(sparkline => sparkline.Kind == SparklineKind.Line);
    }

    [Fact]
    public void SelectFormulaPointModeSourceRange_OwnsSheetAndRangeTransition()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        var sourceSheet = session.ActiveSheet;
        var pointedSheet = session.Workbook.AddSheet("Pointed");
        var pointed = Range(pointedSheet.Id, 2, 3, 4, 5);

        session.SelectFormulaPointModeSourceRange(pointed).Should().BeTrue();

        session.ActiveSheet.Should().BeSameAs(pointedSheet);
        session.SelectedRange.Should().Be(pointed);
        session.ActiveCell.Should().Be(pointed.Start);

        session.SelectFormulaPointModeSourceRange(
                Range(SheetId.New(), 1, 1, 1, 1))
            .Should()
            .BeFalse();
        session.ActiveSheet.Should().BeSameAs(pointedSheet);
        sourceSheet.Should().NotBeSameAs(session.ActiveSheet);
    }

    private static QuickAnalysisHostOperation Operation(WorkbookSession session, string itemId)
    {
        var plan = new QuickAnalysisShellSession().PlanOpen(
            session.ActiveSheet,
            session.SelectedRange,
            QuickAnalysisShellCapabilities.DialogBacked);
        var item = plan.ShellPlan.AllItems().Single(candidate => candidate.Id == itemId);
        return QuickAnalysisHostOperationPlanner.Plan(item);
    }

    private static void SeedNumericRange(Sheet sheet, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * col));
        }
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}

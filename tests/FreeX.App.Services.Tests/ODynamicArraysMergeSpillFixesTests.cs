using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression tests for review-group O-dynamic-arrays finding H53: merging cells over a live
/// dynamic-array spill range must be rejected (matching Excel), not silently absorbed.
/// </summary>
public sealed class ODynamicArraysMergeSpillFixesTests
{
    [Fact]
    public void HasLiveSpillTarget_DetectsNonAnchorSpillCellInRange()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(1,3)"); // spills into B1, C1
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,] { { new NumberValue(1), new NumberValue(2), new NumberValue(3) } }, 1, 1));

        CellMergePlanner.HasLiveSpillTarget(sheet, Range(sheet.Id, 1, 2, 2, 3)).Should().BeTrue();
        CellMergePlanner.HasLiveSpillTarget(sheet, Range(sheet.Id, 5, 5, 6, 6)).Should().BeFalse();
    }

    [Fact]
    public void AnalyzeContent_TreatsLiveSpillTargetCellAsContent()
    {
        // A1 spills into B1/C1 via Sheet._spillValues (never Sheet._cells) — AnalyzeContent must not
        // be blind to it just because Sheet.GetCell only reads _cells.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(1,3)");
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,] { { new NumberValue(1), new NumberValue(2), new NumberValue(3) } }, 1, 1));

        var plan = CellMergePlanner.AnalyzeContent(sheet, Range(sheet.Id, 1, 2, 2, 3));

        plan.WouldLoseContent.Should().BeTrue();
        plan.Entries.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 1, 2) && e.DisplayText == "2");
    }

    [Fact]
    public void CreateMergeCommands_OverLiveSpillRange_ReturnsRejectingCommandInsteadOfMergeCellsCommand()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(1,3)");
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,] { { new NumberValue(1), new NumberValue(2), new NumberValue(3) } }, 1, 1));

        var commands = CellMergePlanner.CreateMergeCommands(
            sheet, sheet.Id, Range(sheet.Id, 1, 2, 2, 3), mergeCells: true);

        commands.Should().ContainSingle().Which.Should().BeOfType<RejectSpillOverlapCommand>();
    }

    [Fact]
    public void CreateFormatCellsMergeCommands_OverLiveSpillRange_ReturnsRejectingCommand()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(1,3)");
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,] { { new NumberValue(1), new NumberValue(2), new NumberValue(3) } }, 1, 1));

        var commands = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet, sheet.Id, Range(sheet.Id, 1, 2, 2, 3), mergeCells: true);

        commands.Should().ContainSingle().Which.Should().BeOfType<RejectSpillOverlapCommand>();
    }

    [Fact]
    public void CreateMergeAndCenterCommands_OverLiveSpillRange_ReturnsRejectingCommand()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(1,3)");
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,] { { new NumberValue(1), new NumberValue(2), new NumberValue(3) } }, 1, 1));

        var commands = CellMergePlanner.CreateMergeAndCenterCommands(
            sheet, sheet.Id, Range(sheet.Id, 1, 2, 2, 3), MergeCellContentResolution.KeepFirstCell);

        commands.Should().ContainSingle().Which.Should().BeOfType<RejectSpillOverlapCommand>();
    }

    [Fact]
    public void RejectSpillOverlapCommand_Apply_FailsWithClearErrorAndDoesNotMutateSheet()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(1,3)");
        var spillValue = new RangeValue(new ScalarValue[,] { { new NumberValue(1), new NumberValue(2), new NumberValue(3) } }, 1, 1);
        sheet.SetSpillRange(anchor, spillValue);

        var range = Range(sheet.Id, 1, 2, 2, 3);
        var commands = CellMergePlanner.CreateMergeCommands(sheet, sheet.Id, range, mergeCells: true);
        var command = commands.Should().ContainSingle().Subject;

        var ctx = new WorkbookCommandContext(workbook);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        sheet.MergedRegions.Should().BeEmpty("a rejected merge must not register a merged region");
        sheet.GetValue(1, 2).Should().Be(new NumberValue(2), "the spilled value must survive an aborted merge attempt");
    }

    [Fact]
    public void CreateMergeCommands_OverAnchorCellItself_StillMerges()
    {
        // The anchor cell (A1) is a normal authored formula cell (visible via GetCell/_cells), not a
        // spill target — merging a range that includes only the anchor (and empty cells) is fine and
        // must proceed normally, same as merging any other formula cell.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(anchor, "SEQUENCE(1,3)");
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,] { { new NumberValue(1), new NumberValue(2), new NumberValue(3) } }, 1, 1));

        // Merge A1:A2 — a column below the anchor, disjoint from the spill's row (B1:C1).
        var commands = CellMergePlanner.CreateMergeCommands(
            sheet, sheet.Id, Range(sheet.Id, 1, 1, 2, 1), mergeCells: true);

        commands.Should().ContainSingle().Which.Should().BeOfType<MergeCellsCommand>();
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}

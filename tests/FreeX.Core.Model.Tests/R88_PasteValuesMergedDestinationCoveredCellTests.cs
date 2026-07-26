using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R88-commands-paste-special-5-1: Paste Special &gt; Values/Formulas (with no other Paste
/// Special option selected) writes live values into COVERED (non-anchor) cells of a merged
/// destination. PasteCommandFactory's plain fallthrough for Values/Formulas mode builds an edit
/// list that shifts every source cell onto the destination and hands it to EditCellsCommand,
/// which -- unlike PasteCellsCommand/PasteSpecialCellsCommand -- had no merge-anchor guard, so it
/// wrote directly into the hidden half of an existing merge. Matches Excel: only a merge's
/// top-left anchor cell may ever carry a value.
/// </summary>
public sealed class R88_PasteValuesMergedDestinationCoveredCellTests
{
    [Fact]
    public void PasteValuesMode_IntoMergedDestination_LeavesCoveredCellEmpty()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Existing merge B2:C2 (anchor B2).
        var anchor = new CellAddress(sheet.Id, 2, 2);
        var covered = new CellAddress(sheet.Id, 2, 3);
        sheet.AddMergedRegion(new GridRange(anchor, covered));

        // Copy source A1:B1 = (5, 7).
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(7)));
        var sourceCells = sourceRange.AllCells()
            .Select(addr => (Source: addr, Cell: sheet.GetCell(addr)!.Clone()))
            .ToList();

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            anchor,
            PasteCellsMode.Values,
            default);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(anchor).Should().Be(new NumberValue(5), "the merge's anchor cell takes the pasted value");
        sheet.GetValue(covered).Should().Be(BlankValue.Instance,
            "a merge's covered (non-anchor) cell must never carry an independent value");

        command.Revert(ctx);
        sheet.GetValue(anchor).Should().Be(BlankValue.Instance);
        sheet.GetValue(covered).Should().Be(BlankValue.Instance);
    }

    // No-regression sibling: pasting Values into an UNMERGED destination must still write every
    // cell normally -- the merge-anchor guard must only skip covered cells of an existing merge,
    // never a plain destination.
    [Fact]
    public void PasteValuesMode_IntoUnmergedDestination_WritesEveryCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(7)));
        var sourceCells = sourceRange.AllCells()
            .Select(addr => (Source: addr, Cell: sheet.GetCell(addr)!.Clone()))
            .ToList();

        var destination = new CellAddress(sheet.Id, 5, 2);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            destination,
            PasteCellsMode.Values,
            default);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(new CellAddress(sheet.Id, 5, 2)).Should().Be(new NumberValue(5));
        sheet.GetValue(new CellAddress(sheet.Id, 5, 3)).Should().Be(new NumberValue(7));
    }
}

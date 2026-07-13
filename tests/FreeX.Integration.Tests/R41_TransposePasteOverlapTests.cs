using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R41-commands-transpose-paste-3-1: Excel refuses a Paste Special > Transpose whose destination
/// rectangle overlaps the copied source range ("the Copy area and paste area cannot overlap ... when
/// you use the Transpose option"). PasteCommandValidator.ValidateInternalPaste only checked worksheet
/// bounds and duplicate-destination mapping, never comparing the (possibly-transposed) destination
/// rectangle against the source range, so FreeX silently performed an in-place transpose that Excel
/// would have blocked. Fixed by rejecting a Transpose paste whose destination range overlaps
/// sourceRange, matching Excel's guardrail; non-overlapping transpose pastes (and non-transpose
/// overlapping pastes, which Excel does allow) remain unaffected.
/// </summary>
public sealed class R41_TransposePasteOverlapTests
{
    [Fact]
    public void TransposePaste_OntoOverlappingDestination_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source A1:B2 = { A1=1, B1=2, A2=3, B2=4 }.
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var sourceRange = new GridRange(a1, b2);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(b1, Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(a2, Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(b2, Cell.FromValue(new NumberValue(4)));

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in sourceRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        // Destination A1 (in-place transpose) overlaps the 2x2 source footprint entirely.
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            a1,
            PasteCellsMode.All,
            new PasteSpecialOptions(Transpose: true));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse("Excel blocks a Transpose paste whose destination overlaps the source");
        outcome.ErrorMessage.Should().Contain("overlap");

        // Data must be untouched (command was rejected before any mutation).
        sheet.GetValue(a1).Should().Be(new NumberValue(1));
        sheet.GetValue(b1).Should().Be(new NumberValue(2));
        sheet.GetValue(a2).Should().Be(new NumberValue(3));
        sheet.GetValue(b2).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void TransposePaste_OntoPartiallyOverlappingDestination_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source A1:B2 (2x2). Transposed footprint is also 2x2. Destination B1 overlaps (B1, B2 shared).
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var sourceRange = new GridRange(a1, b2);
        foreach (var addr in sourceRange.AllCells())
            sheet.SetCell(addr, Cell.FromValue(new NumberValue(addr.Row * 10 + addr.Col)));

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in sourceRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        var destination = new CellAddress(sheet.Id, 1, 2); // B1: transposed 2x2 destination = B1:C2, overlaps B1:B2.
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(Transpose: true));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse("the transposed destination rectangle B1:C2 still overlaps source column B");
    }

    [Fact]
    public void TransposePaste_OntoNonOverlappingDestination_Succeeds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source A1:B2 = { A1=1, B1=2, A2=3, B2=4 }.
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var sourceRange = new GridRange(a1, b2);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(b1, Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(a2, Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(b2, Cell.FromValue(new NumberValue(4)));

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in sourceRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        // Destination D1, far away — no overlap with A1:B2.
        var destination = new CellAddress(sheet.Id, 1, 4);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(Transpose: true));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Transposed: D1=A1=1, E1=A2=3, D2=B1=2, E2=B2=4.
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        var d2 = new CellAddress(sheet.Id, 2, 4);
        var e2 = new CellAddress(sheet.Id, 2, 5);
        sheet.GetValue(d1).Should().Be(new NumberValue(1));
        sheet.GetValue(e1).Should().Be(new NumberValue(3));
        sheet.GetValue(d2).Should().Be(new NumberValue(2));
        sheet.GetValue(e2).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void NonTransposePaste_OntoOverlappingDestination_IsStillAllowed()
    {
        // Sibling no-regression case: Excel DOES allow a plain (non-transpose) paste to overlap its
        // source (e.g. shifting a block down-and-right by one row/col); only Transpose is blocked.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var sourceRange = new GridRange(a1, b2);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(b2, Cell.FromValue(new NumberValue(4)));

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in sourceRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        // Destination B2 overlaps the source rectangle, but Transpose is false — should succeed.
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            b2,
            PasteCellsMode.All,
            new PasteSpecialOptions(Transpose: false));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }
}

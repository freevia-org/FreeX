using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// shared-large-document-limits-F1: CreateExternalTextPasteCommand tiled a short external-text
/// block across the destination range with no cell-count cap -- only a worksheet-bounds check
/// (WorksheetBounds.TryGetRectangleEnd), which a whole-sheet destination always passes. Pasting a
/// single-cell external clipboard block onto a whole-sheet selection (reachable via Ctrl+A on a
/// blank sheet, then Ctrl+V from a non-FreeX source) built a ~17.18-billion-entry `edits` list on
/// the synchronous UI thread. CreateInternalPasteCommand's sibling tiled path already rejects an
/// oversized destination up front via MaxTiledPasteCellCount ("Paste destination is too large to
/// fill with the copied cells..."); this pins the identical guard on the external-clipboard path.
/// </summary>
public sealed class PasteCommandFactoryExternalTiledCellCapTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void ExternalTextPaste_WholeSheetDestination_IsRejectedInsteadOfTilingBillionsOfCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var wholeSheet = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));

        // A single short external-text cell, exactly the "Ctrl+A on a blank sheet, then paste a
        // Notepad snippet" gesture from the finding -- no internal FreeX clipboard content, no
        // Paste Special options.
        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, wholeSheet, [["hello"]]);

        command.Should().BeOfType<RejectedWorkbookCommand>();
        var ctx = new TestCommandContext(wb);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("too large to fill with the copied cells");
        outcome.ErrorMessage.Should().Contain("4,000,000");
    }

    [Fact]
    public void ExternalTextPaste_JustOverCapDestination_IsRejected()
    {
        // Pins the exact boundary with a destination small enough (2,001 x 2,001 = 4,004,001
        // cells) to safely exercise the pre-fix code path -- which built `edits` unconditionally --
        // without the OOM/hang risk of actually allocating the whole-sheet-scale list from the
        // finding.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var destination = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2001, 2001));

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, destination, [["x"]]);

        command.Should().BeOfType<RejectedWorkbookCommand>();
        var ctx = new TestCommandContext(wb);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("too large to fill with the copied cells");
        outcome.ErrorMessage.Should().Contain("4,004,001");
    }

    [Fact]
    public void ExternalTextPaste_DestinationAtCap_StillPastesSuccessfully()
    {
        // Sibling no-regression case: a destination exactly at the existing internal-paste cap
        // (2,000 x 2,000 = 4,000,000 cells) must still be accepted and tiled normally -- the new
        // guard must not tighten the limit below what the internal path already allows.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var destination = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2000, 2000));

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, destination, [["x"]]);

        command.Should().NotBeOfType<RejectedWorkbookCommand>();
        var ctx = new TestCommandContext(wb);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().BeOfType<TextValue>();
        sheet.GetValue(new CellAddress(sheet.Id, 2000, 2000)).Should().BeOfType<TextValue>();
    }

    [Fact]
    public void ExternalTextPaste_SmallSingleCellDestination_StillPastesSuccessfully()
    {
        // Sibling no-regression case: the ordinary single-cell external paste (no tiling at all)
        // must be entirely unaffected by the new cap.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["hello"]]);

        command.Should().NotBeOfType<RejectedWorkbookCommand>();
        var ctx = new TestCommandContext(wb);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var value = sheet.GetValue(address);
        value.Should().BeOfType<TextValue>();
        ((TextValue)value).Value.Should().Be("hello");
    }
}

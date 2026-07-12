using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R30-clipboard-paste-special-ops-1/3: two related Paste Special gaps around combining Format-only
/// and external-text pastes with an arithmetic Operation.
///
/// ops-1: Paste Special "Formats" mode combined with an arithmetic Operation fell through to the
/// value-combining branch (that branch's own gate required Operation==None before it would take the
/// PasteFormatsCommand shortcut), so "Formats" + Add silently combined the destination/source values
/// instead of copying only the source's formatting -- and copied NO format at all. Fixed by making
/// mode==PasteCellsMode.Formats always take the PasteFormatsCommand path regardless of Operation, at
/// both the non-tiled and tiled call sites, matching how Comments/Validation/ColumnWidths modes
/// already ignore Operation.
///
/// ops-3: an external (non-FreeX) clipboard Text/UnicodeText paste combined with an Operation was a
/// silent no-op, because preserveText forced the pasted text into a TextValue, and
/// PasteArithmetic.ApplyOperation's TryNumber check never accepts a TextValue -- so ApplyOperation
/// always returned null and every cell was skipped (e.g. 10 + "5" left the destination at 10). Fixed
/// by always parsing the external text numerically for the arithmetic (ExternalTextPasteSpecialCommand
/// only ever runs when an Operation is set, so preserveText no longer has any effect there).
/// </summary>
public sealed class R30_ClipboardPasteSpecialOpsTests
{
    // ---- ops-1 --------------------------------------------------------------------------------

    [Fact]
    public void FormatsMode_WithAddOperation_CopiesFormatAndLeavesValueUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 0, 0) });
        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var destinationStyle = wb.RegisterStyle(new CellStyle { Bold = false });
        var destinationCell = Cell.FromValue(new NumberValue(10));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Formats,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Formats-only paste never touches the value, even with an Operation selected.
        sheet.GetValue(destination).Should().Be(new NumberValue(10), "Paste Special Formats must never combine values");
        var pastedStyle = wb.GetStyle(sheet.GetCell(destination)!.StyleId);
        pastedStyle.Bold.Should().BeTrue("the source's format must be copied");
        pastedStyle.FillColor.Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void TiledFormatsMode_WithAddOperation_CopiesFormatAndLeavesValuesUntouched()
    {
        // Same fix, tiled path (destination bigger than the copied source).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var sourceCell = Cell.FromValue(new NumberValue(2));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var destinationStyle = wb.RegisterStyle(new CellStyle { Bold = false });
        var destinationRange = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 2, 4));
        foreach (var addr in destinationRange.AllCells())
        {
            var cell = Cell.FromValue(new NumberValue(10));
            cell.StyleId = destinationStyle;
            sheet.SetCell(addr, cell);
        }

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destinationRange,
            PasteCellsMode.Formats,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        foreach (var addr in destinationRange.AllCells())
        {
            sheet.GetValue(addr).Should().Be(new NumberValue(10), "tiled Formats paste must never combine values either");
            wb.GetStyle(sheet.GetCell(addr)!.StyleId).Bold.Should().BeTrue();
        }
    }

    [Fact]
    public void FormatsMode_NoOperation_StillFormatOnly_NoRegression()
    {
        // Sibling case: Formats + Operation==None (the pre-existing, already-working path) must keep
        // copying only formatting.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(10)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Formats,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(10));
        wb.GetStyle(sheet.GetCell(destination)!.StyleId).Bold.Should().BeTrue();
    }

    [Fact]
    public void ValuesMode_WithAddOperation_StillCombinesValues_NoRegression()
    {
        // Sibling case: a plain Values (not Formats) paste with an Operation must keep combining
        // values exactly as before -- only mode==Formats changed behavior.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceCell = Cell.FromValue(new NumberValue(5));
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(10)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Values,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(15), "Values + Operation must still combine values");
    }

    // ---- ops-3 --------------------------------------------------------------------------------

    [Fact]
    public void ExternalTextPaste_WithAddOperation_CombinesNumerically()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, Cell.FromValue(new NumberValue(10)));

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new GridRange(address, address),
            [["5"]],
            preserveText: true,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(address).Should().Be(new NumberValue(15), "10 + \"5\" must combine numerically, not silently no-op");
    }

    [Fact]
    public void ExternalTextPaste_NoOperation_StillForcesTextWhenPreserveTextSet_NoRegression()
    {
        // Sibling case: preserveText must still force literal text when there is no Operation
        // (the plain-values paste path is untouched by this fix).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            address,
            [["123"]],
            preserveText: true);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(address).Should().Be(new TextValue("123"));
    }

    [Fact]
    public void ExternalTextPaste_WithAddOperation_NonNumericTextLeavesDestinationUnchanged()
    {
        // Sibling case: an external-text Operation paste of genuinely non-numeric text must still
        // leave the destination untouched (matching PasteArithmetic.ApplyOperation's own no-op rule),
        // rather than over-correcting into forcing every external Operation paste to succeed.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, Cell.FromValue(new NumberValue(10)));

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new GridRange(address, address),
            [["West"]],
            preserveText: true,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(address).Should().Be(new NumberValue(10), "non-numeric text combined via Operation must leave the destination unchanged");
    }
}

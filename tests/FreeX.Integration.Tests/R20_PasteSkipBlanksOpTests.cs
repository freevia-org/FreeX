using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R20-paste-special-operations-2: Paste Special Operation (Add/Subtract/Multiply/Divide) combined
/// with Skip Blanks used to silently drop a source cell whose FORMULA currently evaluates to
/// blank/0, instead of applying the operation with the computed value (treated as 0). Root cause:
/// CreateInternalPasteCommand's non-tiled Operation branch correctly checked the ORIGINAL formula
/// cell against Skip Blanks (FormulaText non-null => not blank), but then collapsed it to a plain
/// value cell (Cell.FromValue, dropping FormulaText) before handing it to PasteSpecialCellsCommand,
/// whose own downstream Skip-Blanks re-check saw the now-formula-less, still-blank-valued cell and
/// incorrectly skipped it a second time, leaving the destination untouched. Excel's Skip Blanks only
/// ever skips a source cell that is truly empty (no formula, no value) -- not a live formula that
/// happens to currently compute to blank/0.
/// </summary>
public sealed class R20_paste_special_skipblanks_Tests
{
    [Fact]
    public void PasteSpecialMultiply_SkipBlanks_SourceFormulaEvaluatesToBlank_StillAppliesOperation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // B1 = "=A1" where A1 is empty: a live formula cell whose cached result is currently blank.
        var source = new CellAddress(sheet.Id, 1, 2);
        var sourceCell = new Cell { FormulaText = "=A1", Value = BlankValue.Instance };
        sheet.SetCell(source, sourceCell);

        // D1 = 10, the paste destination (same 1x1 shape as source, so the non-tiled path runs).
        var destination = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(10)));

        var options = new PasteSpecialOptions(Operation: PasteSpecialOperation.Multiply, SkipBlanks: true);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            workbook,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            options);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Excel: existing(10) * source-as-0 = 0. FreeX pre-fix left the destination unchanged at 10
        // because the collapsed source cell was mistaken for truly blank and skipped a second time.
        sheet.GetValue(destination).Should().Be(new NumberValue(0));

        command.Revert(ctx);

        sheet.GetValue(destination).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void PasteSpecialMultiply_SkipBlanks_TrulyEmptySourceCell_IsSkipped()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // B1 is genuinely empty: no formula, no value.
        var source = new CellAddress(sheet.Id, 1, 2);
        var sourceCell = Cell.FromValue(BlankValue.Instance);

        var destination = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(10)));

        var options = new PasteSpecialOptions(Operation: PasteSpecialOperation.Multiply, SkipBlanks: true);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            workbook,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            options);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // A truly empty source cell must still be skipped by Skip Blanks -- destination untouched.
        sheet.GetValue(destination).Should().Be(new NumberValue(10));
    }
}

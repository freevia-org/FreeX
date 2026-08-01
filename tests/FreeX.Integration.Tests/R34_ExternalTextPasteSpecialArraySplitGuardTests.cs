using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R34-commands-paste-special-3-1: ExternalTextPasteSpecialCommand.Apply (the external-clipboard
/// counterpart of Paste Special's arithmetic Operation, e.g. pasting a copied Notepad number with
/// Add/Subtract/Multiply/Divide) never called CommandGuards.RejectIfSplitsArray before mutating the
/// destination cell, unlike every other content-mutating paste command (PasteCellsCommand,
/// PasteSpecialCellsCommand, EditCellsCommand). That let it silently overwrite a single member of a
/// live dynamic-array spill (or legacy CSE array) in the raw cell dictionary while the spill's
/// _spillAnchors/_spillValues bookkeeping -- keyed off the anchor -- was left untouched, desyncing the
/// cached spill state instead of being rejected with Excel's "You cannot change part of an array."
/// Fixed by adding the same RejectIfSplitsArray guard used by its siblings.
/// </summary>
public sealed class R34_ExternalTextPasteSpecialArraySplitGuardTests
{
    private const string CannotChangePartOfArrayMessage = "You cannot change part of an array.";

    private static (Workbook Workbook, Sheet Sheet, CellAddress Anchor, ICommandContext Ctx) MakeLiveSpillSetup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3,1)"));
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills to A1:A3
        return (wb, sheet, anchor, new TestCommandContext(wb));
    }

    [Fact]
    public void ExternalTextPasteWithOperation_OnNonAnchorSpillMember_IsBlocked()
    {
        var (_, sheet, _, ctx) = MakeLiveSpillSetup();
        var member = new CellAddress(sheet.Id, 2, 1); // A2 - spill member, not anchor

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new GridRange(member, member),
            [["5"]],
            preserveText: true,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        // The spill member must be left untouched -- no silent overwrite/desync.
        sheet.GetValue(member).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void ExternalTextPasteWithOperation_OnAnchorAlone_ForDynamicArray_IsAllowed()
    {
        // R112-array-anchor-edit: real Excel always allows replacing a modern dynamic array's
        // anchor cell directly, including via a Paste Special arithmetic Operation -- the paste
        // touches only the single formula/anchor cell, the same shape as retyping it, and
        // naturally clears the old spill via SetCell's ClearSpillRange side effect. This is the
        // sibling fix for the same RejectIfSplitsArray choke point used by EditCellsCommand.
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var member = new CellAddress(sheet.Id, 2, 1); // A2 - was part of the old spill

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new GridRange(anchor, anchor),
            [["5"]],
            preserveText: true,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // The anchor's formula is gone -- replaced by the arithmetic-combine result (its prior raw
        // value was Blank/0 in this synthetic setup, matching ExternalTextPasteWithOperation_OnEntireSpillRange_IsAllowed's
        // documented "starts from Blank" behavior for this command).
        sheet.GetCell(anchor)!.FormulaText.Should().BeNull();
        sheet.GetValue(anchor).Should().Be(new NumberValue(5));
        // The old spill must have been vacated by the anchor's own SetCell, not left dangling.
        sheet.GetValue(member).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void ExternalTextPasteWithOperation_OnLegacyArrayAnchorAlone_IsStillBlocked()
    {
        // No-regression sibling: a legacy CSE array's anchor is NOT the modern-dynamic-array
        // exception -- Excel still requires the whole declared range to be selected/edited as a
        // unit, distinguished here via Cell.LegacyArrayRows/LegacyArrayCols.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        var legacyCell = Cell.FromFormula("A10:A11+A20:A21");
        legacyCell.LegacyArrayRows = 3;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(anchor, legacyCell);
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells));
        var ctx = new TestCommandContext(wb);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new GridRange(anchor, anchor),
            [["5"]],
            preserveText: true,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        sheet.GetCell(anchor)!.FormulaText.Should().Be("A10:A11+A20:A21");
    }

    [Fact]
    public void ExternalTextPasteWithOperation_OnNormalCell_StillCombinesNumerically_NoRegression()
    {
        // Sibling case: a plain (non-array) destination cell must still be able to receive an
        // external-clipboard arithmetic-Operation paste exactly as before the guard was added.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 5, 6);
        sheet.SetCell(address, Cell.FromValue(new NumberValue(10)));

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new GridRange(address, address),
            [["5"]],
            preserveText: true,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void ExternalTextPasteWithOperation_OnEntireSpillRange_IsAllowed()
    {
        // Sibling case: selecting and pasting over the WHOLE array range at once (not splitting it)
        // must still be permitted, matching every other RejectIfSplitsArray-guarded command.
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var whole = new GridRange(anchor, new CellAddress(sheet.Id, 3, 1)); // A1:A3

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            whole,
            [["10"], ["20"], ["30"]],
            preserveText: true,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Only the anchor has a raw stored Cell in this synthetic setup (spill members exist only
        // via the spill's cached RangeValue, never as their own Cell); ExternalTextPasteSpecialCommand
        // combines against sheet.GetCell (the raw dictionary), so every destination here starts from
        // Blank (0) regardless of what GetValue reported pre-paste -- the point of this test is only
        // that the whole-range paste is allowed (not rejected), not the arithmetic result.
        sheet.GetValue(anchor).Should().Be(new NumberValue(10));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(20));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new NumberValue(30));
    }
}

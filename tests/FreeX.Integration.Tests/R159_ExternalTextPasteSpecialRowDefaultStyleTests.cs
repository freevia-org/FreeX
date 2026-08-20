using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// freex-copy-paste-formats F2: ExternalTextPasteSpecialCommand.Apply (Paste Special's
/// Add/Subtract/Multiply/Divide "Operation" applied to plain text pasted from OUTSIDE FreeX, e.g.
/// Notepad/a browser) rebuilt the destination cell's style from the row's/column's whole-row/column
/// default style whenever one existed, even though the destination cell already carried its own
/// explicit style:
///
///     var existing = sheet.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance);
///     existing.StyleId = sheet.GetStyleOnly(address.Row, address.Col) ?? existing.StyleId;
///
/// GetStyleOnly(row, col) falls back cell &gt; row &gt; column (see Sheet.StyleOnly.cs), but it is
/// only ever populated for cells that carry NO cell record at all (style-only overrides for empty
/// cells) -- it has no knowledge of an existing cell's own StyleId, so unconditionally overwriting
/// existing.StyleId with its result silently threw away the destination's real per-cell style
/// whenever the row/column happened to have a default style registered, replacing it with that
/// unrelated default. Fixed to prefer the destination cell's own StyleId first, matching the
/// correctly-ordered IsDestinationTextFormatted helper 65 lines above in the same file.
/// </summary>
public sealed class R159_ExternalTextPasteSpecialRowDefaultStyleTests
{
    [Fact]
    public void ArithmeticOperation_OnCellWithOwnStyle_InRowWithDefaultStyle_PreservesCellsOwnStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var destination = new CellAddress(sheet.Id, 2, 1); // B2

        // The whole row has an unrelated default style (e.g. imported from an XLSX with row-level
        // custom formatting) -- GetStyleOnly(row, col) falls through to this for any cell lacking its
        // own style-only entry.
        var rowDefaultStyle = wb.RegisterStyle(new CellStyle { Bold = false, FillColor = new CellColor(0, 255, 0) });
        sheet.RowStyles[destination.Row] = rowDefaultStyle;

        // The destination cell itself carries its OWN explicit style, distinct from the row default.
        var ownStyle = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 0, 0) });
        var destinationCell = Cell.FromValue(new NumberValue(10));
        destinationCell.StyleId = ownStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new GridRange(destination, destination),
            [["5"]],
            preserveText: true,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // The value combines as expected...
        sheet.GetValue(destination).Should().Be(new NumberValue(15));

        // ...but the cell's own style must survive, not get clobbered by the row's default style.
        var resultCell = sheet.GetCell(destination);
        resultCell.Should().NotBeNull();
        resultCell!.StyleId.Should().Be(ownStyle, "the destination cell's own explicit style must win over the row's unrelated default style");
        var resultStyle = wb.GetStyle(resultCell.StyleId);
        resultStyle.Bold.Should().BeTrue();
        resultStyle.FillColor.Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void ArithmeticOperation_OnBlankCellInRowWithDefaultStyle_StillPicksUpRowDefaultStyle_NoRegression()
    {
        // Sibling case: when the destination cell has NO style of its own (genuinely blank/absent),
        // the row's default style is the correct fallback -- this must keep working exactly as
        // before (that is the whole point of GetStyleOnly's cell > row > column fallback).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var destination = new CellAddress(sheet.Id, 2, 1); // B2, no cell present at all

        var rowDefaultStyle = wb.RegisterStyle(new CellStyle { Bold = false, FillColor = new CellColor(0, 255, 0) });
        sheet.RowStyles[destination.Row] = rowDefaultStyle;

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new GridRange(destination, destination),
            [["5"]],
            preserveText: true,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(5));

        var resultCell = sheet.GetCell(destination);
        resultCell.Should().NotBeNull();
        resultCell!.StyleId.Should().Be(rowDefaultStyle, "a genuinely blank destination cell should still pick up the row's default style");
    }
}

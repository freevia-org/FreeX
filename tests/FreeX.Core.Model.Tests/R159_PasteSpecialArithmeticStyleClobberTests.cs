using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R159 fix wave, freex-copy-paste-formats F1: an arithmetic Paste Special (Add/Subtract/etc.) onto
/// a destination cell that already has its own explicit style must keep that cell's own style, even
/// when the destination's row (or column) carries an unrelated whole-row/whole-column default style.
/// Before the fix, PasteSpecialCellsCommand.TryBuildCell unconditionally overwrote the cloned
/// existing-cell's StyleId with Sheet.GetStyleOnly(...), which falls through to the row/column
/// default regardless of whether the destination cell already had its own real style -- silently
/// clobbering the cell's own formatting (e.g. Bold + a colored fill) with the row's default style.
/// </summary>
public sealed class R159_PasteSpecialArithmeticStyleClobberTests
{
    [Fact]
    public void PasteSpecialCellsCommand_AddOperation_PreservesDestinationCellsOwnStyle_OverRowDefault()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);

        // The whole row carries an unrelated default style (e.g. imported row banding / customFormat).
        var rowDefaultStyle = wb.RegisterStyle(new CellStyle { FillColor = CellColor.FromArgb(0, 128, 0) });
        sheet.RowStyles[dest.Row] = rowDefaultStyle;

        // The destination cell itself has its own explicit style (Bold + a red fill) distinct from
        // the row default.
        var ownFill = CellColor.FromArgb(255, 0, 0);
        var ownStyle = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = ownFill });
        sheet.SetCell(dest, new Cell { Value = new NumberValue(10), StyleId = ownStyle });

        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(3)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(new NumberValue(13));

        var finalStyle = wb.GetStyle(sheet.GetCell(dest)!.StyleId);
        finalStyle.Bold.Should().BeTrue("the destination cell's own style must survive an arithmetic Paste Special");
        finalStyle.FillColor.Should().Be(ownFill, "the row's unrelated default fill must not clobber the cell's own fill");
    }

    /// <summary>
    /// No-regression sibling: when the destination cell is truly blank (no real Cell object) but
    /// carries a style-only override, that style-only override -- including one that resolves via
    /// the row/column default fallback -- must still be applied to the newly-created cell, exactly
    /// as PasteSpecialCellsCommand_AddOperationPreservesStyleOnlyDestination already covers for a
    /// per-cell style-only entry.
    /// </summary>
    [Fact]
    public void PasteSpecialCellsCommand_AddOperation_StillAppliesRowDefaultStyle_ToTrulyBlankDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);

        var rowDefaultStyle = wb.RegisterStyle(new CellStyle { FillColor = CellColor.FromArgb(0, 128, 0) });
        sheet.RowStyles[dest.Row] = rowDefaultStyle;
        // No cell at `dest` at all -- sheet.GetCell(dest) is null.

        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(3)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(new NumberValue(3));
        sheet.GetCell(dest)!.StyleId.Should().Be(rowDefaultStyle);
    }
}

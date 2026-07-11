using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R21-clipboard-paste-special-deep-3: Paste Special "All Except Borders" is supposed to exclude
/// ALL border formatting from the source cell, but MergeAllExceptBorders only overwrote the four
/// straight-edge borders (Top/Right/Bottom/Left) with the destination's values, leaving the source
/// cell's diagonal borders (BorderDiagonalDown/BorderDiagonalUp) cloned through untouched. A source
/// cell with a diagonal border and a plain destination with none would end up with the destination
/// silently gaining a diagonal border it never had and the dialog never warned about.
/// </summary>
public sealed class R21_PasteAllExceptBordersDiagonalTests
{
    [Fact]
    public void PasteCommandFactory_AllExceptBorders_ExcludesSourceDiagonalBorders()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)),
            BorderDiagonalDown = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)),
            BorderDiagonalUp = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0))
        });
        // Destination has no borders of any kind, including no diagonals.
        var destinationStyle = wb.RegisterStyle(new CellStyle());
        var sourceCell = Cell.FromValue(new NumberValue(42));
        sourceCell.StyleId = sourceStyle;
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders));

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.Value.Should().Be(new NumberValue(42));
        var style = wb.GetStyle(pasted.StyleId);
        // Non-border formatting still comes through.
        style.Bold.Should().BeTrue();
        // All border formatting -- straight edges AND diagonals -- must be excluded, matching the
        // (borderless) destination, not leaked through from the source.
        style.BorderTop.Style.Should().Be(BorderStyle.None);
        style.BorderDiagonalDown.Style.Should().Be(BorderStyle.None);
        style.BorderDiagonalUp.Style.Should().Be(BorderStyle.None);
    }
}

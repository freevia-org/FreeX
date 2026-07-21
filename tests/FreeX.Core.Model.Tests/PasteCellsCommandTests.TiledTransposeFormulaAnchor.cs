using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for review finding R57-meta-1: a TILED transpose paste (destination an exact
/// multiple of the transposed source block's size) must transpose each replica tile's formulas
/// against that TILE'S OWN destination-block anchor, not the overall (tile-1) destination anchor.
/// Before the fix, every replica tile beyond the first copied tile-1's rewritten formula verbatim
/// instead of re-anchoring its references to its own tile position.
/// </summary>
public sealed partial class PasteCellsCommandTests
{
    [Fact]
    public void PasteCommandFactory_TiledTransposeRebasesFormulaAgainstOwnTileAnchor()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source: A1:B1 (1 row x 2 cols). A1 = 5, B1 = "=A1*2".
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var aCell = Cell.FromValue(new NumberValue(5));
        var bCell = Cell.FromFormula("A1*2");
        sheet.SetCell(a1, aCell);
        sheet.SetCell(b1, bCell);

        // Destination: D1:D4 (4 rows x 1 col) -- exactly 2x the transposed block's height (2 rows),
        // so this triggers the tiled paste path with tile 1 = D1:D2 and tile 2 = D3:D4.
        var destinationStart = new CellAddress(sheet.Id, 1, 4);
        var destinationEnd = new CellAddress(sheet.Id, 4, 4);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(a1, b1),
            [(a1, aCell.Clone()), (b1, bCell.Clone())],
            new GridRange(destinationStart, destinationEnd),
            PasteCellsMode.All,
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        // Tile 1 (D1:D2): transposes exactly like the single-tile case -- D2 references D1 (its own
        // tile's sibling cell), the cross-reference B1 had to A1.
        sheet.GetValue(1, 4).Should().Be(new NumberValue(5)); // D1
        sheet.GetCell(new CellAddress(sheet.Id, 2, 4))!.FormulaText.Should().Be("D1*2"); // D2

        // Tile 2 (D3:D4): must transpose against ITS OWN tile anchor (D3), not tile 1's anchor (D1).
        // Real Excel produces D4 = "=D3*2"; the pre-fix bug produced "=D1*2" (tile-1's anchor reused).
        sheet.GetValue(3, 4).Should().Be(new NumberValue(5)); // D3
        sheet.GetCell(new CellAddress(sheet.Id, 4, 4))!.FormulaText.Should().Be("D3*2"); // D4
    }

    [Fact]
    public void PasteCommandFactory_TiledTransposeRebasesFormulaAcrossThreeReplicaTiles()
    {
        // Sibling no-regression test: the same fix path (CreateTiledInternalPasteCommand's Transpose
        // branch) generalizes correctly beyond two tiles -- every one of THREE replica tiles must
        // rebase its formula against its own tile anchor (D1/D3/D5), not just the first replica.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var aCell = Cell.FromValue(new NumberValue(5));
        var bCell = Cell.FromFormula("A1*2");
        sheet.SetCell(a1, aCell);
        sheet.SetCell(b1, bCell);

        // Destination D1:D6 -- exactly 3x the transposed block's height (2 rows): tiles are
        // D1:D2, D3:D4, D5:D6.
        var destinationStart = new CellAddress(sheet.Id, 1, 4);
        var destinationEnd = new CellAddress(sheet.Id, 6, 4);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(a1, b1),
            [(a1, aCell.Clone()), (b1, bCell.Clone())],
            new GridRange(destinationStart, destinationEnd),
            PasteCellsMode.All,
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 4).Should().Be(new NumberValue(5)); // D1
        sheet.GetCell(new CellAddress(sheet.Id, 2, 4))!.FormulaText.Should().Be("D1*2"); // D2
        sheet.GetValue(3, 4).Should().Be(new NumberValue(5)); // D3
        sheet.GetCell(new CellAddress(sheet.Id, 4, 4))!.FormulaText.Should().Be("D3*2"); // D4
        sheet.GetValue(5, 4).Should().Be(new NumberValue(5)); // D5
        sheet.GetCell(new CellAddress(sheet.Id, 6, 4))!.FormulaText.Should().Be("D5*2"); // D6
    }
}

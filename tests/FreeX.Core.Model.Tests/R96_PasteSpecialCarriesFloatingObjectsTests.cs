using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R96-cmd-paste-special-floating-objects: <see cref="PasteCommandFactory.CreateInternalPasteCommand"/>'s
/// "special options" branch (mode==All plus SkipBlanks/Transpose/a non-Default ContentKind, with a
/// single-cell destination so tiling never kicks in) never carried comments or anchored
/// pictures/shapes/textboxes/charts -- unlike the plain-paste branch (R91/R92) and the tiled branch,
/// which both do. Excel's Paste Special carries comments/floating objects along exactly like a plain
/// Ctrl+V whenever a full-content paste (All / All except borders / All using source theme / Values and
/// source formatting) is combined with Skip Blanks or Transpose -- those options only change how the
/// cell grid is filled, not whether comments/objects travel with the paste. These tests drive the real
/// production entry point with non-default <see cref="PasteSpecialOptions"/> and a single-cell
/// destination (the overwhelmingly common "click one cell, Paste Special" workflow).
/// </summary>
public sealed class R96_PasteSpecialCarriesFloatingObjectsTests
{
    private static (Workbook wb, Sheet sheet, TestCommandContext ctx) MakeWorkbook()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        return (wb, sheet, ctx);
    }

    private static List<(CellAddress, Cell)> SourceCells(Sheet sheet, GridRange sourceRange)
    {
        sheet.SetCell(sourceRange.Start, Cell.FromValue(new TextValue("hi")));
        return sourceRange.AllCells()
            .Select(a => (a, sheet.GetCell(a) ?? Cell.FromValue(BlankValue.Instance)))
            .ToList();
    }

    [Fact]
    public void PasteSpecial_SkipBlanksCarriesShapeAnchoredInsideCopiedRange()
    {
        var (wb, sheet, ctx) = MakeWorkbook();
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Kind = DrawingShapeKind.Rectangle };
        sheet.DrawingShapes.Add(shape);

        var options = new PasteSpecialOptions(SkipBlanks: true);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, options);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.DrawingShapes.Should().HaveCount(2, "Paste Special > All with Skip Blanks must still carry the anchored shape, exactly like a plain Ctrl+V");
        var pasted = sheet.DrawingShapes.Single(s => s.Id != shape.Id);
        pasted.Anchor.Row.Should().Be(destination.Row);
        pasted.Anchor.Col.Should().Be(destination.Col);

        command.Revert(ctx);
        sheet.DrawingShapes.Should().ContainSingle().Which.Id.Should().Be(shape.Id);
    }

    [Fact]
    public void PasteSpecial_SkipBlanksCarriesTextBoxAnchoredInsideCopiedRange()
    {
        var (wb, sheet, ctx) = MakeWorkbook();
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 2, 2), Text = "hello" };
        sheet.TextBoxes.Add(textBox);

        var options = new PasteSpecialOptions(SkipBlanks: true);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, options);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.TextBoxes.Should().HaveCount(2);
        var pasted = sheet.TextBoxes.Single(t => t.Id != textBox.Id);
        pasted.Anchor.Row.Should().Be(destination.Row + 1);
        pasted.Anchor.Col.Should().Be(destination.Col + 1);
        pasted.Text.Should().Be(textBox.Text);

        command.Revert(ctx);
        sheet.TextBoxes.Should().ContainSingle().Which.Id.Should().Be(textBox.Id);
    }

    [Fact]
    public void PasteSpecial_SkipBlanksCarriesChartAnchoredInsideCopiedRange()
    {
        var (wb, sheet, ctx) = MakeWorkbook();
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        var chart = new ChartModel { Left = 70, Top = 25, Width = 200, Height = 150 };
        sheet.Charts.Add(chart);

        var options = new PasteSpecialOptions(SkipBlanks: true);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, options);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Charts.Should().HaveCount(2, "Paste Special > All with Skip Blanks must still carry the anchored chart");

        command.Revert(ctx);
        sheet.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);
    }

    [Fact]
    public void PasteSpecial_TransposeCarriesCommentAtTransposedDestination()
    {
        var (wb, sheet, ctx) = MakeWorkbook();
        // Two-cell horizontal source range B2:C2 so transpose actually moves the second cell.
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        var destination = new CellAddress(sheet.Id, 10, 10);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("a")));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("b")));
        var sourceCells = sourceRange.AllCells()
            .Select(a => (a, sheet.GetCell(a) ?? Cell.FromValue(BlankValue.Instance)))
            .ToList();

        sheet.Comments[new CellAddress(sheet.Id, 1, 2)] = "note on second source cell";

        var options = new PasteSpecialOptions(Transpose: true);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, options);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Source (1,1)->(1,2) is a column offset of +1; transposed, that becomes a ROW offset of +1
        // at the destination, i.e. (destination.Row + 1, destination.Col).
        var transposedAddress = new CellAddress(sheet.Id, destination.Row + 1, destination.Col);
        sheet.Comments.Should().ContainKey(transposedAddress)
            .WhoseValue.Should().Be("note on second source cell");

        command.Revert(ctx);
        sheet.Comments.Should().NotContainKey(transposedAddress);
        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, 1, 2));
    }

    [Fact]
    public void PasteSpecial_ValuesModeDoesNotCarryShape()
    {
        // No-regression sibling: Paste Special "Values" (mode != All) must NOT bring a shape along,
        // matching the plain-paste branch's identical mode gate (R92's PasteValuesDoesNotCarryTextBox).
        var (wb, sheet, ctx) = MakeWorkbook();
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.DrawingShapes.Add(shape);

        var options = new PasteSpecialOptions(SkipBlanks: true);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.Values, options);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.DrawingShapes.Should().ContainSingle("Paste Special > Values must not carry the shape along");
    }
}

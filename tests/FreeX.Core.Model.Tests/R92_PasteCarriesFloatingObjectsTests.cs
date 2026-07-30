using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R92-cmd-paste-floating-objects: r91 added floating-object carry to a plain Ctrl+C/Ctrl+V of a
/// cell range, but PasteCommandFactory.FindPicturesAnchoredIn only ever consulted sheet.Pictures --
/// a Chart, DrawingShape (incl. WordArt), or TextBox anchored inside the copied range was silently
/// left behind. This test drives the real <see cref="PasteCommandFactory.CreateInternalPasteCommand"/>
/// entry point (the same one production paste/Ctrl+V reaches) for each of the three previously-missed
/// kinds, mirroring R91_PasteCarriesFloatingPicturesTests's shape exactly.
/// </summary>
public sealed class R92_PasteCarriesFloatingObjectsTests
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

    // ---------------------------------------------------------------- DrawingShape

    [Fact]
    public void InternalPaste_PlainPasteCarriesShapeAnchoredInsideCopiedRange()
    {
        var (wb, sheet, ctx) = MakeWorkbook();
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Kind = DrawingShapeKind.Rectangle };
        sheet.DrawingShapes.Add(shape);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, default);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.DrawingShapes.Should().HaveCount(2, "the original shape stays put and a new copy is created at the destination");
        var pasted = sheet.DrawingShapes.Single(s => s.Id != shape.Id);
        pasted.Anchor.Row.Should().Be(destination.Row);
        pasted.Anchor.Col.Should().Be(destination.Col);
        pasted.Kind.Should().Be(shape.Kind);

        command.Revert(ctx);
        sheet.DrawingShapes.Should().ContainSingle().Which.Id.Should().Be(shape.Id);
    }

    [Fact]
    public void InternalPaste_ShapeAnchoredOutsideCopiedRangeIsNotCarried()
    {
        var (wb, sheet, ctx) = MakeWorkbook();
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        var outsideShape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 20, 20) };
        sheet.DrawingShapes.Add(outsideShape);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, default);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.DrawingShapes.Should().ContainSingle().Which.Id.Should().Be(outsideShape.Id);
    }

    // ---------------------------------------------------------------- TextBox

    [Fact]
    public void InternalPaste_PlainPasteCarriesTextBoxAnchoredInsideCopiedRange()
    {
        var (wb, sheet, ctx) = MakeWorkbook();
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 2, 2), Text = "hello" };
        sheet.TextBoxes.Add(textBox);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, default);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.TextBoxes.Should().HaveCount(2);
        var pasted = sheet.TextBoxes.Single(t => t.Id != textBox.Id);
        // anchored at (2,2) inside B2:D4 (offset +1,+1 from range start (1,1)) -> destination (11,11)
        pasted.Anchor.Row.Should().Be(destination.Row + 1);
        pasted.Anchor.Col.Should().Be(destination.Col + 1);
        pasted.Text.Should().Be(textBox.Text);

        command.Revert(ctx);
        sheet.TextBoxes.Should().ContainSingle().Which.Id.Should().Be(textBox.Id);
    }

    [Fact]
    public void InternalPaste_PasteValuesDoesNotCarryTextBox()
    {
        // No-regression sibling: Paste Special "Values" must NOT bring a text box along.
        var (wb, sheet, ctx) = MakeWorkbook();
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.TextBoxes.Add(textBox);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.Values, default);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.TextBoxes.Should().ContainSingle("Paste Values must not carry the text box along");
    }

    // ---------------------------------------------------------------- Chart

    [Fact]
    public void InternalPaste_PlainPasteCarriesChartAnchoredInsideCopiedRange()
    {
        var (wb, sheet, ctx) = MakeWorkbook();
        // B2:D4 (rows/cols 1..3, 0-based) -- default row height 20, default col width 8.43*8 = 67.44
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        // Chart's top-left corner sits inside the B2:D4 pixel box (col 1 starts at 67.44, row 1 starts at 20).
        var chart = new ChartModel { Left = 70, Top = 25, Width = 200, Height = 150 };
        sheet.Charts.Add(chart);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, default);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Charts.Should().HaveCount(2, "the original chart stays put and a new copy is created at the destination");
        var pasted = sheet.Charts.Single(c => c.Id != chart.Id);

        // Destination top-left pixel position for row/col 10 with all-default sizing:
        var expectedDestLeft = 10 * sheet.DefaultColumnWidth * 8;
        var expectedDestTop = 10 * sheet.DefaultRowHeight;
        var sourceLeft = 1 * sheet.DefaultColumnWidth * 8;
        var sourceTop = 1 * sheet.DefaultRowHeight;
        var expectedLeft = expectedDestLeft + (chart.Left - sourceLeft);
        var expectedTop = expectedDestTop + (chart.Top - sourceTop);
        pasted.Left.Should().BeApproximately(expectedLeft, 0.01);
        pasted.Top.Should().BeApproximately(expectedTop, 0.01);
        pasted.Width.Should().Be(chart.Width);
        pasted.Height.Should().Be(chart.Height);

        command.Revert(ctx);
        sheet.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);
    }

    [Fact]
    public void InternalPaste_ChartAnchoredOutsideCopiedRangeIsNotCarried()
    {
        var (wb, sheet, ctx) = MakeWorkbook();
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)); // B2 only
        var destination = new CellAddress(sheet.Id, 10, 10);
        var sourceCells = SourceCells(sheet, sourceRange);

        // Far away, well outside the single-cell B2 pixel box.
        var outsideChart = new ChartModel { Left = 5000, Top = 5000, Width = 200, Height = 150 };
        sheet.Charts.Add(outsideChart);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, default);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Charts.Should().ContainSingle().Which.Id.Should().Be(outsideChart.Id);
    }
}

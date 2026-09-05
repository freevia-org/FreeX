using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r437: a table cell's own fill and borders must survive a .pptx round trip.
///
/// <para>The last uncovered part of the table after r423 (structure), r424 (styling flags) and r427
/// (cell text). Cell-level fill and borders are the OVERRIDES an author applies to individual cells
/// -- a highlighted total row, a boxed heading -- on top of whatever the table style paints. That is
/// what makes their loss quiet: the cell falls back to the table style and still looks styled, just
/// no longer emphasised. The table remains attractive and stops saying what the author meant it to.</para>
/// </summary>
public sealed class R437_TableCellFillAndBordersReachTheFileTests
{
    private static TableCell CellWith(string text, Action<TableCell>? configure = null)
    {
        var cell = new TableCell { TextBody = new TextBody() };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        cell.TextBody!.Paragraphs.Add(paragraph);
        configure?.Invoke(cell);
        return cell;
    }

    private static TableShape RoundTrip(Action<TableShape> configure)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.AddRange([914400, 914400]);

        var row = new TableRow { HeightEmu = 370840 };
        row.Cells.Add(CellWith("first"));
        row.Cells.Add(CellWith("second"));
        table.Rows.Add(row);

        configure(table);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Table",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 400000,
            Table = table,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var shape = PptxPackageReader.Read(stream).Slides[0].Shapes.FirstOrDefault();
        shape?.Table.Should().NotBeNull("the table must survive before its cells can be judged");
        return shape!.Table!;
    }

    [Fact]
    public void ACellFillSurvives()
    {
        var table = RoundTrip(configured =>
            configured.Rows[0].Cells[0].Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x33AA66)));

        var fill = table.Rows[0].Cells[0].Fill.Should().BeOfType<ShapeFill.Solid>(
            "a highlighted cell that loses its fill falls back to the table style and stops being emphasised").Subject;

        fill.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x33AA66), "the colour is the emphasis");
    }

    [Fact]
    public void CellBordersSurviveEdgeByEdge()
    {
        // Four edges, four DIFFERENT widths. A writer that emitted one edge for all of them, or
        // transposed left and right, passes a test using identical borders and fails this one --
        // the same reasoning r420 used for FreeX cell borders.
        var table = RoundTrip(configured =>
            configured.Rows[0].Cells[0].Borders = new TableCellBorders
            {
                Left = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0xFF0000)), widthPt: 1.0),
                Right = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0x00FF00)), widthPt: 2.0),
                Top = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0x0000FF)), widthPt: 3.0),
                Bottom = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0xFFFF00)), widthPt: 4.0),
            });

        var borders = table.Rows[0].Cells[0].Borders;
        borders.Should().NotBeNull("a boxed cell that loses its borders is no longer boxed");

        borders!.Left.Should().BeOfType<ShapeOutline.Visible>().Subject.WidthPt.Should().Be(1.0);
        borders.Right.Should().BeOfType<ShapeOutline.Visible>().Subject.WidthPt.Should().Be(2.0);
        borders.Top.Should().BeOfType<ShapeOutline.Visible>().Subject.WidthPt.Should().Be(3.0);
        borders.Bottom.Should().BeOfType<ShapeOutline.Visible>().Subject.WidthPt.Should().Be(4.0, "each edge keeps its OWN width");
    }

    [Fact]
    public void ACellFillStaysInItsOwnCell()
    {
        // The assertion a single-cell test cannot make: a writer that emitted one cell's fill for the
        // whole row would satisfy every case above while repainting the neighbour.
        var table = RoundTrip(configured =>
            configured.Rows[0].Cells[0].Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x33AA66)));

        table.Rows[0].Cells[0].Fill.Should().BeOfType<ShapeFill.Solid>();
        table.Rows[0].Cells[1].Fill.Should().BeNull("the neighbouring cell was never filled and must not inherit one");
    }

    [Fact]
    public void APlainCellGainsNoFillOrBorders()
    {
        // Every assertion above checks that something set survives, so a reader that invented a fill
        // would satisfy them -- and an invented cell fill emphasises a row the author did not choose.
        var table = RoundTrip(_ => { });

        table.Rows[0].Cells[0].Fill.Should().BeNull("a plain cell must not acquire a fill");
        table.Rows[0].Cells[0].Borders.Should().BeNull("nor borders it never had");
    }
}

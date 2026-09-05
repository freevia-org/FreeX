using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r427: text formatting inside a TABLE CELL must survive a .pptx round trip.
///
/// <para>The intersection of two earlier rounds, and covered by neither. r423 checked that a cell's
/// TEXT survives; r425 checked that a run's FORMATTING survives in a shape's own text body. A table
/// cell's text body travels a different writer path from a shape's -- cells are built inside
/// <c>a:tbl</c> rather than <c>p:txBody</c> -- so formatting could be correct in one and dropped in
/// the other, and each earlier test would still pass.</para>
///
/// <para><b>Correction, after checking rather than assuming.</b> The claim above that a cell travels
/// a wholly different writer path is only half true. The cell's <c>a:txBody</c> WRAPPER is built
/// separately -- A namespace, its own bodyPr and lstStyle, its own empty-paragraph rule -- but the
/// paragraphs inside go through the SAME <c>BuildParaEl</c>/<c>BuildRunEl</c> the shape path uses. So
/// r425 already covered how a run's properties serialise; what was genuinely uncovered is that the
/// cell path REACHES that shared builder at all, and that formatting does not leak between cells.</para>
///
/// <para>The leakage case is the one no single-cell test can make, and is why this is worth keeping
/// after that correction: a writer emitting one cell's run properties for the whole row would
/// satisfy every other assertion here.</para>
/// </summary>
public sealed class R427_TableCellTextFormattingReachesTheFileTests
{
    private static TableCell CellWith(string text, Action<Run>? configure = null)
    {
        var cell = new TableCell { TextBody = new TextBody() };
        var paragraph = new Paragraph();
        var run = new Run { Text = text };
        configure?.Invoke(run);
        paragraph.Runs.Add(run);
        cell.TextBody!.Paragraphs.Add(paragraph);
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
        shape?.Table.Should().NotBeNull("the table must survive before its cell text can be judged");
        return shape!.Table!;
    }

    private static Run FirstRun(TableShape table, int column) =>
        table.Rows[0].Cells[column].TextBody!.Paragraphs.First().Runs.First();

    [Fact]
    public void CellRunLanguageSurvives()
    {
        var table = RoundTrip(configured =>
            configured.Rows[0].Cells[0] = CellWith("first", run => run.Language = "fr-FR"));

        FirstRun(table, 0).Language.Should().Be(
            "fr-FR", "a cell run carries its own language for spell-check and screen readers");
    }

    [Fact]
    public void CellRunCharacterSpacingSurvives()
    {
        var table = RoundTrip(configured =>
            configured.Rows[0].Cells[0] = CellWith("first", run => run.CharacterSpacingHundredthsPt = 150));

        FirstRun(table, 0).CharacterSpacingHundredthsPt.Should().Be(
            150, "run properties inside a table cell travel a different writer path from a shape's");
    }

    [Fact]
    public void CellRunUnderlineTokenSurvives()
    {
        var table = RoundTrip(configured =>
            configured.Rows[0].Cells[0] = CellWith("first", run => run.UnderlineStyleToken = "sng"));

        FirstRun(table, 0).UnderlineStyleToken.Should().Be("sng");
    }

    [Fact]
    public void FormattingStaysInItsOwnCell()
    {
        // The assertion the single-cell tests cannot make: formatting applied to one cell must not
        // leak into its neighbour. A writer that emitted one cell's run properties for the whole row
        // would pass every test above.
        var table = RoundTrip(configured =>
        {
            configured.Rows[0].Cells[0] = CellWith("first", run => run.CharacterSpacingHundredthsPt = 150);
            configured.Rows[0].Cells[1] = CellWith("second");
        });

        FirstRun(table, 0).CharacterSpacingHundredthsPt.Should().Be(150);
        FirstRun(table, 1).CharacterSpacingHundredthsPt.Should().BeNull(
            "the neighbouring cell was never formatted and must not inherit it");
        FirstRun(table, 1).Text.Should().Be("second", "and it must still be the right cell");
    }

    [Fact]
    public void APlainCellRunGainsNoFormatting()
    {
        // Every assertion above checks that something set survives, so a reader that invented values
        // would satisfy them all.
        var table = RoundTrip(_ => { });
        var run = FirstRun(table, 0);

        run.Language.Should().BeNull("a plain cell run must not acquire a language");
        run.CharacterSpacingHundredthsPt.Should().BeNull();
        run.UnderlineStyleToken.Should().BeNull();
    }
}

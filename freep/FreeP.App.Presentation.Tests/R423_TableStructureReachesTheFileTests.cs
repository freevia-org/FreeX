using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r423: a table's structure must survive a .pptx round trip -- its grid, its spans, its row heights
/// and its cell text.
///
/// <para>FreeP is where r412's real persistence bug was found (rotation and flip written nowhere for
/// an inherited-geometry shape), and tables are its most structured shape by a wide margin: a column
/// width list, rows with heights, and cells carrying spans, insets and their own text bodies. Every
/// one of those is a place to lose something.</para>
///
/// <para>Span loss is the worst of them. A merged cell that comes back unmerged does not look
/// corrupt -- the table simply has an extra visible boundary where the author had one wide cell, and
/// the reader assumes the layout was always that way.</para>
/// </summary>
public sealed class R423_TableStructureReachesTheFileTests
{
    private static TableCell CellWithText(string text)
    {
        var cell = new TableCell { TextBody = new TextBody() };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        cell.TextBody!.Paragraphs.Add(paragraph);
        return cell;
    }

    private static Presentation DeckWithTable(Action<TableShape> configure)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.AddRange([914400, 1828800, 457200]);

        for (var rowIndex = 0; rowIndex < 2; rowIndex++)
        {
            var row = new TableRow { HeightEmu = 370840 + (rowIndex * 100000) };
            for (var col = 0; col < 3; col++)
                row.Cells.Add(CellWithText($"r{rowIndex}c{col}"));
            table.Rows.Add(row);
        }

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
            ExtentCxEmu = 3200400,
            ExtentCyEmu = 800000,
            Table = table,
        });
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static TableShape RoundTrip(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var shape = PptxPackageReader.Read(stream).Slides[0].Shapes.FirstOrDefault();
        shape.Should().NotBeNull("the table shape itself must survive before its structure can be judged");
        shape!.Table.Should().NotBeNull("a table shape that comes back without its table is empty on screen");
        return shape.Table!;
    }

    [Fact]
    public void TheGridSurvives()
    {
        var table = RoundTrip(DeckWithTable(_ => { }));

        table.ColumnWidthsEmu.Should().Equal([914400L, 1828800L, 457200L],
            "column widths are the table's layout; losing one collapses a column");
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells.Should().HaveCount(3);
    }

    [Fact]
    public void CellTextSurvivesInEveryCell()
    {
        // Asserted for every cell rather than the first: a writer that emitted only the first row,
        // or only the first cell of each row, would pass a spot check.
        var table = RoundTrip(DeckWithTable(_ => { }));

        for (var rowIndex = 0; rowIndex < 2; rowIndex++)
        {
            for (var col = 0; col < 3; col++)
            {
                var text = table.Rows[rowIndex].Cells[col].TextBody?.Paragraphs
                    .SelectMany(paragraph => paragraph.Runs)
                    .Select(run => run.Text)
                    .FirstOrDefault();

                text.Should().Be($"r{rowIndex}c{col}", "every cell's own text must come back in its own cell");
            }
        }
    }

    [Fact]
    public void RowHeightsSurvive()
    {
        var table = RoundTrip(DeckWithTable(_ => { }));

        table.Rows[0].HeightEmu.Should().Be(370840);
        table.Rows[1].HeightEmu.Should().Be(470840, "each row keeps its OWN height, not the first row's");
    }

    [Fact]
    public void AHorizontalMergeSurvives()
    {
        // A span that comes back as 1 does not look corrupt -- the table just shows an extra
        // boundary where the author had one wide cell.
        var table = RoundTrip(DeckWithTable(configured =>
        {
            configured.Rows[0].Cells[0].GridSpan = 2;
            configured.Rows[0].Cells[1].HMerge = true;
        }));

        table.Rows[0].Cells[0].GridSpan.Should().Be(2, "the merged cell must still span two columns");
        table.Rows[0].Cells[1].HMerge.Should().BeTrue("the continuation cell must still be marked merged");
    }

    [Fact]
    public void AVerticalMergeSurvives()
    {
        var table = RoundTrip(DeckWithTable(configured =>
        {
            configured.Rows[0].Cells[2].RowSpan = 2;
            configured.Rows[1].Cells[2].VMerge = true;
        }));

        table.Rows[0].Cells[2].RowSpan.Should().Be(2, "the merged cell must still span two rows");
        table.Rows[1].Cells[2].VMerge.Should().BeTrue("the continuation cell must still be marked merged");
    }

    /// <summary>
    /// r424: the banding flags decide which rows and columns a table style paints differently. They
    /// are booleans with MIXED defaults -- <c>BandRow</c> starts true and the rest false -- so each is
    /// set to the opposite of its own default; a probe of "all true" would let a writer that emits
    /// nothing pass on BandRow alone.
    /// </summary>
    [Fact]
    public void TheBandingFlagsSurvive()
    {
        var table = RoundTrip(DeckWithTable(configured => configured.Flags = new TableStyleFlags
        {
            FirstRow = true,
            LastRow = true,
            FirstCol = true,
            LastCol = true,
            BandRow = false,
            BandCol = true,
        }));

        table.Flags.FirstRow.Should().BeTrue("a header row that stops being styled changes the table's meaning");
        table.Flags.LastRow.Should().BeTrue();
        table.Flags.FirstCol.Should().BeTrue();
        table.Flags.LastCol.Should().BeTrue();
        table.Flags.BandRow.Should().BeFalse("this one defaults to true, so losing it looks like nothing happened");
        table.Flags.BandCol.Should().BeTrue();
    }

    [Fact]
    public void TheTableStyleIdSurvives()
    {
        // The style id is what binds the table to a theme's table style. Losing it does not empty the
        // table; it silently falls back to unstyled, which reads as a formatting choice.
        var styleId = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}";
        var table = RoundTrip(DeckWithTable(configured => configured.TableStyleId = styleId));

        table.TableStyleId.Should().Be(styleId, "a table that loses its style id renders unstyled");
    }

    [Fact]
    public void CellInsetsSurvive()
    {
        // Four independent insets, each a different value, so a writer that emitted one for all four
        // -- or transposed left and right -- fails here rather than passing on symmetry.
        var table = RoundTrip(DeckWithTable(configured =>
        {
            var cell = configured.Rows[0].Cells[0];
            cell.InsetLeftPt = 1.5;
            cell.InsetRightPt = 2.5;
            cell.InsetTopPt = 3.5;
            cell.InsetBottomPt = 4.5;
        }));

        var reloaded = table.Rows[0].Cells[0];
        reloaded.InsetLeftPt.Should().Be(1.5);
        reloaded.InsetRightPt.Should().Be(2.5);
        reloaded.InsetTopPt.Should().Be(3.5);
        reloaded.InsetBottomPt.Should().Be(4.5);
    }

    [Fact]
    public void AnUnmergedTableGainsNoSpans()
    {
        // The control. Every assertion above checks that something set survives, so a reader that
        // defaulted spans to 2, or marked cells merged, would satisfy them all.
        var table = RoundTrip(DeckWithTable(_ => { }));

        table.Rows.SelectMany(row => row.Cells).Should().OnlyContain(
            cell => cell.GridSpan == 1 && cell.RowSpan == 1 && !cell.HMerge && !cell.VMerge,
            "a table with no merges must not acquire any");
    }
}

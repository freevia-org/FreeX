using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r165. Writing a table whose HeaderRow option is on (the default for most table styles) rebuilds
/// that row's runs so the text can be bolded. That rebuild hand-listed the marks to carry and dropped
/// fourteen of them, so saving silently stripped a header cell's character style, move-revision mark,
/// citation, cross-reference, breaks and shapes from the file on disk.
///
/// It is the ninth run copier of this shape the program has found, and unlike the earlier ones it
/// corrupts the saved document rather than an in-memory state, so no undo recovers it. These tests go
/// through the real writer and reader, because the defect only appears once the file is written.
/// </summary>
public sealed class R165_HeaderRowRunMarksSurviveSaveTests
{
    private static TextDocument DocumentWithHeaderRowTable(Run cellRun)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var paragraph = new Paragraph();
        paragraph.Runs.Clear();
        paragraph.Runs.Add(cellRun);

        var cell = new TableCell();
        cell.Paragraphs.Add(paragraph);

        var row = new TableRow();
        row.Cells.Add(cell);

        var table = new Table { Formatting = TableFormatting.Default with { HeaderRow = true } };
        table.Rows.Add(row);
        document.Blocks.Add(table);

        return document;
    }

    private static Run RoundTripHeaderCellRun(Run cellRun)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(DocumentWithHeaderRowTable(cellRun), stream);
        stream.Position = 0;

        var reloaded = DocxReader.Read(stream);
        var table = reloaded.Blocks.OfType<Table>().Single();
        var paragraph = table.Rows[0].Cells[0].Paragraphs.Single();
        return paragraph.Runs.First(run => run.Text.Length > 0);
    }

    /// <summary>All runs of the header cell after a save-and-reload; a break run carries no text.</summary>
    private static IReadOnlyList<Run> RoundTripHeaderCellRuns(Run cellRun)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(DocumentWithHeaderRowTable(cellRun), stream);
        stream.Position = 0;

        var reloaded = DocxReader.Read(stream);
        var table = reloaded.Blocks.OfType<Table>().Single();
        return table.Rows[0].Cells[0].Paragraphs.Single().Runs;
    }

    [Fact]
    public void A_header_cells_character_style_survives_the_save()
    {
        var run = RoundTripHeaderCellRun(new Run("Header") { StyleId = "Strong" });

        run.StyleId.Should().Be("Strong");
    }

    [Fact]
    public void A_header_cells_page_break_survives_the_save()
    {
        // A second mark from the fourteen the old hand-list dropped, chosen because it has a real
        // on-disk representation. MoveRevisionId is deliberately NOT asserted here: a move id has no
        // standalone form in the file without the surrounding w:moveFrom/w:moveTo markup, so a
        // round-trip cannot show it either way. It is pinned at the copier instead, in
        // R165_RunCopiersCarryEveryMarkTests, which is where that loss actually happened.
        var runs = RoundTripHeaderCellRuns(new Run("Header") { IsPageBreak = true });

        runs.Should().Contain(run => run.IsPageBreak);
    }

    [Fact]
    public void A_header_cells_text_is_still_bolded_by_the_writer()
    {
        // Sibling/no-regression: carrying the marks must not cost the one thing this path exists to
        // do. The header row's text is still emboldened.
        var run = RoundTripHeaderCellRun(new Run("Header"));

        run.Formatting.Bold.Should().BeTrue();
    }
}

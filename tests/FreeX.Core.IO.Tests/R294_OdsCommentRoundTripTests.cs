using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r294: cell comments were dropped by the ODS adapter, exactly as hyperlinks were in r293.
///
/// <para>Found by asking what ELSE the adapter's capability profile failed to mention. The profile
/// lists what round-trips faithfully and what is deliberately deferred (charts, images, data
/// validation, conditional formatting, pivot tables, freeze panes -- "an expected ceiling, not a
/// bug"). Hyperlinks and comments appeared in NEITHER list, and both were silently lost. A feature
/// missing from that document cannot be judged loss-or-bug at all, which is the one thing the
/// document exists to decide.</para>
///
/// <para>ODF holds a note as <c>office:annotation</c> inside the cell, before its paragraphs. The
/// schema's ordering is why the writer uses <c>AddFirst</c>, and the reader's fallback path had to
/// learn to exclude the annotation -- otherwise a cell carrying only a note would have taken the
/// note's text as its VALUE.</para>
/// </summary>
public sealed class R294_OdsCommentRoundTripTests
{
    private static Workbook WorkbookWithComment(string comment, string? cellText = "v")
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2);
        if (cellText is not null)
            sheet.SetCell(address, new TextValue(cellText));
        sheet.Comments[address] = comment;
        return workbook;
    }

    private static Sheet RoundTrip(Workbook workbook)
    {
        var adapter = new OdsFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream).Sheets.First();
    }

    [Fact]
    public void ACommentSurvivesTheRoundTrip()
    {
        var sheet = RoundTrip(WorkbookWithComment("a note from the author"));

        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, 2, 2));
        sheet.Comments[new CellAddress(sheet.Id, 2, 2)].Should().Be("a note from the author");
    }

    [Fact]
    public void TheCommentedCellKeepsItsOwnValue()
    {
        var sheet = RoundTrip(WorkbookWithComment("note", cellText: "the actual value"));

        sheet.GetValue(new CellAddress(sheet.Id, 2, 2))
            .Should().Be(new TextValue("the actual value"),
                "the annotation sits beside the value paragraph and must not replace or join it");
    }

    /// <summary>
    /// The interaction that made this more than a copy of r293: with no value paragraph, the
    /// reader's fallback reads the cell's whole subtree -- which now contains the annotation.
    /// </summary>
    [Fact]
    public void ACommentOnAnEmptyCellDoesNotBecomeTheCellsValue()
    {
        var sheet = RoundTrip(WorkbookWithComment("just a note", cellText: null));
        var address = new CellAddress(sheet.Id, 2, 2);

        sheet.Comments.Should().ContainKey(address, "the note is still the note");
        sheet.GetValue(address).Should().Be(BlankValue.Instance,
            "the cell was empty; taking the note's text as its value invents content the user "
            + "never typed, and would show the comment as if it had been entered in the grid");
    }

    [Fact]
    public void AMultiLineCommentKeepsItsLineBreaks()
    {
        var sheet = RoundTrip(WorkbookWithComment("first line\nsecond line"));

        sheet.Comments[new CellAddress(sheet.Id, 2, 2)]
            .Should().Be("first line\nsecond line",
                "ODF stores one text:p per line, so the split and the join must agree");
    }

    [Fact]
    public void ACellWithoutACommentGainsNone()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("plain"));

        var loaded = RoundTrip(workbook);

        loaded.Comments.Should().BeEmpty();
        loaded.GetValue(new CellAddress(loaded.Id, 1, 1)).Should().Be(new TextValue("plain"));
    }
}

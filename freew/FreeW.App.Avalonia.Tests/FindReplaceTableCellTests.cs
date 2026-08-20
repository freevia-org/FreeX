using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// shared-find-replace-crossapp / freew-avalonia-find-replace-skips-tables: FreeW's Avalonia Find/Replace
/// used to only ever search <c>document.Blocks</c> for a <see cref="Paragraph"/>, silently skipping any
/// <see cref="Table"/> block -- Find Next reported "not found" and Replace All reported 0 replacements for
/// text that was plainly visible inside a table cell. See
/// freew/FreeW.App.Presentation/Dialogs/FindReplaceDialogPlanner.cs (the shared search engine) and
/// freew/FreeW.App.Avalonia/Editing/DocumentView.cs (FindNext/ReplaceAll, the production call site).
/// </summary>
public sealed class FindReplaceTableCellTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static DocumentView BuildViewWithTable(string beforeText, string cellText, string afterText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(beforeText));

        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(new TableCell(cellText));
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        doc.Blocks.Add(new Paragraph(afterText));

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    [Fact]
    public async Task FindNext_LocatesAndSelectsTextThatOnlyOccursInsideATableCell()
    {
        var found = false;
        string? selected = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildViewWithTable("intro", "Budget2026 total", "outro");

            found = view.FindNext("Budget2026");
            selected = view.SelectedText;
        });
        if (!ran) return;

        found.Should().BeTrue("the table cell contains the search term and must be found, matching the WPF host");
        selected.Should().Be("Budget2026");
    }

    [Fact]
    public async Task ReplaceAll_ReplacesTextInsideATableCellAndCountsIt()
    {
        var count = -1;
        string? cellText = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildViewWithTable("intro", "Budget2026 total", "outro");

            count = view.ReplaceAll(
                "Budget2026",
                "Budget2027",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            var table = (Table)view.Document.Blocks[1];
            cellText = table.Rows[0].Cells[0].Paragraphs[0].PlainText;
        });
        if (!ran) return;

        count.Should().Be(1, "Replace All must count the table-cell occurrence, not silently skip it");
        cellText.Should().Be("Budget2027 total");
    }

    [Fact]
    public async Task ReplaceAll_ReplacesMultipleOccurrencesAcrossDifferentCellsInTheSameRow()
    {
        var count = -1;
        string? firstCellText = null;
        string? secondCellText = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var table = new Table();
            var row = new TableRow();
            row.Cells.Add(new TableCell("cat sat"));
            row.Cells.Add(new TableCell("cat ran"));
            table.Rows.Add(row);
            doc.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            count = view.ReplaceAll(
                "cat",
                "dog",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            var resultTable = (Table)view.Document.Blocks[0];
            firstCellText = resultTable.Rows[0].Cells[0].Paragraphs[0].PlainText;
            secondCellText = resultTable.Rows[0].Cells[1].Paragraphs[0].PlainText;
        });
        if (!ran) return;

        count.Should().Be(2);
        firstCellText.Should().Be("dog sat");
        secondCellText.Should().Be("dog ran");
    }

    [Fact]
    public async Task ReplaceAll_WithAGridSpanCellEarlierInTheRow_ReplacesTheCorrectLaterCellNotAWrongOne()
    {
        // freex-r142-remediation / regression: EnumerateTableMatches used to yield the RAW
        // TableRow.Cells index as the match column instead of the grid-projected StartColumn every
        // consumer (GetCellParagraph -> TableGridProjection.StartingAt) expects. The two only coincide
        // when every cell in the row has GridSpan == 1. Here cell0 spans 2 grid columns, so cell2's raw
        // index (2) collides with cell1's real StartColumn (2) -- the buggy code resolved the match to
        // cell1 (an entirely unrelated cell) and corrupted its text instead of replacing the match in
        // cell2, while cell2 (which actually contains the search term) was left untouched.
        var count = -1;
        string? cell0Text = null;
        string? cell1Text = null;
        string? cell2Text = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var table = new Table();
            var row = new TableRow();
            row.Cells.Add(new TableCell("header") { GridSpan = 2 }); // raw idx0, grid cols 0-1
            row.Cells.Add(new TableCell("xxxxxxx"));                 // raw idx1, grid col 2
            row.Cells.Add(new TableCell("catfish"));                 // raw idx2, grid col 3 -- has the match
            table.Rows.Add(row);
            doc.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            count = view.ReplaceAll(
                "cat",
                "dog",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            var resultTable = (Table)view.Document.Blocks[0];
            cell0Text = resultTable.Rows[0].Cells[0].Paragraphs[0].PlainText;
            cell1Text = resultTable.Rows[0].Cells[1].Paragraphs[0].PlainText;
            cell2Text = resultTable.Rows[0].Cells[2].Paragraphs[0].PlainText;
        });
        if (!ran) return;

        count.Should().Be(1);
        cell2Text.Should().Be("dogfish", "the cell that actually contains the match must be the one that changes");
        cell1Text.Should().Be("xxxxxxx", "an unrelated cell must not be corrupted by a raw-index/grid-column mismatch");
        cell0Text.Should().Be("header");
    }

    [Fact]
    public async Task ReplaceAll_WithAGridSpanCellCoveringTheWholeGapBeforeTheMatch_DoesNotReportAPhantomReplacement()
    {
        // freex-r142-remediation / regression, count half: when the raw cell index lands on a grid
        // column no cell actually starts at (mid-span), TableGridProjection.StartingAt returns null,
        // GetCellParagraph returns null, and the replace is silently skipped -- but DocumentView.ReplaceAll
        // still increments its counter, so Replace All would report a replacement that never happened.
        var count = -1;
        string? cell1Text = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var table = new Table();
            var row = new TableRow();
            row.Cells.Add(new TableCell("wide") { GridSpan = 3 }); // raw idx0, grid cols 0-2
            row.Cells.Add(new TableCell("cat sat"));                // raw idx1, grid col 3 -- has the match
            table.Rows.Add(row);
            doc.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            count = view.ReplaceAll(
                "cat",
                "dog",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            var resultTable = (Table)view.Document.Blocks[0];
            cell1Text = resultTable.Rows[0].Cells[1].Paragraphs[0].PlainText;
        });
        if (!ran) return;

        cell1Text.Should().Be("dog sat", "the real match must actually be replaced, not silently skipped");
        count.Should().Be(1, "the reported count must match the number of actual replacements performed");
    }

    [Fact]
    public async Task FindNext_StillLocatesPlainBodyParagraphTextWhenNoTableIsInvolved()
    {
        // Sibling/non-regression coverage: adding table support must not disturb the ordinary
        // body-paragraph-only Find Next path.
        var found = false;
        string? selected = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("plain body text only"));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            found = view.FindNext("body");
            selected = view.SelectedText;
        });
        if (!ran) return;

        found.Should().BeTrue();
        selected.Should().Be("body");
    }
}

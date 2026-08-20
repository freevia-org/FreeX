using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// R158: the Avalonia shell was never wired to <c>TableCellListMarkerPlanner</c> when the WPF host was
/// fixed in R157 -- <see cref="DocumentView"/>'s table-cell layout path (<c>LayoutTablePaged</c>) still
/// called only <c>PreservedNumberingMarkerPlanner.BuildByParagraph</c>, which explicitly skips any
/// paragraph whose <see cref="ListKind"/> is already set, and the body render loop
/// (<c>RunBodyLayoutBlocks</c>) never replayed a table's cell paragraphs through its own live
/// <c>DocumentListMarkerSequencePlanner</c> when it hit a Table block. So on this shell a Number/Bullet/
/// MultiLevel paragraph inside a table cell rendered with no marker at all, and a body list resuming
/// after the table did not account for numbers "used" inside the table.
/// <para>
/// The Avalonia renderer has no WPF-style <see cref="System.Windows.Documents.FlowDocument"/> /
/// <c>TextRange</c> to read rendered text from, so these tests use the
/// <c>AllRenderedMarkerTextsForTest</c> host-access hook, which returns every rendered marker's literal
/// text in document (body + table-cell) render order straight out of the same <c>_markers</c> list the
/// real paint pass consumes -- exercising the actual production layout path, not a parallel
/// re-implementation of it.
/// </para>
/// </summary>
public sealed class TableCellNativeListMarkerAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static Table TableWithCellParagraphs(params Paragraph[][] rowsOfCellParagraphs)
    {
        var table = new Table();
        foreach (var cellParagraphs in rowsOfCellParagraphs)
        {
            var row = new TableRow();
            var cell = new TableCell();
            cell.Paragraphs.AddRange(cellParagraphs);
            row.Cells.Add(cell);
            table.Rows.Add(row);
        }
        // Each row above has exactly one cell (a single-column table), so a single column width is
        // enough to give ComputeColumnWidths a stable layout to measure against (mirrors the existing
        // BT1 table test in DocumentViewListEditTests.cs).
        table.ColumnWidthsPt.Add(120.0);
        return table;
    }

    private static Paragraph NumberParagraph(string text, int? startOverride = null) => new(text)
    {
        Formatting = ParagraphFormatting.Default with
        {
            ListKind = ListKind.Number,
            ListStartOverride = startOverride,
        }
    };

    // --- The core defect: no marker at all, and the body sequence must stay in sync across the table ---

    [Fact]
    public async Task NumberListInTableCell_GetsMarker_AndContinuesTheBodySequenceAcrossTheTable()
    {
        IReadOnlyList<string>? markers = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(NumberParagraph("One"));
            doc.Blocks.Add(NumberParagraph("Two"));
            doc.Blocks.Add(TableWithCellParagraphs([NumberParagraph("Three")]));
            doc.Blocks.Add(NumberParagraph("Four"));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));

            markers = view.AllRenderedMarkerTextsForTest;
        });

        if (!ran) return;

        // The defect: before the fix, "Three" rendered with NO marker whatsoever (only three markers
        // total -- "1.", "2." for the body paragraphs before the table, and "3." for "Four", which had
        // silently taken the number the table cell should have consumed). A marker-only check (just
        // "does the cell have a marker") would not catch a fix that mislabels the cell or fails to keep
        // the body counter in sync, so assert the full ordered sequence.
        markers.Should().Equal(
            ["1.", "2.", "3.", "4."],
            "the table cell must render its own continuing marker (\"3.\"), and the body list resuming " +
            "after the table must pick up at \"4.\" -- not restart at 1, not collide with the cell's own " +
            "marker, and not silently skip the cell paragraph as if it were never numbered");
    }

    // --- Sibling: two independent list instances in different cells must NOT share the counter --------

    [Fact]
    public async Task TwoIndependentNumberListsInDifferentCells_SecondRestartsInsteadOfContinuing()
    {
        IReadOnlyList<string>? markers = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            // Cell 2's explicit ListStartOverride mirrors how DocxReader surfaces a genuinely different
            // w:numId: nothing else distinguishes the two lists' shapes, but Word numbers them
            // independently, so cell 2 must restart at 1, not continue to 3.
            doc.Blocks.Add(TableWithCellParagraphs(
                [NumberParagraph("Alpha"), NumberParagraph("Bravo")],
                [NumberParagraph("Charlie", startOverride: 1)]));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));

            markers = view.AllRenderedMarkerTextsForTest;
        });

        if (!ran) return;

        markers.Should().Equal(
            ["1.", "2.", "1."],
            "the first cell's own list continues normally within itself (1., 2.), but an independent " +
            "list instance in a different cell (explicit restart override) must restart at 1., not " +
            "silently continue to 3. just because it is the next Number paragraph in document order");
    }

    // --- Bullet lists are named in the same defect and use a different (stateless) marker path ---------

    [Fact]
    public async Task BulletListInTableCell_GetsMarker()
    {
        IReadOnlyList<string>? markers = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var bulletParagraph = new Paragraph("Item")
            {
                Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
            };
            doc.Blocks.Add(TableWithCellParagraphs([bulletParagraph]));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));

            markers = view.AllRenderedMarkerTextsForTest;
        });

        if (!ran) return;

        markers.Should().Equal(
            ["•"],
            "a Bullet-kind paragraph inside a table cell must render its marker glyph, matching a bullet " +
            "paragraph at the body level");
    }
}

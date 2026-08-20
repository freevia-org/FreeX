using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R157: a Number/Bullet/MultiLevel paragraph inside a table cell (mapped by <c>DocxReader.ReadParagraph</c>
/// exactly like a body paragraph — same <c>numbering</c> map for <c>w:tc/w:p</c> as for body text) rendered
/// with no marker at all. The only place a native-list marker was produced was the top-level body loop in
/// <see cref="DocumentView"/>.Render(), which only iterates <see cref="TextDocument.Blocks"/> — never
/// descending into a table cell — and <c>PreservedNumberingMarkerPlanner</c> (the other marker mechanism)
/// explicitly skips any paragraph whose <see cref="ListKind"/> is already set, so the fallback never
/// covered this case either.
/// <para>
/// The WPF host renders body Number-kind lists via a native <see cref="System.Windows.Documents.List"/>
/// (marker drawn by WPF chrome, not literal text — see <see cref="ListNumberingRestartWpfTests"/>), but a
/// table cell's list paragraph is rendered as a plain <see cref="Paragraph"/> with the marker PREPENDED as
/// literal text (the same mechanism already used for MultiLevel accumulated markers and for preserved,
/// un-mapped numbering), so these assertions can read the marker straight out of the rendered document
/// text.
/// </para>
/// </summary>
public sealed class TableCellNativeListMarkerWpfTests
{
    private static Table TableWithCellParagraphs(params Paragraph[][] rowsOfCellParagraphs)
    {
        var table = new Table();
        foreach (var cellParagraphs in rowsOfCellParagraphs)
        {
            var row = new FreeW.Core.Model.TableRow();
            var cell = new FreeW.Core.Model.TableCell();
            cell.Paragraphs.AddRange(cellParagraphs);
            row.Cells.Add(cell);
            table.Rows.Add(row);
        }
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

    // --- The core defect: no marker at all -----------------------------------------------------------

    [StaFact]
    public void NumberListInTableCell_GetsMarker_AndContinuesTheBodySequenceAcrossTheTable()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(NumberParagraph("One"));
        doc.Blocks.Add(NumberParagraph("Two"));
        doc.Blocks.Add(TableWithCellParagraphs([NumberParagraph("Three")]));
        doc.Blocks.Add(NumberParagraph("Four"));

        var view = new DocumentView();
        view.LoadModel(doc);

        // The defect: before the fix, "Three" rendered with NO leading marker whatsoever (just the
        // table cell's ordinary indentation) -- a bare substring check for "3." would not have caught a
        // fix that, say, numbered every cell "1.", so assert the exact marker+text pairing.
        var rendered = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
        rendered.Should().Contain("3. Three",
            "a Number-kind paragraph inside a table cell must render its own marker, continuing the " +
            "running body sequence (1, 2 before the table) rather than showing no marker or restarting at 1");

        // Continuity: the body list resumed AFTER the table must pick up at 4, not restart at 1 and not
        // collide with the table's own "3" -- this is the part a marker-only test would miss entirely,
        // since it requires the body loop's own live counter to have been advanced through the table.
        var lists = view.Document.Blocks.OfType<List>().ToList();
        lists.Should().HaveCount(2, "the table interrupts the top-level run the same way a body paragraph would");
        lists[0].StartIndex.Should().Be(1);
        lists[0].ListItems.Count.Should().Be(2);
        lists[1].StartIndex.Should().Be(4,
            "the table cell consumed number 3, so the body list resuming after the table must start at 4, " +
            "not restart at 1 and not collide with the table's own marker");
    }

    // --- Sibling: two independent list instances in different cells must NOT share the counter --------

    [StaFact]
    public void TwoIndependentNumberListsInDifferentCells_SecondRestartsInsteadOfContinuing()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // Cell 2's explicit ListStartOverride mirrors how DocxReader surfaces a genuinely different
        // w:numId (NumberingRestartState.Resolve): nothing else distinguishes the two lists' shapes, but
        // Word numbers them independently, so cell 2 must restart at 1, not continue to 3.
        doc.Blocks.Add(TableWithCellParagraphs(
            [NumberParagraph("Alpha"), NumberParagraph("Bravo")],
            [NumberParagraph("Charlie", startOverride: 1)]));

        var view = new DocumentView();
        view.LoadModel(doc);

        var rendered = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
        rendered.Should().Contain("1. Alpha");
        rendered.Should().Contain("2. Bravo", "the first cell's own list continues normally within itself");
        rendered.Should().Contain("1. Charlie",
            "an independent list instance (explicit restart override) in a different cell must restart, " +
            "not silently continue to 3 just because it is the next Number paragraph in document order");
        rendered.Should().NotContain("3. Charlie");
    }

    // --- Bullet lists are named in the same defect and use a different (stateless) marker path ---------

    [StaFact]
    public void BulletListInTableCell_GetsMarker()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var bulletParagraph = new Paragraph("Item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
        };
        doc.Blocks.Add(TableWithCellParagraphs([bulletParagraph]));

        var view = new DocumentView();
        view.LoadModel(doc);

        var rendered = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
        rendered.Should().Contain("• Item",
            "a Bullet-kind paragraph inside a table cell must render its marker glyph, matching a bullet " +
            "paragraph at the body level");
    }
}

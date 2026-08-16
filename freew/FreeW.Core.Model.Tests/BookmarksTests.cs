namespace FreeW.Core.Model.Tests;

public class BookmarksTests
{
    [Fact]
    public void List_EmptyDocument_YieldsEmptyList()
    {
        var doc = new TextDocument();

        Bookmarks.List(doc).Should().BeEmpty();
    }

    [Fact]
    public void List_NoBookmarks_YieldsEmptyList()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Just body text"));
        doc.Blocks.Add(new Paragraph("More body text") { StyleId = "Normal" });

        Bookmarks.List(doc).Should().BeEmpty();
    }

    [Fact]
    public void List_EnumeratesBookmarksInDocumentOrderWithBlockIndices()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Intro"));                                  // 0: no bookmark
        doc.Blocks.Add(new Paragraph("Chapter One") { BookmarkName = "ch1" });   // 1
        doc.Blocks.Add(new Paragraph("Body"));                                   // 2: no bookmark
        doc.Blocks.Add(Table.Create(1, 1));                                      // 3: table (skipped)
        doc.Blocks.Add(new Paragraph("Chapter Two") { BookmarkName = "ch2" });   // 4

        var bookmarks = Bookmarks.List(doc);

        bookmarks.Should().Equal(
            new BookmarkLocation("ch1", 1),
            new BookmarkLocation("ch2", 4));
    }

    [Fact]
    public void RemoveBookmark_ClearsMatchingParagraphAndLeavesOthers()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { BookmarkName = "ch1" });
        doc.Blocks.Add(new Paragraph("Chapter Two") { BookmarkName = "ch2" });

        Bookmarks.RemoveBookmark(doc, "ch1");

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].BookmarkName.Should().BeNull();
        paragraphs[0].PlainText.Should().Be("Chapter One"); // text preserved
        paragraphs[1].BookmarkName.Should().Be("ch2");      // other bookmark untouched

        Bookmarks.List(doc).Should().Equal(new BookmarkLocation("ch2", 1));
    }

    [Fact]
    public void RemoveBookmark_UnknownName_LeavesDocumentUnchanged()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { BookmarkName = "ch1" });

        Bookmarks.RemoveBookmark(doc, "missing");

        Bookmarks.List(doc).Should().Equal(new BookmarkLocation("ch1", 0));
    }

    [Fact]
    public void RemoveBookmark_EmptyName_IsNoOp()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { BookmarkName = "ch1" });

        Bookmarks.RemoveBookmark(doc, string.Empty);

        Bookmarks.List(doc).Should().Equal(new BookmarkLocation("ch1", 0));
    }

    // --- Bookmarks placed inside table cells ---

    [Fact]
    public void List_FindsBookmarkInsideTableCell_WithExactLogicalAddress()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Intro"));                 // 0: no bookmark
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells[1].Paragraphs[0].BookmarkName = "cellMark";
        doc.Blocks.Add(table);                                  // 1: table, bookmark in second cell

        var bookmarks = Bookmarks.List(doc);

        // A cell-nested paragraph has no standalone index into TextDocument.Blocks, so List reports the
        // containing table's own block index (1) — the same convention ComplexFieldEngine's body-paragraph
        // walk uses for SEQ.
        bookmarks.Should().Equal(new BookmarkLocation(
            "cellMark",
            1,
            TableRowIndex: 0,
            TableGridColumnIndex: 2,
            TableParagraphIndex: 0));
    }

    [Fact]
    public void FindParagraph_ResolvesBookmarkInsideTableCell_ToTheActualCellParagraph()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Intro"));
        var table = Table.Create(1, 2);
        var targetCellParagraph = table.Rows[0].Cells[1].Paragraphs[0];
        targetCellParagraph.Runs.Add(new Run("Cell target text"));
        targetCellParagraph.BookmarkName = "cellMark";
        doc.Blocks.Add(table);

        var found = Bookmarks.FindParagraph(doc, "cellMark");

        found.Should().BeSameAs(targetCellParagraph);
    }

    [Fact]
    public void FindLocation_ResolvesBodyAndTableTargetsWithExactOrdinalMatching()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body target") { BookmarkName = "BodyMark" });
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells[1].Paragraphs[0].BookmarkName = "CellMark";
        doc.Blocks.Add(table);

        Bookmarks.FindLocation(doc, "BodyMark").Should().Be(new BookmarkLocation("BodyMark", 0));
        Bookmarks.FindLocation(doc, "CellMark").Should().Be(new BookmarkLocation(
            "CellMark",
            1,
            TableRowIndex: 0,
            TableGridColumnIndex: 2,
            TableParagraphIndex: 0));
        Bookmarks.FindLocation(doc, "cellmark").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing")]
    public void FindLocation_UnknownOrEmptyName_ReturnsNull(string? name)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Target") { BookmarkName = "known" });

        Bookmarks.FindLocation(doc, name).Should().BeNull();
    }

    [Fact]
    public void FindParagraph_PrefersTopLevelBookmark_OverSameNameInsideACell()
    {
        // Sibling/no-regression: a top-level bookmark is still found via the normal (non-cell) path when
        // both exist — the cell-descent addition must not disturb top-level resolution or ordering.
        var doc = new TextDocument();
        var topLevel = new Paragraph("Top-level target") { BookmarkName = "shared" };
        doc.Blocks.Add(topLevel);
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].BookmarkName = "shared";
        doc.Blocks.Add(table);

        Bookmarks.FindParagraph(doc, "shared").Should().BeSameAs(topLevel);
    }

    [Fact]
    public void FindParagraph_UnknownName_ReturnsNull()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].BookmarkName = "cellMark";
        doc.Blocks.Add(table);

        Bookmarks.FindParagraph(doc, "missing").Should().BeNull();
    }

    [Fact]
    public void RemoveBookmark_ClearsBookmarkInsideTableCell_LeavingTheCellTextIntact()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        var cellParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        cellParagraph.Runs.Add(new Run("Cell text"));
        cellParagraph.BookmarkName = "cellMark";
        doc.Blocks.Add(table);

        Bookmarks.RemoveBookmark(doc, "cellMark");

        cellParagraph.BookmarkName.Should().BeNull();
        cellParagraph.PlainText.Should().Be("Cell text");
        Bookmarks.List(doc).Should().BeEmpty();
    }

    // --- Duplicate-name-safe, single-instance removal (confirmed HIGH finding) ---
    // A document can carry two paragraphs with the same bookmark name (Word forbids creating a second
    // one via the UI, but an imported/legacy document may already have duplicates). RemoveBookmarkAt must
    // clear only the one instance its BookmarkLocation identifies, unlike RemoveBookmark(name) above,
    // which intentionally clears every paragraph sharing that name.

    [Fact]
    public void RemoveBookmarkAt_ClearsOnlyTheTargetedInstance_LeavingASiblingWithTheSameNameIntact()
    {
        var doc = new TextDocument();
        var first = new Paragraph("First") { BookmarkName = "Dup" };
        var second = new Paragraph("Second") { BookmarkName = "Dup" };
        doc.Blocks.Add(first);
        doc.Blocks.Add(second);

        Bookmarks.RemoveBookmarkAt(doc, new BookmarkLocation("Dup", 1));

        first.BookmarkNames.Should().Equal("Dup");
        second.BookmarkNames.Should().BeEmpty();
        Bookmarks.List(doc).Should().Equal(new BookmarkLocation("Dup", 0));
    }

    [Fact]
    public void RemoveBookmarkAt_ResolvesATableCellLocation_LeavingTheCellTextIntact()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        var cellParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        cellParagraph.Runs.Add(new Run("Cell text"));
        cellParagraph.BookmarkName = "cellMark";
        doc.Blocks.Add(table);
        var location = Bookmarks.List(doc).Single();

        Bookmarks.RemoveBookmarkAt(doc, location);

        cellParagraph.BookmarkName.Should().BeNull();
        cellParagraph.PlainText.Should().Be("Cell text");
    }

    [Fact]
    public void RemoveBookmarkAt_StaleBlockIndex_IsNoOp()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { BookmarkName = "ch1" });

        Bookmarks.RemoveBookmarkAt(doc, new BookmarkLocation("ch1", 99));

        Bookmarks.List(doc).Should().Equal(new BookmarkLocation("ch1", 0));
    }

    [Fact]
    public void ResolveLocation_ReturnsTheExactBodyParagraph()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Intro"));
        var target = new Paragraph("Chapter One") { BookmarkName = "ch1" };
        doc.Blocks.Add(target);

        Bookmarks.ResolveLocation(doc, new BookmarkLocation("ch1", 1)).Should().BeSameAs(target);
    }

    [Fact]
    public void ResolveLocation_OutOfRangeBlockIndex_ReturnsNull()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Only"));

        Bookmarks.ResolveLocation(doc, new BookmarkLocation("x", 5)).Should().BeNull();
    }
}

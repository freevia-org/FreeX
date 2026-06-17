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
}

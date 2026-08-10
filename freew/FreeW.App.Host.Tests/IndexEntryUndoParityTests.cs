using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class IndexEntryUndoParityTests
{
    [StaFact]
    public void MarkIndexEntry_InsertsUndoableXeFieldAndIgnoresSameParagraphDuplicate()
    {
        var document = TextDocument.CreateEmpty();
        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.MarkIndexEntry("Alpha");
        Marks(editor).Should().Equal("Alpha");
        editor.Undo();
        Marks(editor).Should().BeEmpty();
        editor.Redo();
        Marks(editor).Should().Equal("Alpha");

        editor.MarkIndexEntry("alpha");
        Marks(editor).Should().Equal("Alpha");
    }

    [StaFact]
    public void RefreshIndex_PreservesRepeatedLabelsFromDistinctPhysicalPages()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(DocumentIndex.HeadingText) { StyleId = DocumentIndex.HeadingStyleId });
        document.Blocks.Add(new Paragraph("Old, 9") { StyleId = DocumentIndex.EntryStyleId });
        var firstSectionPage = document.Page.Clone();
        firstSectionPage.PageNumberFormat = PageNumberFormat.Decimal;
        firstSectionPage.PageNumberStartAt = 1;
        document.Page.PageNumberFormat = PageNumberFormat.Decimal;
        document.Page.PageNumberStartAt = 1;
        document.Blocks.Add(new Paragraph
        {
            Runs = { new Run("First"), DocumentIndex.MarkRun("Alpha") },
            SectionBreak = new Section(firstSectionPage, SectionBreakKind.NextPage)
        });
        document.Blocks.Add(new Paragraph
        {
            Runs = { new Run("Second"), DocumentIndex.MarkRun("Alpha"), DocumentIndex.MarkRun("Beta") }
        });

        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.RefreshIndex();

        editor.Model.Blocks.OfType<Paragraph>()
            .Where(DocumentIndex.IsIndexParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Alpha, 1, 1", "B", "Beta, 1");
    }

    [StaFact]
    public void RefreshIndex_ReportsBrokenXeRangeBookmark()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "MissingRange")) }
        });
        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.RefreshIndex();

        editor.Model.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId)
            .PlainText.Should().Be("Alpha, " + DocumentIndex.BrokenBookmarkText);
    }

    [StaFact]
    public void InsertIndex_DefaultAndPeopleRegionsCoexistWithMatchingEntriesOnly()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Entries"),
                DocumentIndex.MarkRun(new IndexMark("Alpha")),
                DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")),
                DocumentIndex.MarkRun(new IndexMark("Ignored", Identifier: "Places"))
            }
        });
        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.InsertIndex();
        editor.InsertIndex("People");

        IndexText(editor, identifier: null).Should().Equal("A", "Alpha, 1");
        IndexText(editor, "People").Should().Equal("A", "Ada, 1");
        editor.Model.Blocks.Should().NotContain(block => DocumentIndex.IsIndexParagraph(block, "Places"));
    }

    [StaFact]
    public void RefreshIndex_PeopleLeavesDefaultRegionUntouchedAndUpdatesPeopleOnly()
    {
        var defaultHeading = new Paragraph(DocumentIndex.HeadingText)
        {
            StyleId = DocumentIndex.HeadingStyleIdFor(identifier: null)
        };
        var defaultEntry = new Paragraph("Alpha, 7")
        {
            StyleId = DocumentIndex.EntryStyleIdFor(identifier: null)
        };
        var peopleHeading = new Paragraph(DocumentIndex.HeadingText)
        {
            StyleId = DocumentIndex.HeadingStyleIdFor("People")
        };
        var peopleEntry = new Paragraph("Old Person, 9")
        {
            StyleId = DocumentIndex.EntryStyleIdFor("People")
        };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(defaultHeading);
        document.Blocks.Add(defaultEntry);
        document.Blocks.Add(peopleHeading);
        document.Blocks.Add(peopleEntry);
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Entries"),
                DocumentIndex.MarkRun(new IndexMark("Beta")),
                DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")),
                DocumentIndex.MarkRun(new IndexMark("Grace", Identifier: "People"))
            }
        });
        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.MarkIndexEntry(string.Empty);
        var defaultRegionBefore = editor.Model.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier: null))
            .Cast<Paragraph>()
            .ToArray();

        editor.RefreshIndex("People");

        var defaultRegionAfter = editor.Model.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier: null))
            .Cast<Paragraph>()
            .ToArray();
        defaultRegionAfter.Should().BeEquivalentTo(defaultRegionBefore, options => options.WithStrictOrdering());
        IndexText(editor, "People").Should().Equal("A", "Ada, 1", "G", "Grace, 1");
        editor.Model.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Should().NotContain(peopleHeading)
            .And.NotContain(peopleEntry);
    }

    [StaFact]
    public void RefreshIndex_ReplacesWordUpdatedNativeRegionByFieldOwnership()
    {
        var field = new ComplexField(" INDEX \\h \"A\" \\z \"1033\" ");
        var heading = new Paragraph("A")
        {
            StyleId = "IndexHeading",
            SpanningFieldStart = field,
            SpanningFieldOwner = field
        };
        var entry = new Paragraph("Alpha, 1")
        {
            StyleId = "Index1",
            SpanningFieldOwner = field
        };
        var trailing = new Paragraph
        {
            StyleId = "IndexEntry",
            SpanningFieldOwner = field,
            EndsSpanningField = true
        };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun("Beta") } });
        document.Blocks.Add(heading);
        document.Blocks.Add(entry);
        document.Blocks.Add(trailing);
        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.RefreshIndex();

        IndexText(editor, identifier: null).Should().Equal("B", "Beta, 1");
        editor.Model.Blocks.Should().NotContain(heading).And.NotContain(entry).And.NotContain(trailing);
    }

    [StaFact]
    public void StructuredMarkIndexEntry_PreservesHierarchyAndCrossReferenceThroughUndo()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var mark = new IndexMark("Transportation", "Rail", "See Trains");

        editor.MarkIndexEntry(mark);

        MarksWithOptions(editor).Should().Equal(mark);
        editor.Undo();
        MarksWithOptions(editor).Should().BeEmpty();
        editor.Redo();
        MarksWithOptions(editor).Should().Equal(mark);
        editor.MarkIndexEntry(new IndexMark("transportation", "rail", "see trains"));
        MarksWithOptions(editor).Should().Equal(mark);
    }

    [StaFact]
    public void MarkAllIndexEntries_MarksMatchingParagraphsAsOneUndoableOperation()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Alpha first Alpha"));
        document.Blocks.Add(new Paragraph("alphabet control"));
        document.Blocks.Add(new Paragraph("Second ALPHA"));
        var editor = new DocumentView();
        editor.LoadModel(document);
        var mark = new IndexMark("Alpha", "Topic", BoldPageNumber: true);

        editor.MarkAllIndexEntries("Alpha", mark).Should().Be(3);
        MarksWithOptions(editor).Should().Equal(mark, mark, mark);

        editor.Undo();
        MarksWithOptions(editor).Should().BeEmpty();
        editor.Redo();
        MarksWithOptions(editor).Should().Equal(mark, mark, mark);
    }

    [StaFact]
    public void MarkAllIndexEntries_IncludesTableCellsInTheUndoGroup()
    {
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph("Alpha in a nested cell"));
        var row = new TableRow();
        row.Cells.Add(cell);
        var nestedTable = new Table();
        nestedTable.Rows.Add(row);
        var outerCell = new TableCell("outer control");
        outerCell.NestedTables.Add(nestedTable);
        var outerRow = new TableRow();
        outerRow.Cells.Add(outerCell);
        var table = new Table();
        table.Rows.Add(outerRow);
        var document = new TextDocument();
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph("Alpha in the body"));
        var editor = new DocumentView();
        editor.LoadModel(document);
        var mark = new IndexMark("Alpha", "Topic", ItalicPageNumber: true);

        editor.MarkAllIndexEntries("Alpha", mark).Should().Be(2);
        editor.CommitToModel();
        MarksWithOptions(editor).Should().Equal(mark, mark);
        cell.Paragraphs[0].Runs.Clear();
        MarksWithOptions(editor).Should().Equal(mark, mark);

        editor.Undo();
        MarksWithOptions(editor).Should().BeEmpty();
        editor.Redo();
        MarksWithOptions(editor).Should().Equal(mark, mark);
    }

    private static IEnumerable<string> Marks(DocumentView editor) =>
        editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(DocumentIndex.MarkedTerm)
            .OfType<string>();

    private static IEnumerable<IndexMark> MarksWithOptions(DocumentView editor) =>
        editor.Model.Blocks.SelectMany(ParagraphsIn)
            .SelectMany(paragraph => paragraph.Runs)
            .Select(DocumentIndex.MarkedEntry)
            .OfType<IndexMark>();

    private static IEnumerable<Paragraph> ParagraphsIn(Block block)
    {
        if (block is Paragraph paragraph)
        {
            yield return paragraph;
            yield break;
        }

        if (block is not Table table)
            yield break;

        foreach (var cellParagraph in table.Rows
                     .SelectMany(row => row.Cells)
                     .SelectMany(ParagraphsIn))
        {
            yield return cellParagraph;
        }
    }

    private static IEnumerable<Paragraph> ParagraphsIn(TableCell cell)
    {
        foreach (var nested in cell.NestedTables.SelectMany(table => table.Rows).SelectMany(row => row.Cells))
        {
            foreach (var paragraph in ParagraphsIn(nested))
                yield return paragraph;
        }

        foreach (var paragraph in cell.Paragraphs)
            yield return paragraph;
    }

    private static IEnumerable<string> IndexText(DocumentView editor, string? identifier) =>
        editor.Model.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText);
}

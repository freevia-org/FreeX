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
    public void RefreshIndex_AggregatesLogicalPagesFromXeOccurrences()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(DocumentIndex.HeadingText) { StyleId = DocumentIndex.HeadingStyleId });
        document.Blocks.Add(new Paragraph("Old, 9") { StyleId = DocumentIndex.EntryStyleId });
        document.Blocks.Add(new Paragraph { Runs = { new Run("First"), DocumentIndex.MarkRun("Alpha") } });
        document.Blocks.Add(DocumentOps.CreatePageBreak());
        document.Blocks.Add(new Paragraph
        {
            Runs = { new Run("Second"), DocumentIndex.MarkRun("Alpha"), DocumentIndex.MarkRun("Beta") }
        });
        document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        document.Page.PageNumberStartAt = 4;

        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.RefreshIndex();

        editor.Model.Blocks.OfType<Paragraph>()
            .Where(DocumentIndex.IsIndexParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Index", "Alpha, IV, V", "Beta, V");
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

    private static IEnumerable<string> Marks(DocumentView editor) =>
        editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(DocumentIndex.MarkedTerm)
            .OfType<string>();

    private static IEnumerable<IndexMark> MarksWithOptions(DocumentView editor) =>
        editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(DocumentIndex.MarkedEntry)
            .OfType<IndexMark>();
}

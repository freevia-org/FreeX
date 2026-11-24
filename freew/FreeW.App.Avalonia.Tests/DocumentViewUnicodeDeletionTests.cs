using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewUnicodeDeletionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData("body", true)]
    [InlineData("body", false)]
    [InlineData("cell", true)]
    [InlineData("cell", false)]
    [InlineData("header", true)]
    [InlineData("header", false)]
    [InlineData("shape", true)]
    [InlineData("shape", false)]
    public Task BackspaceAndDeleteRemoveWholeTextElementsAndUndoAsOneEdit(string story, bool backward) => OnUiThread(() =>
    {
        foreach (var cluster in new[] { "😀", "e\u0301", "👩‍💻" })
        {
            var content = "a" + cluster + "b";
            var (view, text, move) = CreateStory(story, content);
            move(backward ? 1 + cluster.Length : 1);

            Delete(view, backward);

            text().Should().Be("ab", $"{story} deletion must remove the complete {cluster} text element");
            view.CanUndo.Should().BeTrue();
            view.Undo();
            text().Should().Be(content);
            view.CanUndo.Should().BeFalse("one deletion gesture is one undoable edit");
            view.Redo();
            text().Should().Be("ab");
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task InteriorCaretOffsetsDeleteTheContainingCluster(bool backward) => OnUiThread(() =>
    {
        foreach (var story in new[] { "body", "cell", "header", "shape" })
        {
            var (view, text, move) = CreateStory(story, "a👩‍💻b");
            move(3);

            Delete(view, backward);

            text().Should().Be("ab", $"an interior {story} caret must not leave surrogate or ZWJ fragments");
        }
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    [InlineData("header")]
    [InlineData("shape")]
    public Task TrackedDeletionKeepsTheEntireClusterInOneDeletionRevision(string story) => OnUiThread(() =>
    {
        var (view, text, move) = CreateStory(story, "a👩‍💻b");
        view.ToggleTrackChanges();
        move(1);

        view.DeleteForwardForTest();

        text().Should().Be("a👩‍💻b");
        var runs = StoryParagraph(view.Document, story).Runs;
        string.Concat(runs.Where(run => run.Revision == RevisionKind.Deleted).Select(run => run.Text))
            .Should().Be("👩‍💻");
        view.Undo();
        StoryParagraph(view.Document, story).Runs.Should().OnlyContain(run => run.Revision == RevisionKind.None);
    });

    [Fact]
    public Task HeaderCachedFieldOffsetsRemainAtomicBeforeAUnicodeDeletion() => OnUiThread(() =>
    {
        var (view, _, _) = CreateStory("header", "");
        var paragraph = StoryParagraph(view.Document, "header");
        paragraph.Runs.Clear();
        paragraph.Runs.Add(Run.DateField("CACHED-FIELD-TEXT"));
        paragraph.Runs.Add(new Run("a👩‍💻b"));
        view.LoadDocument(view.Document);
        view.Measure(new Size(816, 4000));
        view.PlaceCaretInHeaderFooter(false, 0, "CACHED-FIELD-TEXTa👩‍💻".Length);

        view.BackspaceForTest();

        var field = StoryParagraph(view.Document, "header").Runs.Single(run => run.FieldKind == RunFieldKind.Date);
        field.Text.Should().Be("CACHED-FIELD-TEXT");
        StoryParagraph(view.Document, "header").Runs.Last().Text.Should().Be("ab");
        view.Undo();
        StoryParagraph(view.Document, "header").PlainText.Should().Be("CACHED-FIELD-TEXTa👩‍💻b");
    });

    [Fact]
    public Task StructuralFieldsInBodyAndCellsSurviveDeletionOfAnAdjacentUnicodeCluster() => OnUiThread(() =>
    {
        foreach (var story in new[] { "body", "cell" })
        {
            var (view, _, move) = CreateStory(story, "");
            var paragraph = StoryParagraph(view.Document, story);
            paragraph.Runs.Add(Run.DateField(""));
            paragraph.Runs.Add(new Run("a👩‍💻b"));
            view.LoadDocument(view.Document);
            view.Measure(new Size(816, 4000));
            var displayLength = story == "body"
                ? view.GetPlacedForBlock(0).Count
                : view.GetCellPlaced(0, 0, 0, 0).Where(glyph => !glyph.Sentinel).Max(glyph => glyph.ParaOffset) + 1;
            move(displayLength - 1);

            view.BackspaceForTest();

            StoryParagraph(view.Document, story).Runs.Should().Contain(run => run.FieldKind == RunFieldKind.Date);
            StoryParagraph(view.Document, story).Runs.Last().Text.Should().Be("ab");
        }
    });

    [Fact]
    public Task ZeroWidthMarkersBetweenFormattedClusterPartsArePreserved() => OnUiThread(() =>
    {
        var (view, _, move) = CreateStory("body", "");
        var paragraph = StoryParagraph(view.Document, "body");
        paragraph.Runs.Add(new Run("ae", RunFormatting.Default with { Bold = true }));
        paragraph.Runs.Add(Run.CommentReference(71));
        paragraph.Runs.Add(new Run("\u0301b"));
        view.LoadDocument(view.Document);
        move(3);

        view.BackspaceForTest();

        StoryParagraph(view.Document, "body").PlainText.Should().Be("ab");
        StoryParagraph(view.Document, "body").Runs.Should().Contain(run => run.IsCommentReference && run.CommentId == 71);
        StoryParagraph(view.Document, "body").Runs.First().Formatting.Bold.Should().BeTrue();
        view.Undo();
        StoryParagraph(view.Document, "body").PlainText.Should().Be("ae\u0301b");
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task FormFieldDeletionUsesWholeTextElementsUnderFormsProtection(bool backward) => OnUiThread(() =>
    {
        var (view, _, _) = CreateStory("body", "");
        var paragraph = StoryParagraph(view.Document, "body");
        paragraph.Runs.Add(Run.PlainTextControl("a👩‍💻b"));
        view.LoadDocument(view.Document);
        view.SetProtection(ProtectionMode.FillingForms);
        view.MoveCaretToBlockForTest(0, backward ? "a👩‍💻".Length : 1);

        Delete(view, backward);

        StoryParagraph(view.Document, "body").PlainText.Should().Be("ab");
        view.Undo();
        StoryParagraph(view.Document, "body").PlainText.Should().Be("a👩‍💻b");
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    [InlineData("header")]
    [InlineData("shape")]
    public Task LockedContentControlsRefuseWholeClusterDeletion(string story) => OnUiThread(() =>
    {
        var (view, _, move) = CreateStory(story, "");
        var paragraph = StoryParagraph(view.Document, story);
        var run = Run.PlainTextControl("a👩‍💻b");
        run.Control = run.Control! with { LockMode = ContentControlLockMode.ContentLocked };
        paragraph.Runs.Add(run);
        view.LoadDocument(view.Document);
        view.Measure(new Size(816, 4000));
        if (story == "shape")
        {
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing();
        }
        move("a👩‍💻".Length);

        view.BackspaceForTest();

        StoryParagraph(view.Document, story).PlainText.Should().Be("a👩‍💻b");
        view.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData("body", true)]
    [InlineData("body", false)]
    [InlineData("cell", true)]
    [InlineData("cell", false)]
    [InlineData("header", true)]
    [InlineData("header", false)]
    [InlineData("shape", true)]
    [InlineData("shape", false)]
    public Task DeletingAllTextInADeletionLockedEditableControlKeepsTheEmptyField(string story, bool backward) => OnUiThread(() =>
    {
        var (view, _, move) = CreateStory(story, "");
        var paragraph = StoryParagraph(view.Document, story);
        paragraph.Runs.Clear();
        var run = Run.PlainTextControl("😀", tag: "editable-field");
        run.Control = run.Control! with { LockMode = ContentControlLockMode.ControlLocked };
        paragraph.Runs.Add(run);
        view.LoadDocument(view.Document);
        view.Measure(new Size(816, 4000));
        if (story == "shape")
        {
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing();
        }
        move(backward ? 2 : 0);

        Delete(view, backward);

        var edited = StoryParagraph(view.Document, story);
        edited.PlainText.Should().BeEmpty();
        edited.Runs.Should().ContainSingle().Which.Control!.Tag.Should().Be("editable-field");
        view.Undo();
        StoryParagraph(view.Document, story).PlainText.Should().Be("😀");
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    [InlineData("header")]
    [InlineData("shape")]
    public Task UnicodeUndoRedoRestoresBookmarkEdgesAcrossFormattedRuns(string story) => OnUiThread(() =>
    {
        var (view, _, move) = CreateStory(story, "");
        var paragraph = StoryParagraph(view.Document, story);
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("a"));
        paragraph.Runs.Add(new Run("e", RunFormatting.Default with { Bold = true }));
        paragraph.Runs.Add(new Run("\u0301"));
        paragraph.Runs.Add(new Run("tail"));
        paragraph.BookmarkNames.Add("tail-bookmark");
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("b", BookmarkBoundaryKind.Start, 3, "tail-bookmark"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("b", BookmarkBoundaryKind.End, 4));
        var original = paragraph.BookmarkBoundaries.ToArray();
        view.LoadDocument(view.Document);
        view.Measure(new Size(816, 4000));
        if (story == "shape")
        {
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();
        }
        move(3);

        view.BackspaceForTest();

        StoryParagraph(view.Document, story).BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(1, 2);
        view.Undo();
        StoryParagraph(view.Document, story).Runs.Select(run => run.Text).Should().Equal("a", "e", "\u0301", "tail");
        StoryParagraph(view.Document, story).BookmarkBoundaries.Should().Equal(original);
        view.Redo();
        StoryParagraph(view.Document, story).PlainText.Should().Be("atail");
        StoryParagraph(view.Document, story).BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(1, 2);
    });

    [Fact]
    public Task UnicodeDeletionDoesNotPruneCommentMetadataOutsideItsUndoCommand() => OnUiThread(() =>
    {
        var (view, _, move) = CreateStory("body", "");
        var paragraph = StoryParagraph(view.Document, "body");
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("e\u0301") { CommentId = 71 });
        view.Document.Comments[71] = new Comment(71, "annotation", "reviewer", "R");
        view.LoadDocument(view.Document);
        move(2);

        view.BackspaceForTest();

        view.Document.Comments.Should().ContainKey(71);
        view.Undo();
        StoryParagraph(view.Document, "body").PlainText.Should().Be("e\u0301");
        StoryParagraph(view.Document, "body").Runs.Should().Contain(run => run.CommentId == 71);
        view.Document.Comments.Should().ContainKey(71);
    });

    private static (DocumentView View, Func<string> Text, Action<int> Move) CreateStory(string story, string content)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        if (story == "cell")
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0] = new TableCell(content);
            document.Blocks.Add(table);
        }
        else if (story == "shape")
        {
            var shape = new Shape(ShapeKind.TextBox, 144, 108, "#FFFFFF")
            {
                Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront },
            };
            shape.TextParagraphs.Add(new Paragraph(content));
            var paragraph = new Paragraph("body untouched");
            paragraph.Runs.Add(new Run(shape.PlainText) { Shape = shape });
            document.Blocks.Add(paragraph);
        }
        else
        {
            document.Blocks.Add(new Paragraph(story == "body" ? content : "body untouched"));
            if (story == "header")
                document.FinalSectionHeadersFooters.Header = new HeaderFooter(content);
        }
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        if (story == "shape" && content.Length > 0)
        {
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();
        }
        void Move(int offset)
        {
            if (story == "body") view.MoveCaretToBlockForTest(0, offset);
            else if (story == "header") view.PlaceCaretInHeaderFooter(false, 0, offset);
            else if (story == "cell") view.SetCellTextSelectionForTest(0, 0, 0, 0, offset, 0, 0, 0, offset);
            else view.SelectShapeTextRangeForTest(0, offset, offset);
        }
        return (view, () => StoryParagraph(view.Document, story).PlainText, Move);
    }

    private static Paragraph StoryParagraph(TextDocument document, string story) => story switch
    {
        "cell" => ((Table)document.Blocks[0]).Rows[0].Cells[0].Paragraphs[0],
        "header" => document.FinalSectionHeadersFooters.Header!.Paragraphs[0],
        "shape" => ((Paragraph)document.Blocks[0]).Runs[1].Shape!.TextParagraphs[0],
        _ => (Paragraph)document.Blocks[0],
    };

    private static void Delete(DocumentView view, bool backward)
    {
        if (backward) view.BackspaceForTest(); else view.DeleteForwardForTest();
    }

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);
}

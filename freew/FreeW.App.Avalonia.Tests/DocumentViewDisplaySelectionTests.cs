using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewDisplaySelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    public static IEnumerable<object[]> Prefixes()
    {
        foreach (var story in new[] { "body", "cell" })
        foreach (var kind in new[] { "date", "complex", "note", "equation" })
            yield return [story, kind];
    }

    [Theory]
    [MemberData(nameof(Prefixes))]
    public Task SelectedTextAndDeletionUseDisplayOffsetsWithoutDroppingOtherRuns(string story, string kind) => OnUiThread(() =>
    {
        var (prefix, display) = Prefix(story, kind);
        var marker = Run.CommentReference(77);
        var empty = Run.PlainTextControl("", tag: "empty-field");
        var suffix = new Run(" suffix", RunFormatting.Default with { Bold = true })
        {
            StyleId = "Emphasis", HyperlinkUrl = "https://example.com/selection"
        };
        var (view, paragraph) = Story(story, prefix, marker, suffix, empty);
        var originalRuns = paragraph.Runs.ToArray();
        paragraph.BookmarkNames.Add("suffix");
        paragraph.BookmarkBoundaries.Add(new("suffix-pair", BookmarkBoundaryKind.Start, 2, "suffix"));
        paragraph.BookmarkBoundaries.Add(new("suffix-pair", BookmarkBoundaryKind.End, 3));
        var originalBookmarks = paragraph.BookmarkBoundaries.ToArray();
        Select(view, story, 0, display.Length + suffix.Text.Length);
        view.SelectedText.Should().Be(display + " suffix");
        Select(view, story, display.Length + 4, display.Length + 1);
        view.SelectedText.Should().Be("suf");
        view.TryGetDisplaySelectionProjection(out var projectedParagraph, out var plan).Should().BeTrue();
        projectedParagraph.Should().BeSameAs(paragraph);
        plan.LiteralRanges.Should().ContainSingle().Which.ModelStart.Should().Be(1);

        view.DeleteForwardForTest();

        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, prefix));
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, marker));
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, empty));
        var literals = paragraph.Runs.Where(run => !ReferenceEquals(run, prefix)
            && !ReferenceEquals(run, marker) && !ReferenceEquals(run, empty)).ToArray();
        string.Concat(literals.Select(run => run.Text)).Should().Be(" fix");
        literals.Should().OnlyContain(run => run.Formatting.Bold && run.StyleId == "Emphasis"
            && run.HyperlinkUrl == "https://example.com/selection");
        paragraph.BookmarkBoundaries[0].RunIndex.Should().Be(2);
        paragraph.BookmarkBoundaries[1].RunIndex.Should().Be(paragraph.Runs.IndexOf(empty),
            "the bookmark end stays before the unrelated empty control");
        view.Document.Comments.Should().ContainKey(77);
        view.Undo();
        paragraph.Runs.Should().Equal(originalRuns);
        paragraph.BookmarkBoundaries.Should().Equal(originalBookmarks);
        view.SelectedText.Should().Be("suf");
        view.CanUndo.Should().BeFalse("one gesture creates one undo edit");
        view.Redo();
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, prefix));
        view.SelectedText.Should().BeEmpty();
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    public Task PartialAtomicSelectionIsReadableButCannotCutOrDelete(string story) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story(story, field, new Run("suffix"));
        var original = paragraph.Runs.ToArray();
        Select(view, story, 1, 4);

        view.SelectedText.Should().Be(" DA");
        view.IsDisplaySelectionDeletionBlocked().Should().BeTrue();
        view.TryDeleteSelection().Should().BeFalse();
        view.BackspaceForTest();
        view.DeleteForwardForTest();

        paragraph.Runs.Should().Equal(original);
        view.SelectedText.Should().Be(" DA");
        view.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    public Task DeletingAWholeNoteRestoresItsExactAnchorAndStoreOnUndo(string story) => OnUiThread(() =>
    {
        var noteRun = Run.FootnoteReference(403);
        var (view, paragraph) = Story(story, noteRun, new Run("suffix"));
        var note = view.Document.Footnotes[403];
        var unrelated = new Footnote(404);
        unrelated.Content.Add(new Paragraph("Unreferenced entry"));
        view.Document.Footnotes[404] = unrelated;
        Select(view, story, 0, story == "body" ? 1 : 3);

        view.TryDeleteSelection().Should().BeTrue();

        paragraph.Runs.Should().NotContain(run => run.FootnoteId == 403);
        view.Document.Footnotes.Should().NotContainKey(403);
        view.Document.Footnotes[404].Should().BeSameAs(unrelated);
        view.Undo();
        paragraph.Runs.First().Should().BeSameAs(noteRun);
        view.Document.Footnotes[403].Should().BeSameAs(note);
        view.SelectedText.Should().Be(story == "body" ? "1" : "403");
        view.Redo();
        view.Document.Footnotes.Should().NotContainKey(403);
        view.Document.Footnotes[404].Should().BeSameAs(unrelated);
    });

    [Theory]
    [InlineData("body", false)]
    [InlineData("cell", false)]
    [InlineData("body", true)]
    [InlineData("cell", true)]
    public Task TrackedLiteralDeletionKeepsOtherRunsAndRespectsOwnInsertions(string story, bool ownInsertion) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var suffix = new Run("suffix");
        var (view, paragraph) = Story(story, field, Run.CommentReference(77), suffix);
        view.RevisionAuthor = "Reviewer";
        if (ownInsertion)
        {
            suffix.Revision = RevisionKind.Inserted;
            suffix.RevisionAuthor = view.RevisionAuthor;
        }
        var original = paragraph.Runs.ToArray();
        view.ToggleTrackChanges();
        Select(view, story, 8, 11);

        view.TryDeleteSelection().Should().BeTrue();

        paragraph.Runs.First().Should().BeSameAs(field);
        if (ownInsertion)
            string.Concat(paragraph.Runs.Where(run => run.Revision == RevisionKind.Inserted).Select(run => run.Text))
                .Should().Be("fix");
        else
            paragraph.Runs.Where(run => run.Revision == RevisionKind.Deleted).Should().ContainSingle()
                .Which.Text.Should().Be("suf");
        view.Document.Comments.Should().ContainKey(77);
        view.Undo();
        paragraph.Runs.Should().Equal(original);
        view.SelectedText.Should().Be("suf");
        view.Redo();
        paragraph.Runs.First().Should().BeSameAs(field);
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    public Task TrackedAtomicDeletionDeclinesWithoutRemovingItsPayload(string story) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story(story, field, new Run("suffix"));
        view.ToggleTrackChanges();
        Select(view, story, 0, 8);

        view.IsDisplaySelectionDeletionBlocked().Should().BeTrue();
        view.TryDeleteSelection().Should().BeFalse();
        view.BackspaceForTest();

        paragraph.Runs.First().Should().BeSameAs(field);
        field.Revision.Should().Be(RevisionKind.None);
        view.SelectedText.Should().Be("{ DATE }");
    });

    [Theory]
    [InlineData("body", false)]
    [InlineData("cell", false)]
    [InlineData("body", true)]
    [InlineData("cell", true)]
    public Task ContentAndDocumentLocksDeclineMappedDeletion(string story, bool documentLock) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var suffix = Run.PlainTextControl("suffix");
        if (!documentLock)
            suffix.Control = suffix.Control! with { LockMode = ContentControlLockMode.ContentLocked };
        var (view, paragraph) = Story(story, field, suffix);
        if (documentLock) view.SetProtection(ProtectionMode.ReadOnly);
        Select(view, story, 8, 11);
        var original = paragraph.Runs.ToArray();

        view.IsDisplaySelectionDeletionBlocked().Should().BeTrue();
        view.TryDeleteSelection().Should().BeFalse();
        view.DeleteForwardForTest();

        paragraph.Runs.Should().Equal(original);
        suffix.Text.Should().Be("suffix");
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    public Task UnicodeLiteralSelectionAfterAnAtomicRunDeletesExactlyTheSelectedCluster(string story) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story(story, field, new Run("a👩‍💻b"));
        Select(view, story, 9, 9 + "👩‍💻".Length);

        view.SelectedText.Should().Be("👩‍💻");
        view.TryDeleteSelection().Should().BeTrue();

        paragraph.Runs.First().Should().BeSameAs(field);
        string.Concat(paragraph.Runs.Skip(1).Select(run => run.Text)).Should().Be("ab");
        view.Undo();
        view.SelectedText.Should().Be("👩‍💻");
    });

    [Fact]
    public Task CellSelectionMapsOneDisplayedNewlineToTheCompleteCrLf() => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story("cell", field, new Run("a\r\nb"));
        Select(view, "cell", 9, 10);

        view.SelectedText.Should().Be("\n");
        view.TryDeleteSelection().Should().BeTrue();

        paragraph.Runs.First().Should().BeSameAs(field);
        string.Concat(paragraph.Runs.Skip(1).Select(run => run.Text)).Should().Be("ab");
        view.Undo();
        paragraph.Runs.Last().Text.Should().Be("a\r\nb");
        view.SelectedText.Should().Be("\n");
    });

    [Fact]
    public Task RichCrossParagraphSelectionDoesNotFallBackToDestructiveFlattening() => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story("body", field, new Run("suffix"));
        var later = new Paragraph("Later");
        view.Document.Blocks.Add(later);
        view.LoadDocument(view.Document);
        view.SetBodySelectionForTest(0, 8, 1, 2);

        view.HasUnsupportedRichDisplaySelection().Should().BeTrue();
        view.IsDisplaySelectionDeletionBlocked().Should().BeTrue();
        view.TryDeleteSelection().Should().BeFalse();
        view.BackspaceForTest();

        view.Document.Blocks.Should().Equal(paragraph, later);
        paragraph.Runs.First().Should().BeSameAs(field);
        later.PlainText.Should().Be("Later");
    });

    [Theory]
    [MemberData(nameof(Prefixes))]
    public Task SingleLineTypingReplacesTheMappedSelectionAsOneUndoEdit(string story, string kind) => OnUiThread(() =>
    {
        var (prefix, display) = Prefix(story, kind);
        var marker = Run.CommentReference(77);
        var suffix = new Run(" suffix");
        var (view, paragraph) = Story(story, prefix, marker, suffix);
        var original = paragraph.Runs.ToArray();
        Select(view, story, display.Length + 1, display.Length + 4);

        view.InsertText("X");

        paragraph.Runs.First().Should().BeSameAs(prefix);
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, marker));
        string.Concat(paragraph.Runs.Where(run => !ReferenceEquals(run, prefix)).Select(run => run.Text))
            .Should().Be(" Xfix");
        view.SelectedText.Should().BeEmpty();
        view.Undo();
        paragraph.Runs.Should().Equal(original);
        view.SelectedText.Should().Be("suf");
        view.CanUndo.Should().BeFalse();
        view.Redo();
        string.Concat(paragraph.Runs.Where(run => !ReferenceEquals(run, prefix)).Select(run => run.Text))
            .Should().Be(" Xfix");
    });

    [Theory]
    [InlineData("body", false)]
    [InlineData("cell", false)]
    [InlineData("body", true)]
    [InlineData("cell", true)]
    public Task SingleLinePasteReplacesMappedTextAndUndoesTogether(string story, bool mergeFormatting) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story(story, field, new Run("suffix"));
        Select(view, story, 8, 11);

        (mergeFormatting ? view.PasteMergeFormatting("X") : view.PastePlainText("X")).Should().BeTrue();

        paragraph.Runs.First().Should().BeSameAs(field);
        string.Concat(paragraph.Runs.Skip(1).Select(run => run.Text)).Should().Be("Xfix");
        view.Undo();
        paragraph.Runs.Last().Text.Should().Be("suffix");
        view.SelectedText.Should().Be("suf");
        view.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    public Task MultilinePasteAndEnterDeclineBeforeDeletingTheSelection(string story) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story(story, field, new Run("suffix"));
        Select(view, story, 8, 11);
        var original = paragraph.Runs.ToArray();

        view.SelectionNeedsDisplayProjection().Should().BeTrue();
        view.PastePlainText("first\nsecond").Should().BeFalse();
        view.InsertParagraphBreakForTest();

        paragraph.Runs.Should().Equal(original);
        view.SelectedText.Should().Be("suf");
        view.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RichPrefixCannotRedirectASelectionIntoTheCachedControlFastPath(bool forms) => OnUiThread(() =>
    {
        foreach (var operation in new[] { "backspace", "delete", "type" })
        {
            var field = Run.DateField();
            field.FieldCodeVisible = true;
            var control = Run.PlainTextControl("0123456789", tag: "neighbor");
            var (view, paragraph) = Story("body", field, control);
            if (forms) view.SetProtection(ProtectionMode.FillingForms);
            Select(view, "body", 1, 4); // Inside displayed DATE, but inside cached control offsets 0..10.
            var original = paragraph.Runs.ToArray();

            if (operation == "backspace") view.BackspaceForTest();
            else if (operation == "delete") view.DeleteForwardForTest();
            else view.InsertText("X");

            paragraph.Runs.Should().Equal(original);
            control.Text.Should().Be("0123456789", operation + " must not touch the neighboring control");
            view.SelectedText.Should().Be(" DA");
        }
    });

    [Fact]
    public Task OrdinaryFormControlKeepsItsExistingReplacementBehavior() => OnUiThread(() =>
    {
        var (view, paragraph) = Story("body", Run.PlainTextControl("suffix", tag: "ordinary"));
        view.SetProtection(ProtectionMode.FillingForms);
        Select(view, "body", 0, 3);

        view.SelectionNeedsDisplayProjection().Should().BeFalse();
        view.InsertText("X");

        paragraph.PlainText.Should().Be("Xfix");
        paragraph.Runs.Should().OnlyContain(run => run.Control != null && run.Control.Tag == "ordinary");
    });

    [Fact]
    public Task BookmarkedSelectionWithAtomicNeighborDeclinesStructuralReplacementBeforeChangingText() => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story("body", new Run("abc"), field);
        paragraph.BookmarkNames.Add("whole");
        paragraph.BookmarkBoundaries.Add(new("whole-pair", BookmarkBoundaryKind.Start, 0, "whole"));
        paragraph.BookmarkBoundaries.Add(new("whole-pair", BookmarkBoundaryKind.End, 1));
        var original = paragraph.BookmarkBoundaries.ToArray();
        Select(view, "body", 1, 2);

        view.SelectionNeedsStructuralPreservation().Should().BeTrue();
        view.PastePlainText("first\nsecond").Should().BeFalse();
        view.InsertParagraphBreakForTest();

        paragraph.PlainText.Should().Be("abc");
        paragraph.Runs.Last().Should().BeSameAs(field);
        paragraph.BookmarkBoundaries.Should().Equal(original);
        view.SelectedText.Should().Be("b");
        view.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    public Task CrossParagraphPlainCopyUsesDisplayTextEvenWhenMutationIsUnsupported(string story) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story(story, field, new Run("suffix"));
        var laterField = Run.ComplexFieldRun(" PAGE ", "OLD CACHE", showCode: true);
        var later = new Paragraph();
        later.Runs.Add(laterField);
        later.Runs.Add(new Run("tail"));
        if (story == "cell")
            ((Table)view.Document.Blocks[0]).Rows[0].Cells[0].Paragraphs.Add(later);
        else view.Document.Blocks.Add(later);
        view.LoadDocument(view.Document);
        if (story == "cell") view.SetCellTextSelectionForTest(0, 0, 0, 0, 8, 0, 0, 1, 8);
        else view.SetBodySelectionForTest(0, 8, 1, 8);

        view.SelectedText.Should().Be("suffix\n{ PAGE }");
        view.HasUnsupportedRichDisplaySelection().Should().BeTrue();
        view.TryGetDisplaySelectionProjection(out _, out _).Should().BeFalse();
        view.TryDeleteSelection().Should().BeFalse();
        paragraph.Runs.First().Should().BeSameAs(field);
        later.Runs.First().Should().BeSameAs(laterField);
    });

    [Fact]
    public Task RemainingCommentAnchorInsideGroupedTextBoxKeepsTheThread() => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var bodyAnchor = new Run("anchor") { CommentId = 77 };
        var (view, paragraph) = Story("body", field, bodyAnchor);
        var comment = view.Document.Comments[77];
        var shape = new Shape(ShapeKind.Rectangle, 120, 50);
        var child = new Paragraph();
        child.Runs.Add(new Run("child anchor") { CommentId = 77 });
        shape.TextParagraphs.Add(child);
        var group = new DrawingGroup { WidthPt = 160, HeightPt = 80 };
        group.Children.Add(shape);
        group.ChildOffsets.Add((0, 0));
        var groupRun = new Run("") { DrawingGroup = group };
        paragraph.Runs.Add(groupRun);
        view.LoadDocument(view.Document);
        Select(view, "body", 8, 14);

        view.TryDeleteSelection().Should().BeTrue();

        view.Document.Comments[77].Should().BeSameAs(comment);
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, groupRun));
        child.Runs.Single().CommentId.Should().Be(77);
        view.Undo();
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, bodyAnchor));
        view.Document.Comments[77].Should().BeSameAs(comment);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TrackedReplacementKeepsDeletionAndInsertionInOneUndoEdit(bool ownInsertion) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var suffix = new Run("suffix");
        var (view, paragraph) = Story("body", field, suffix);
        view.RevisionAuthor = "Reviewer";
        if (ownInsertion)
        {
            suffix.Revision = RevisionKind.Inserted;
            suffix.RevisionAuthor = view.RevisionAuthor;
        }
        view.ToggleTrackChanges();
        Select(view, "body", 8, 11);

        view.InsertText("X");

        paragraph.Runs.First().Should().BeSameAs(field);
        string.Concat(paragraph.Runs.Where(run => run.Revision == RevisionKind.Inserted).Select(run => run.Text))
            .Should().Be(ownInsertion ? "Xfix" : "X");
        string.Concat(paragraph.Runs.Where(run => run.Revision == RevisionKind.Deleted).Select(run => run.Text))
            .Should().Be(ownInsertion ? "" : "suf");
        view.Undo();
        paragraph.Runs.Last().Should().BeSameAs(suffix);
        view.SelectedText.Should().Be("suf");
    });

    [Theory]
    [InlineData("body", true, false)]
    [InlineData("cell", true, false)]
    [InlineData("body", false, false)]
    [InlineData("cell", false, false)]
    [InlineData("body", true, true)]
    [InlineData("cell", true, true)]
    [InlineData("body", false, true)]
    [InlineData("cell", false, true)]
    public Task CollapsedAsciiDeletionPreservesRichNeighborsAndExactCaretHistory(string story, bool backward, bool tracked) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var marker = Run.CommentReference(77);
        var tail = new Run("abc");
        var (view, paragraph) = Story(story, field, marker, tail);
        var original = paragraph.Runs.ToArray();
        if (tracked) view.ToggleTrackChanges();
        var before = backward ? 10 : 9;
        Select(view, story, before, before);

        if (backward) view.BackspaceForTest(); else view.DeleteForwardForTest();

        paragraph.Runs.First().Should().BeSameAs(field);
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, marker));
        string.Concat(paragraph.Runs.Where(run => !ReferenceEquals(run, field)).Select(run => run.Text))
            .Should().Be(tracked ? "abc" : "ac");
        if (tracked)
            paragraph.Runs.Single(run => run.Revision == RevisionKind.Deleted).Text.Should().Be("b");
        AssertCollapsedCaret(view, tracked && !backward ? 10 : 9);
        view.Undo();
        paragraph.Runs.Should().Equal(original);
        AssertCollapsedCaret(view, before);
        view.Redo();
        AssertCollapsedCaret(view, tracked && !backward ? 10 : 9);
    });

    [Theory]
    [InlineData("body", true)]
    [InlineData("body", false)]
    [InlineData("cell", true)]
    [InlineData("cell", false)]
    public Task CollapsedWholeNoteMarkerDeletionKeepsItsPayloadUndoable(string story, bool backward) => OnUiThread(() =>
    {
        var id = story == "body" ? 403 : 7;
        var anchor = Run.FootnoteReference(id);
        var (view, paragraph) = Story(story, anchor, new Run("tail"));
        var note = new Footnote(id, "Original note");
        view.Document.Footnotes[id] = note;
        Select(view, story, backward ? 1 : 0, backward ? 1 : 0);

        if (backward) view.BackspaceForTest(); else view.DeleteForwardForTest();

        paragraph.Runs.Should().NotContain(run => run.FootnoteId == id);
        view.Document.Footnotes.Should().NotContainKey(id);
        AssertCollapsedCaret(view, 0);
        view.Undo();
        paragraph.Runs.First().Should().BeSameAs(anchor);
        view.Document.Footnotes[id].Should().BeSameAs(note);
        AssertCollapsedCaret(view, backward ? 1 : 0);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task CollapsedCellDeletionTreatsCrLfAsOneDisplayedBreak(bool backward) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var tail = new Run("a\r\nb");
        var (view, paragraph) = Story("cell", field, tail);
        var before = backward ? 10 : 9;
        Select(view, "cell", before, before);

        if (backward) view.BackspaceForTest(); else view.DeleteForwardForTest();

        paragraph.Runs.First().Should().BeSameAs(field);
        string.Concat(paragraph.Runs.Skip(1).Select(run => run.Text)).Should().Be("ab");
        AssertCollapsedCaret(view, 9);
        view.Undo();
        paragraph.Runs.Last().Should().BeSameAs(tail);
        tail.Text.Should().Be("a\r\nb");
        AssertCollapsedCaret(view, before);
    });

    [Fact]
    public Task RichCollapsedBoundaryAndParagraphSplitDeclineWithoutPartialMutation() => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story("body", field, new Run("tail"));
        var before = new Paragraph("Before");
        view.Document.Blocks.Insert(0, before);
        view.LoadDocument(view.Document);
        view.SetBodySelectionForTest(1, 0, 1, 0);

        view.BackspaceForTest();
        view.SelectionNeedsStructuralPreservation().Should().BeTrue();
        view.InsertParagraphBreakForTest();
        view.PastePlainText("first\nsecond").Should().BeFalse();

        view.Document.Blocks.Should().Equal(before, paragraph);
        paragraph.Runs.First().Should().BeSameAs(field);
        view.CanUndo.Should().BeFalse();
        view.CaretOffsetForTest.Should().Be(0);
        view.AutomationSnapshot().CaretOffset.Should().Be("Before\n".Length);
        view.AutomationSnapshot().Selection.Should().BeNull();
    });

    public static IEnumerable<object[]> InsertionPoints()
    {
        foreach (var story in new[] { "body", "cell" })
        foreach (var text in new[] { "X", "😀" })
        foreach (var point in new[] { 0, 8, 9 })
            yield return [story, text, point];
    }

    [Theory]
    [MemberData(nameof(InsertionPoints))]
    public Task CollapsedInsertionMapsAdjacentLiteralBoundariesAndRestoresCaret(string story, string text, int point) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var marker = Run.CommentReference(77);
        var (view, paragraph) = Story(story, field, marker, new Run("ab"));
        var original = paragraph.Runs.ToArray();
        Select(view, story, point, point);

        view.InsertText(text);

        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, field));
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, marker));
        field.Text.Should().BeEmpty();
        AssertCollapsedCaret(view, point + text.Length);
        Select(view, story, 0, 10 + text.Length);
        view.SelectedText.Should().Be(point == 0 ? text + "{ DATE }ab"
            : point == 8 ? "{ DATE }" + text + "ab" : "{ DATE }a" + text + "b");
        view.Undo();
        paragraph.Runs.Should().Equal(original);
        AssertCollapsedCaret(view, point);
        view.CanUndo.Should().BeFalse();
        view.Redo();
        AssertCollapsedCaret(view, point + text.Length);
    });

    [Theory]
    [MemberData(nameof(Prefixes))]
    public Task CollapsedInsertionBesideNoteEquationAndFieldKeepsTheirSourceIdentities(string story, string kind) => OnUiThread(() =>
    {
        var (prefix, display) = Prefix(story, kind);
        var (view, paragraph) = Story(story, prefix, new Run("tail"));
        Select(view, story, display.Length, display.Length);

        view.InsertText("X");

        paragraph.Runs.First().Should().BeSameAs(prefix);
        string.Concat(paragraph.Runs.Skip(1).Select(run => run.Text)).Should().Be("Xtail");
        AssertCollapsedCaret(view, display.Length + 1);
        view.Undo();
        paragraph.Runs.First().Should().BeSameAs(prefix);
        paragraph.Runs.Last().Text.Should().Be("tail");
        AssertCollapsedCaret(view, display.Length);
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    public Task CollapsedInteriorAtomicInsertionDeclinesInsteadOfChangingTheCache(string story) => OnUiThread(() =>
    {
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        var (view, paragraph) = Story(story, field, new Run("tail"));
        var original = paragraph.Runs.ToArray();
        Select(view, story, 3, 3);

        view.InsertText("X");

        paragraph.Runs.Should().Equal(original);
        field.Text.Should().BeEmpty();
        AssertCollapsedCaret(view, 3);
        view.CanUndo.Should().BeFalse();
    });

    [Fact]
    public Task CollapsedCellInsertionMapsTheBoundaryAfterCrLfWithoutSplittingIt() => OnUiThread(() =>
    {
        var (view, paragraph) = Story("cell", new Run("a\r\nb"));
        Select(view, "cell", 2, 2);

        view.InsertText("X");

        paragraph.PlainText.Should().Be("a\r\nXb");
        AssertCollapsedCaret(view, 3);
        view.Undo();
        paragraph.PlainText.Should().Be("a\r\nb");
        AssertCollapsedCaret(view, 2);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PendingFormattingIsConsumedByOneMappedInsertionOrReplacement(bool replace) => OnUiThread(() =>
    {
        var (view, paragraph) = Story("body", new Run("abcdef", RunFormatting.Default with { FontSizePt = 11 }));
        view.SetSelectionFontSize(20); // Prime the real collapsed-caret font-dialog state.
        var field = Run.DateField();
        field.FieldCodeVisible = true;
        paragraph.Runs.Insert(0, field); // Introduce the rich neighbor while retaining that pending state.
        Select(view, "body", 8, replace ? 11 : 8);

        view.InsertText("X");

        paragraph.Runs.Single(run => run.Text == "X").Formatting.FontSizePt.Should().Be(20);
        var end = 8 + 1 + (replace ? 3 : 6);
        Select(view, "body", end, end); // The 11pt tail owns this boundary; this helper does not clear pending formatting.
        view.InsertText("Y");
        paragraph.Runs.Single(run => run.Text == "Y").Formatting.FontSizePt.Should().Be(11,
            "the pending 20pt setting applies once, then the mapped ambient run supplies formatting");
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, field));
    });

    private static void AssertCollapsedCaret(DocumentView view, int offset)
    {
        view.SelectedText.Should().BeEmpty();
        view.AutomationSnapshot().CaretOffset.Should().Be(offset);
        view.AutomationSnapshot().Selection.Should().BeNull();
    }

    private static (Run Run, string Display) Prefix(string story, string kind)
    {
        if (kind == "date")
        {
            var field = Run.DateField();
            field.FieldCodeVisible = true;
            return (field, "{ DATE }");
        }
        if (kind == "complex")
            return (Run.ComplexFieldRun(" PAGE ", "OLD LONG CACHED RESULT", showCode: true), "{ PAGE }");
        if (kind == "note")
            return (Run.FootnoteReference(403), story == "body" ? "1" : "403");
        return (new Run("") { Equation = Equation.FromText("x+y") }, story == "body" ? "x+y" : "");
    }

    private static (DocumentView View, Paragraph Paragraph) Story(string story, params Run[] runs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange(runs);
        if (story == "cell")
        {
            var cell = new TableCell();
            cell.Paragraphs.Clear();
            cell.Paragraphs.Add(paragraph);
            var row = new TableRow();
            row.Cells.Add(cell);
            var table = new Table();
            table.Rows.Add(row);
            document.Blocks.Add(table);
        }
        else document.Blocks.Add(paragraph);
        var note = new Footnote(403);
        note.Content.Add(new Paragraph("Note text"));
        document.Footnotes[403] = note;
        document.Comments[77] = new Comment(77, "Comment text");
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        view.Arrange(new Rect(0, 0, 816, 4000));
        return (view, paragraph);
    }

    private static void Select(DocumentView view, string story, int anchor, int caret)
    {
        if (story == "cell") view.SetCellTextSelectionForTest(0, 0, 0, 0, anchor, 0, 0, 0, caret);
        else view.SetBodySelectionForTest(0, anchor, 0, caret);
    }

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);
}

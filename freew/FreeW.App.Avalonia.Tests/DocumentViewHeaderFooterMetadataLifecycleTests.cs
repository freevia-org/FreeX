using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewHeaderFooterMetadataLifecycleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(false, "insert", "abcXdef", 5)]
    [InlineData(true, "insert", "abcXdef", 5)]
    [InlineData(false, "paste", "abcXYdef", 6)]
    [InlineData(true, "paste", "abcXYdef", 6)]
    [InlineData(false, "backspace", "abcef", 3)]
    [InlineData(true, "backspace", "abcef", 3)]
    [InlineData(false, "delete", "abcef", 3)]
    [InlineData(true, "delete", "abcef", 3)]
    [InlineData(false, "replace", "abXef", 3)]
    [InlineData(true, "replace", "abXef", 3)]
    [InlineData(false, "cut", "abef", 2)]
    [InlineData(true, "cut", "abef", 2)]
    public Task SameParagraphEditsPreserveUnrelatedRunsAndMapBookmarks(
        bool footer, string edit, string expectedText, int expectedBookmarkEnd) => Session.Dispatch(() =>
    {
        var (document, view, paragraph) = RichStory(footer);
        var prefix = paragraph.Runs[0];
        var suffix = paragraph.Runs[^1];
        Apply(view, footer, edit);

        Story(document, footer).Paragraphs.Single().Should().BeSameAs(paragraph,
            "a same-paragraph edit does not replace the addressed paragraph");
        paragraph.PlainText.Should().Be(expectedText);
        AssertUnrelatedMetadata(paragraph);
        AssertBookmark(paragraph, "middle", BookmarkBoundaryKind.Start, 2);
        AssertBookmark(paragraph, "middle", BookmarkBoundaryKind.End, expectedBookmarkEnd);
        AssertZeroWidthAnchor(paragraph);
        prefix.Text.Should().Be("ab", "the undo snapshot's original runs must never be mutated");
        suffix.Text.Should().Be("ef");
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TypingInsideStyledHyperlinkInheritsOnlyTypingMetadata(bool footer) => Session.Dispatch(() =>
    {
        var (_, view, paragraph) = RichStory(footer);
        view.PlaceCaretInHeaderFooter(footer, offset: 1);
        view.InsertText("X");

        paragraph.PlainText.Should().Be("aXbcdef");
        var inserted = paragraph.Runs.Single(run => run.Text == "X");
        inserted.StyleId.Should().Be("PrefixStyle");
        inserted.HyperlinkUrl.Should().Be("https://example.invalid/prefix");
        inserted.HyperlinkTooltip.Should().Be("Prefix link");
        inserted.Revision.Should().Be(RevisionKind.None);
        inserted.RevisionAuthor.Should().BeNull();
        inserted.NestedRevision.Should().BeNull();
        inserted.CommentId.Should().BeNull();
        inserted.FieldKind.Should().Be(RunFieldKind.None);
        inserted.ComplexField.Should().BeNull();
        paragraph.Runs.Where(run => run.Text is "a" or "b")
            .Should().AllSatisfy(run =>
            {
                run.StyleId.Should().Be("PrefixStyle");
                run.RevisionAuthor.Should().Be("Source reviewer");
                run.NestedRevision.Should().Be(new NestedRevisionInfo(
                    RevisionKind.Deleted, "Outer reviewer", "2026-10-02T10:00:00Z"));
            });
        AssertZeroWidthAnchor(paragraph, 3);
    }, CancellationToken.None);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task AtomicNeighborStopsRemoteControlLinkAndStyleInheritance(bool before, bool locked) => Session.Dispatch(() =>
    {
        var remote = Run.PlainTextControl("A", tag: "Remote");
        remote.StyleId = "RemoteStyle";
        remote.HyperlinkUrl = "https://example.invalid/remote";
        remote.Formatting = new RunFormatting { Bold = true };
        remote.Control = remote.Control! with
        {
            LockMode = locked ? ContentControlLockMode.ContentLocked : ContentControlLockMode.NotSpecified,
        };
        var field = Run.ComplexFieldRun(" DOCPROPERTY Value ", "B");
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange(before ? [field, remote] : [remote, field]);
        var document = TextDocument.CreateEmpty();
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        view.PlaceCaretInHeaderFooter(false, offset: before ? 0 : 2);
        view.InsertText("X");

        paragraph.PlainText.Should().Be(before ? "XBA" : "ABX");
        var inserted = paragraph.Runs.Single(run => run.Text == "X");
        inserted.Control.Should().BeNull();
        inserted.StyleId.Should().BeNull();
        inserted.HyperlinkUrl.Should().BeNull();
        inserted.Formatting.Should().Be(remote.Formatting, "the existing formatting-only fallback is retained");
        remote.Text.Should().Be("A");
        field.Text.Should().Be("B");
        view.CanUndo.Should().BeTrue();
    }, CancellationToken.None);

    [Fact]
    public Task EmptyLeadingFieldAlsoStopsRemoteControlInheritance() => Session.Dispatch(() =>
    {
        var field = Run.ComplexFieldRun(" DOCPROPERTY Value ", string.Empty);
        var remote = Run.PlainTextControl("A", tag: "Remote");
        remote.Control = remote.Control! with { LockMode = ContentControlLockMode.ContentLocked };
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([field, remote]);
        var document = TextDocument.CreateEmpty();
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        view.PlaceCaretInHeaderFooter(false, offset: 0);
        view.InsertText("X");

        paragraph.PlainText.Should().Be("XA");
        paragraph.Runs.Single(run => run.Text == "X").Control.Should().BeNull();
        paragraph.Runs.Should().ContainSingle(run => run.ComplexField != null);
        remote.Control.LockMode.Should().Be(ContentControlLockMode.ContentLocked);
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UndoRedoThenTypingRestoresCaretSelectionAndMetadata(bool footer) => Session.Dispatch(() =>
    {
        var (_, view, paragraph) = RichStory(footer);
        Select(view, footer, 2, 4);
        view.InsertText("XY");
        paragraph.PlainText.Should().Be("abXYef");
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(4);

        view.Undo();
        paragraph.PlainText.Should().Be("abcdef");
        view.SelectedText.Should().Be("cd");
        AssertUnrelatedMetadata(paragraph);
        view.Redo();
        paragraph.PlainText.Should().Be("abXYef");
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(4);
        view.Undo();
        view.InsertText("Q");
        paragraph.PlainText.Should().Be("abQef");
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(3);
        view.CanRedo.Should().BeFalse();
        AssertUnrelatedMetadata(paragraph);
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UnicodeDeletionKeepsItsExistingMetadataSafeHistory(bool backward) => Session.Dispatch(() =>
    {
        var source = new Run("c😀d");
        var (_, view, paragraph) = RichStory(false, source);
        view.PlaceCaretInHeaderFooter(false, offset: backward ? 5 : 3);
        if (backward) view.BackspaceForTest(); else view.DeleteForwardForTest();

        paragraph.PlainText.Should().Be("abcdef");
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(3);
        AssertUnrelatedMetadata(paragraph);
        AssertZeroWidthAnchor(paragraph);
        AssertBookmark(paragraph, "middle", BookmarkBoundaryKind.End, 4);
        source.Text.Should().Be("c😀d");
        view.Undo();
        paragraph.PlainText.Should().Be("abc😀def");
        view.Redo();
        paragraph.PlainText.Should().Be("abcdef");
        AssertUnrelatedMetadata(paragraph);
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EmptyCachedFieldInsertionRetainsInstructionAndNeighborMetadata(bool footer) => Session.Dispatch(() =>
    {
        var (_, view, paragraph) = RichStory(footer);
        view.PlaceCaretInHeaderFooter(footer, offset: 3);
        view.InsertComplexField(" DOCPROPERTY EmptyValue ", string.Empty);

        paragraph.PlainText.Should().Be("abcdef");
        var field = paragraph.Runs.Should().ContainSingle(run => run.ComplexField != null).Subject;
        field.Text.Should().BeEmpty();
        field.ComplexField!.Instruction.Trim().Should().Be("DOCPROPERTY EmptyValue");
        AssertUnrelatedMetadata(paragraph);
        AssertZeroWidthAnchor(paragraph);
        view.Undo();
        paragraph.Runs.Should().NotContain(run => run.ComplexField != null);
        AssertUnrelatedMetadata(paragraph);
        view.Redo();
        paragraph.Runs.Should().ContainSingle(run => run.ComplexField != null
            && run.ComplexField.Instruction.Trim() == "DOCPROPERTY EmptyValue");
        AssertUnrelatedMetadata(paragraph);
    }, CancellationToken.None);

    [Fact]
    public Task EmptyFieldInsertionAtBookmarkCutDoesNotCaptureTheOriginalZeroWidthAnchor() => Session.Dispatch(() =>
    {
        var (_, view, paragraph) = RichStory(false);
        view.PlaceCaretInHeaderFooter(false, offset: 2);
        view.InsertComplexField(" DOCPROPERTY EmptyValue ", string.Empty);

        var fieldIndex = paragraph.Runs.FindIndex(run => run.ComplexField != null);
        var anchorIndex = paragraph.Runs.FindIndex(run => run.StyleId == "AnchorStyle");
        fieldIndex.Should().BeGreaterThanOrEqualTo(0);
        anchorIndex.Should().Be(fieldIndex + 1);
        var start = paragraph.BookmarkBoundaries.Single(boundary => boundary.PairKey == "empty"
            && boundary.Kind == BookmarkBoundaryKind.Start);
        start.RunIndex.Should().Be(anchorIndex, "a text-offset-only map cannot distinguish these two empty runs");
        AssertZeroWidthAnchor(paragraph);
        AssertUnrelatedMetadata(paragraph);
    }, CancellationToken.None);

    [Theory]
    [InlineData("checkbox")]
    [InlineData("date")]
    [InlineData("dropdown")]
    public Task ControlReplacementPreservesNeighborAndControlMetadata(string kind) => Session.Dispatch(() =>
    {
        var field = kind switch
        {
            "checkbox" => Run.CheckBoxControl(false, tag: "Choice"),
            "date" => Run.DatePickerControl("2026-01-01", tag: "Choice", dateFormat: "yyyy-MM-dd"),
            _ => Run.DropDownListControl([new("Red", "R"), new("Green", "G")], tag: "Choice"),
        };
        field.StyleId = "ControlStyle";
        field.Control = field.Control! with
        {
            Alias = "Stable field",
            WordMetadata = new ContentControlWordMetadata(Id: "120", TabIndex: "7"),
        };
        var (document, view, paragraph) = RichStory(false, field);
        view.PlaceCaretInHeaderFooter(false, offset: 2);
        view.ActivateHfContentControlForTest().Should().BeTrue();
        if (kind == "date")
        {
            var calendar = ((StackPanel)((Flyout)view.ActiveContentControlCalendarForTest!).Content!)
                .Children.OfType<global::Avalonia.Controls.Calendar>().Single();
            calendar.SelectedDate = new DateTime(2026, 2, 3);
        }
        else if (kind == "dropdown")
        {
            var menu = (ContextMenu)view.ActiveContextMenuForTests!;
            menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Green")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }

        var updated = paragraph.Runs.Should().ContainSingle(run => run.Control != null).Subject;
        updated.StyleId.Should().Be("ControlStyle");
        updated.Control!.Tag.Should().Be("Choice");
        updated.Control.Alias.Should().Be("Stable field");
        updated.Control.WordMetadata.Should().Be(field.Control.WordMetadata);
        updated.Text.Should().Be(kind switch
        {
            "checkbox" => FreeW.Core.Model.ContentControl.CheckedGlyph,
            "date" => "2026-02-03",
            _ => "Green",
        });
        AssertUnrelatedMetadata(paragraph);
        AssertZeroWidthAnchor(paragraph);
        AssertBookmark(paragraph, "middle", BookmarkBoundaryKind.End, 2 + updated.Text.Length);
        document.Footer.Should().BeNull();
    }, CancellationToken.None);

    [Fact]
    public Task TypingClearsOnlyTheEditedPlaceholderAndRetargetsItsOwnedBookmarks() => Session.Dispatch(() =>
    {
        var field = Run.PlainTextControl("cd", tag: "Input");
        field.Control = field.Control! with
        {
            WordMetadata = new ContentControlWordMetadata(Id: "120", ShowingPlaceholder: true, TabIndex: "7"),
        };
        var originalControl = field.Control;
        var (_, view, paragraph) = RichStory(false, field);
        for (var index = 0; index < paragraph.BookmarkBoundaries.Count; index++)
        {
            var boundary = paragraph.BookmarkBoundaries[index];
            if (boundary.PairKey == "middle")
                paragraph.BookmarkBoundaries[index] = boundary with { OwnerControl = originalControl };
        }
        view.PlaceCaretInHeaderFooter(false, offset: 3);
        view.InsertText("X");

        var updated = paragraph.Runs.Should().ContainSingle(run => run.Control != null).Subject;
        updated.Text.Should().Be("cXd");
        updated.Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse();
        updated.Control.WordMetadata.Id.Should().Be("120");
        updated.Control.WordMetadata.TabIndex.Should().Be("7");
        paragraph.BookmarkBoundaries.Where(boundary => boundary.PairKey == "middle")
            .Should().AllSatisfy(boundary => boundary.OwnerControl.Should().BeSameAs(updated.Control));
        originalControl!.WordMetadata!.ShowingPlaceholder.Should().BeTrue();
        AssertUnrelatedMetadata(paragraph);
        view.Undo();
        paragraph.Runs.Single(run => run.Control != null).Control.Should().BeSameAs(originalControl);
        paragraph.BookmarkBoundaries.Where(boundary => boundary.PairKey == "middle")
            .Should().AllSatisfy(boundary => boundary.OwnerControl.Should().BeSameAs(originalControl));
    }, CancellationToken.None);

    [Fact]
    public Task OpenControlMenuResolvesItsOriginalFieldAfterAnotherEditMovesTheRuns() => Session.Dispatch(() =>
    {
        var field = Run.DropDownListControl([new("Red", "R"), new("Green", "G")], tag: "Choice");
        var (_, view, paragraph) = RichStory(false, field);
        view.PlaceCaretInHeaderFooter(false, offset: 2);
        view.ActivateHfContentControlForTest().Should().BeTrue();
        var menu = (ContextMenu)view.ActiveContextMenuForTests!;
        view.PlaceCaretInHeaderFooter(false, offset: 0);
        view.InsertText("X");

        menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Green")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        paragraph.PlainText.Should().Be("XabGreenef");
        paragraph.Runs.Should().ContainSingle(run => run.Control != null).Which.Control!.Tag.Should().Be("Choice");
        AssertUnrelatedMetadata(paragraph);
        AssertZeroWidthAnchor(paragraph, 3);
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(8);
    }, CancellationToken.None);

    [Fact]
    public Task TypingInEmptyControlExpandsItsOwnedBookmarkWithoutCapturingTheAdjacentAnchor() => Session.Dispatch(() =>
    {
        var field = Run.PlainTextControl(string.Empty, tag: "Input");
        field.StyleId = "ControlStyle";
        field.Control = field.Control! with
        {
            WordMetadata = new ContentControlWordMetadata(Id: "120", ShowingPlaceholder: true),
        };
        var (_, view, paragraph) = RichStory(false, field);
        for (var index = 0; index < paragraph.BookmarkBoundaries.Count; index++)
        {
            var boundary = paragraph.BookmarkBoundaries[index];
            if (boundary.PairKey == "middle")
                paragraph.BookmarkBoundaries[index] = boundary with { OwnerControl = field.Control };
        }
        view.PlaceCaretInHeaderFooter(false, offset: 2);
        view.InsertText("X");

        paragraph.PlainText.Should().Be("abXef");
        var updated = paragraph.Runs.Should().ContainSingle(run => run.Control != null).Subject;
        updated.Text.Should().Be("X");
        updated.StyleId.Should().Be("ControlStyle");
        updated.Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse();
        AssertZeroWidthAnchor(paragraph);
        AssertBookmark(paragraph, "middle", BookmarkBoundaryKind.Start, 2);
        AssertBookmark(paragraph, "middle", BookmarkBoundaryKind.End, 3);
        paragraph.BookmarkBoundaries.Where(boundary => boundary.PairKey == "middle")
            .Should().AllSatisfy(boundary => boundary.OwnerControl.Should().BeSameAs(updated.Control));
        view.Undo();
        paragraph.PlainText.Should().Be("abef");
        paragraph.Runs.Single(run => run.Control != null).Should().BeSameAs(field);
    }, CancellationToken.None);

    [Theory]
    [InlineData("typing")]
    [InlineData("paste")]
    public Task PartialAtomicEditRefusesWithoutModelOrHistoryChange(string edit) => Session.Dispatch(() =>
    {
        var field = Run.ComplexFieldRun(" DOCPROPERTY Value ", "field");
        var (_, view, paragraph) = RichStory(false, field);
        var before = paragraph.Runs.ToArray();
        view.PlaceCaretInHeaderFooter(false, offset: 3);
        if (edit == "typing")
            view.InsertText("X");
        else if (edit == "paste")
            view.PastePlainText("X").Should().BeFalse();

        paragraph.Runs.Should().Equal(before);
        paragraph.PlainText.Should().Be("abfieldef");
        field.ComplexField!.Instruction.Should().Be(" DOCPROPERTY Value ");
        view.CanUndo.Should().BeFalse();
        AssertUnrelatedMetadata(paragraph);
    }, CancellationToken.None);

    [Fact]
    public Task SelectionWithinAtomicFieldExpandsTheWholeFieldAndPreservesNeighbors() => Session.Dispatch(() =>
    {
        var field = Run.ComplexFieldRun(" DOCPROPERTY Value ", "field");
        var (document, view, paragraph) = RichStory(false, field);
        Select(view, false, 3, 5);
        view.TryDeleteSelection().Should().BeTrue();

        document.Header!.Paragraphs.Single().Should().BeSameAs(paragraph);
        paragraph.PlainText.Should().Be("abef");
        paragraph.Runs.Should().NotContain(run => run.ComplexField != null);
        field.Text.Should().Be("field");
        AssertUnrelatedMetadata(paragraph);
        AssertZeroWidthAnchor(paragraph);
        AssertBookmark(paragraph, "middle", BookmarkBoundaryKind.Start, 2);
        AssertBookmark(paragraph, "middle", BookmarkBoundaryKind.End, 2);
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(2);
        view.Undo();
        paragraph.PlainText.Should().Be("abfieldef");
        AssertUnrelatedMetadata(paragraph);
    }, CancellationToken.None);

    [Theory]
    [InlineData("typing")]
    [InlineData("paste")]
    [InlineData("delete")]
    public Task ContentLockedParagraphRefusesAllSameParagraphEditRoutes(string edit) => Session.Dispatch(() =>
    {
        var (_, view, paragraph) = RichStory(false);
        paragraph.BlockContentControl = new BlockContentControl(
            BlockContentControlKind.RichText, LockMode: ContentControlLockMode.ContentLocked);
        view.PlaceCaretInHeaderFooter(false, offset: 3);
        var before = paragraph.Runs.ToArray();
        if (edit == "typing") view.InsertText("X");
        else if (edit == "paste") view.PastePlainText("X").Should().BeFalse();
        else view.DeleteForwardForTest();

        paragraph.Runs.Should().Equal(before);
        paragraph.PlainText.Should().Be("abcdef");
        view.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UnsupportedStructuralEditOfMetadataStoryRefusesBeforeAnyMutation(bool paste) => Session.Dispatch(() =>
    {
        var (_, view, paragraph) = RichStory(false);
        var before = paragraph.Runs.ToArray();
        Select(view, false, 2, 4);
        if (paste)
            view.PastePlainText("X\nY").Should().BeFalse();
        else
            view.InsertParagraphBreakForTest();
        paragraph.Runs.Should().Equal(before);
        paragraph.PlainText.Should().Be("abcdef");
        view.SelectedText.Should().Be("cd");
        view.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Fact]
    public Task LinkedDisplayedSectionKeepsOwnerThroughUndoRedoAndTyping() => Session.Dispatch(() =>
    {
        var (document, view, paragraph) = RichStory(false);
        var owner = document.Header!;
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("First") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("Second"));
        document.Sections[0].HeadersFooters.Header = owner;
        document.FinalSectionHeadersFooters.Header = null;
        view.LoadDocument(document);
        view.PlaceCaretInHeaderFooter(false, offset: 3);
        view.InsertText("X");
        view.HeaderFooterCaretInfo!.Value.SectionIndex.Should().Be(1);
        paragraph.PlainText.Should().Be("abcXdef");
        view.Undo();
        view.Redo();
        view.Undo();
        view.InsertText("Q");

        document.Sections[0].HeadersFooters.Header.Should().BeSameAs(owner);
        document.FinalSectionHeadersFooters.Header.Should().BeNull();
        paragraph.PlainText.Should().Be("abcQdef");
        view.HeaderFooterCaretInfo!.Value.SectionIndex.Should().Be(1);
        AssertUnrelatedMetadata(paragraph);
    }, CancellationToken.None);

    [Fact]
    public Task EditedHeaderStylesLinksAndBookmarkRangesSurviveSanitizedDocxRoundTrip() => Session.Dispatch(() =>
    {
        var (document, view, paragraph) = RichStory(false);
        // Nested revision serialization is a separate unsupported contract, not a claim of this unit.
        foreach (var run in paragraph.Runs)
        {
            run.Revision = RevisionKind.None;
            run.RevisionAuthor = null;
            run.RevisionDateXml = null;
            run.NestedRevision = null;
            run.CommentId = null;
        }
        document.Comments.Clear();
        view.PlaceCaretInHeaderFooter(false, offset: 3);
        view.InsertText("X");
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);
        var header = reopened.Header!.Paragraphs.Single();
        header.PlainText.Should().Be("abcXdef");
        header.Runs.Should().Contain(run => run.Text == "ab" && run.StyleId == "PrefixStyle"
            && run.HyperlinkUrl == "https://example.invalid/prefix");
        header.Runs.Should().Contain(run => run.Text == "ef" && run.StyleId == "SuffixStyle"
            && run.HyperlinkAnchor == "Destination");
        var middleStart = header.BookmarkBoundaries.Single(boundary => boundary.Name == "Middle"
            && boundary.Kind == BookmarkBoundaryKind.Start);
        var middleEnd = header.BookmarkBoundaries.Single(boundary => boundary.PairKey == middleStart.PairKey
            && boundary.Kind == BookmarkBoundaryKind.End);
        Offset(header, middleStart).Should().Be(2);
        Offset(header, middleEnd).Should().Be(5);
    }, CancellationToken.None);

    private static void Apply(DocumentView view, bool footer, string edit)
    {
        view.PlaceCaretInHeaderFooter(footer, offset: edit == "backspace" ? 4 : 3);
        switch (edit)
        {
            case "insert": view.InsertText("X"); break;
            case "paste": view.PastePlainText("XY").Should().BeTrue(); break;
            case "backspace": view.BackspaceForTest(); break;
            case "delete": view.DeleteForwardForTest(); break;
            case "replace": Select(view, footer, 2, 4); view.InsertText("X"); break;
            case "cut": Select(view, footer, 2, 4); view.TryDeleteSelection().Should().BeTrue(); break;
        }
    }

    private static void Select(DocumentView view, bool footer, int from, int to)
    {
        view.PlaceCaretInHeaderFooter(footer, offset: from);
        for (var index = from; index < to; index++)
            view.SimulateKeyForTest(Key.Right, shift: true);
        view.SelectedText.Should().HaveLength(to - from);
    }

    private static (TextDocument Document, DocumentView View, Paragraph Paragraph) RichStory(bool footer, Run? middle = null)
    {
        var paragraph = new Paragraph { StyleId = "HeaderStyle" };
        paragraph.Runs.Add(new Run("ab")
        {
            StyleId = "PrefixStyle", HyperlinkUrl = "https://example.invalid/prefix", HyperlinkTooltip = "Prefix link",
            CommentId = 41, Revision = RevisionKind.Inserted, RevisionAuthor = "Source reviewer",
            RevisionDateXml = "2026-10-01T10:00:00Z",
            NestedRevision = new(RevisionKind.Deleted, "Outer reviewer", "2026-10-02T10:00:00Z"),
        });
        paragraph.Runs.Add(new Run(string.Empty) { StyleId = "AnchorStyle" });
        paragraph.Runs.Add(middle ?? new Run("cd"));
        paragraph.Runs.Add(new Run("ef")
        {
            StyleId = "SuffixStyle", HyperlinkAnchor = "Destination", HyperlinkTooltip = "Suffix link",
            CommentId = 42, Revision = RevisionKind.Deleted, RevisionAuthor = "Second reviewer",
            RevisionDateXml = "2026-10-03T10:00:00Z",
        });
        paragraph.BookmarkNames.AddRange(["LegacyAnchor", "Middle", "EmptyAnchor"]);
        paragraph.BookmarkBoundaries.AddRange([
            new("middle", BookmarkBoundaryKind.Start, 2, "Middle", ColumnFirst: 1, ColumnLast: 2),
            new("empty", BookmarkBoundaryKind.Start, 1, "EmptyAnchor"),
            new("empty", BookmarkBoundaryKind.End, 2),
            new("middle", BookmarkBoundaryKind.End, 3),
        ]);
        var story = new HeaderFooter();
        story.Paragraphs.Add(paragraph);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));
        document.Comments[41] = new Comment(41, "Source prefix comment");
        document.Comments[42] = new Comment(42, "Source suffix comment");
        if (footer) document.Footer = story; else document.Header = story;
        foreach (var id in new[] { "PrefixStyle", "SuffixStyle", "AnchorStyle", "ControlStyle" })
            document.Styles[id] = new DocumentStyle { Id = id, Name = id, Type = StyleType.Character };
        document.Styles["HeaderStyle"] = new DocumentStyle
        {
            Id = "HeaderStyle", Name = "HeaderStyle", Type = StyleType.Paragraph,
        };
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        return (document, view, paragraph);
    }

    private static HeaderFooter Story(TextDocument document, bool footer) => (footer ? document.Footer : document.Header)!;

    private static void AssertUnrelatedMetadata(Paragraph paragraph)
    {
        var prefix = paragraph.Runs.Should().ContainSingle(run => run.Text == "ab").Subject;
        prefix.StyleId.Should().Be("PrefixStyle");
        prefix.HyperlinkUrl.Should().Be("https://example.invalid/prefix");
        prefix.HyperlinkTooltip.Should().Be("Prefix link");
        prefix.CommentId.Should().Be(41);
        prefix.Revision.Should().Be(RevisionKind.Inserted);
        prefix.RevisionAuthor.Should().Be("Source reviewer");
        prefix.RevisionDateXml.Should().Be("2026-10-01T10:00:00Z");
        prefix.NestedRevision.Should().Be(new NestedRevisionInfo(RevisionKind.Deleted,
            "Outer reviewer", "2026-10-02T10:00:00Z"));
        var suffix = paragraph.Runs.Should().ContainSingle(run => run.Text == "ef").Subject;
        suffix.StyleId.Should().Be("SuffixStyle");
        suffix.HyperlinkAnchor.Should().Be("Destination");
        suffix.CommentId.Should().Be(42);
        suffix.Revision.Should().Be(RevisionKind.Deleted);
        suffix.RevisionAuthor.Should().Be("Second reviewer");
        paragraph.BookmarkNames.Should().Equal("LegacyAnchor", "Middle", "EmptyAnchor");
    }

    private static void AssertZeroWidthAnchor(Paragraph paragraph, int offset = 2)
    {
        var empty = paragraph.Runs.Should().ContainSingle(run => run.StyleId == "AnchorStyle").Subject;
        empty.Text.Should().BeEmpty();
        var start = paragraph.BookmarkBoundaries.Single(boundary => boundary.PairKey == "empty" && boundary.Kind == BookmarkBoundaryKind.Start);
        var end = paragraph.BookmarkBoundaries.Single(boundary => boundary.PairKey == "empty" && boundary.Kind == BookmarkBoundaryKind.End);
        Offset(paragraph, start).Should().Be(offset);
        Offset(paragraph, end).Should().Be(offset);
        end.RunIndex.Should().Be(start.RunIndex + 1, "the collapsed pair still brackets its original zero-width run");
    }

    private static void AssertBookmark(Paragraph paragraph, string key, BookmarkBoundaryKind kind, int offset)
    {
        var boundary = paragraph.BookmarkBoundaries.Single(item => item.PairKey == key && item.Kind == kind);
        Offset(paragraph, boundary).Should().Be(offset);
        if (kind == BookmarkBoundaryKind.Start)
        {
            boundary.Name.Should().Be("Middle");
            boundary.ColumnFirst.Should().Be(1);
            boundary.ColumnLast.Should().Be(2);
        }
    }

    private static int Offset(Paragraph paragraph, BookmarkBoundary boundary) =>
        paragraph.Runs.Take(boundary.RunIndex).Sum(run => run.Text.Length);
}

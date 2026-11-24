using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewTextInputMethodTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    [InlineData("header")]
    [InlineData("shape")]
    public Task NativeContextUsesTheActiveParagraphAndLocalUtf16Selection(string story) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture(story, "a😀bc");
        var view = fixture.View;
        var client = Client(view);
        client.SurroundingText.Should().Be("a😀bc");
        client.Selection.Should().Be(new TextSelection(0, 0));
        client.TextViewVisual.Should().BeSameAs(view);
        client.SupportsSurroundingText.Should().BeTrue();
        client.SupportsPreedit.Should().BeFalse("inline marked-text layout is a separate renderer migration");

        client.Selection = new(4, 1);
        var expectedNativeRange = OperatingSystem.IsMacOS() ? new TextSelection(1, 4) : new TextSelection(4, 1);
        client.Selection.Should().Be(expectedNativeRange);
        if (OperatingSystem.IsMacOS())
            (client.Selection.End - client.Selection.Start).Should().Be(3, "Cocoa's unsigned NSRange length must not underflow");
        if (story == "cell") view.CellCaretInfo!.Value.Offset.Should().Be(1);
        else view.AutomationSnapshot().CaretOffset.Should().Be(1);
        view.SelectedText.Should().Be("😀b");
        view.CanUndo.Should().BeFalse("native selection changes must not mutate document history");
        client.CursorRectangle.Should().Be(story switch
        {
            "header" => view.HfCaretRectForTest!.Value,
            "shape" => view.ShapeCaretRectForTest!.Value,
            _ => view.CaretRectForTest!.Value,
        });
        fixture.Paragraph.PlainText.Should().Be("a😀bc");

        // The actual committed input follows the editor's existing TextInput event route.
        view.RaiseEvent(new TextInputEventArgs { RoutedEvent = DocumentView.TextInputEvent, Text = "文" });
        fixture.Paragraph.PlainText.Should().Be("a文c");
        view.Undo();
        fixture.Paragraph.PlainText.Should().Be("a😀bc");
        view.SelectedText.Should().Be("😀b");
        client.Selection.Should().Be(expectedNativeRange);
        if (story == "cell") view.CellCaretInfo!.Value.Offset.Should().Be(1);
        else view.AutomationSnapshot().CaretOffset.Should().Be(1);
    }, CancellationToken.None);

    [Fact]
    public Task ACellContextExcludesThePrecedingBodyAndOtherCellParagraphs() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("cell", "second paragraph");
        var cell = ((Table)fixture.View.Document.Blocks[1]).Rows[0].Cells[0];
        cell.Paragraphs.Insert(0, new Paragraph("first paragraph"));
        fixture.View.InvalidateAfterExternalMutation();
        fixture.View.PlaceCaretInCell(1, 0, 0, 1, 3);
        var client = Client(fixture.View);
        client.SurroundingText.Should().Be("second paragraph");
        client.Selection.Should().Be(new TextSelection(3, 3));
    }, CancellationToken.None);

    [Fact]
    public Task SelectionOnlyMovesNotifyTheEventReadByTheMacNativeBridge() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "abcd");
        var client = Client(fixture.View);
        var observations = new List<(string Text, TextSelection Selection)>();
        var selectionEvents = 0;
        client.SurroundingTextChanged += (_, _) => observations.Add((client.SurroundingText, client.Selection));
        client.SelectionChanged += (_, _) => selectionEvents++;

        client.Selection = new(3, 1);

        observations.Should().Contain(("abcd", OperatingSystem.IsMacOS() ? new TextSelection(1, 3) : new TextSelection(3, 1)));
        selectionEvents.Should().BeGreaterThan(0);
        fixture.View.SelectedText.Should().Be("bc");
        fixture.View.CaretPositionForTest.Should().Be((0, 1));
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Fact]
    public Task CommittedTypingPublishesTheFinalTextAndSelectionTogether() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "abcd");
        var client = Client(fixture.View);
        client.Selection = new(2, 2);
        var observations = new List<(string Text, TextSelection Selection)>();
        client.SurroundingTextChanged += (_, _) => observations.Add((client.SurroundingText, client.Selection));

        fixture.View.SimulateTextInputForTest("文");

        observations.Should().NotBeEmpty();
        observations.Should().OnlyContain(value => value.Text == "ab文cd" && value.Selection == new TextSelection(3, 3));
        client.SurroundingText.Should().Be("ab文cd");
        client.Selection.Should().Be(new TextSelection(3, 3));
    }, CancellationToken.None);

    [Theory]
    [InlineData("a😀b", 2)]
    [InlineData("ae\u0301b", 2)]
    [InlineData("a👩‍💻b", 3)]
    [InlineData("abcd", -1)]
    [InlineData("abcd", 5)]
    public Task UnsafeNativeSelectionEndpointsLeaveSelectionAndModelUnchanged(string text, int invalid) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", text);
        var client = Client(fixture.View);
        client.Selection = new(0, 0);
        client.Selection = new(invalid, text.Length);
        client.Selection.Should().Be(new TextSelection(0, 0));
        fixture.View.SelectedText.Should().BeEmpty();
        fixture.Paragraph.PlainText.Should().Be(text);
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Fact]
    public Task ACombiningSequenceAcrossFormattingRunsUsesParagraphBoundaries() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "");
        fixture.Paragraph.Runs.Clear();
        fixture.Paragraph.Runs.Add(new Run("ae"));
        fixture.Paragraph.Runs.Add(new Run("\u0301b") { Formatting = RunFormatting.Default with { Bold = true } });
        fixture.View.InvalidateAfterExternalMutation();
        fixture.View.MoveCaretToBlockForTest(0, 0);
        var client = Client(fixture.View);
        client.SurroundingText.Should().Be("ae\u0301b");
        client.Selection = new(2, 3);
        client.Selection.Should().Be(new TextSelection(0, 0));
        client.Selection = new(1, 3);
        fixture.View.SelectedText.Should().Be("e\u0301");
    }, CancellationToken.None);

    [Theory]
    [InlineData("field")]
    [InlineData("equation")]
    [InlineData("control")]
    [InlineData("hidden")]
    [InlineData("revision")]
    [InlineData("formula")]
    public Task UnmappedOrNonLiteralParagraphsDoNotOfferANativeReconversionClient(string kind) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "abcd");
        var originalClient = Client(fixture.View);
        var run = fixture.Paragraph.Runs[0];
        switch (kind)
        {
            case "field": run.FieldKind = RunFieldKind.PageNumber; break;
            case "equation": run.Equation = new Equation([MathRun.Superscript("x", "2")]); break;
            case "control":
                fixture.Paragraph.Runs.Clear();
                fixture.Paragraph.Runs.Add(Run.PlainTextControl("abcd"));
                break;
            case "hidden": run.Formatting = run.Formatting with { Hidden = true }; break;
            case "revision": run.Revision = RevisionKind.Inserted; break;
            case "formula": run.TableFormula = new TableFormulaField("=1+1"); break;
        }
        fixture.View.InvalidateAfterExternalMutation();
        fixture.View.MoveCaretToBlockForTest(0, 0);
        var before = fixture.Paragraph.PlainText;
        Request(fixture.View).Client.Should().BeNull();
        originalClient.Selection = new(0, 1);
        fixture.Paragraph.PlainText.Should().Be(before);
        fixture.View.SelectedText.Should().BeEmpty();
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Theory]
    [InlineData("body", "\t")]
    [InlineData("cell", "\t")]
    [InlineData("header", "\t")]
    [InlineData("shape", "\t")]
    [InlineData("body", "\n")]
    [InlineData("cell", "\n")]
    [InlineData("shape", "\n")]
    [InlineData("shape", "\r\n")]
    [InlineData("header", "\n")]
    [InlineData("header", "\r")]
    [InlineData("header", "\r\n")]
    [InlineData("header", "\v")]
    [InlineData("header", "\u2028")]
    [InlineData("header", "\u2029")]
    [InlineData("body", "\v")]
    [InlineData("cell", "\v")]
    [InlineData("body", "\u2028")]
    [InlineData("cell", "\u2028")]
    [InlineData("body", "\u2029")]
    [InlineData("cell", "\u2029")]
    public Task LiteralTextControlsKeepTheirNativeSelectionOffsetsAndNormalCommit(string story, string control) => Session.Dispatch(() =>
    {
        var original = "ab" + control + "cd";
        using var fixture = new Fixture(story, original);
        var client = Client(fixture.View);
        var initialRectangle = client.CursorRectangle;
        client.SurroundingText.Should().Be(original);
        client.Selection = new(2, 2 + control.Length);
        client.Selection.Should().Be(new TextSelection(2, 2 + control.Length));
        fixture.View.SelectedText.Should().Be(control);
        client.CursorRectangle.Height.Should().BeGreaterThan(0);
        client.CursorRectangle.Should().Be(story switch
        {
            "header" => fixture.View.HfCaretRectForTest!.Value,
            "shape" => fixture.View.ShapeCaretRectForTest!.Value,
            _ => fixture.View.CaretRectForTest!.Value,
        });
        if ((story is "header" or "shape") && control != "\t")
            client.CursorRectangle.Y.Should().BeGreaterThan(initialRectangle.Y,
                "the active caret after an embedded break must be on its actual subsequent visual line");
        fixture.View.RaiseEvent(new TextInputEventArgs { RoutedEvent = DocumentView.TextInputEvent, Text = "文" });
        fixture.Paragraph.PlainText.Should().Be("ab文cd");
        fixture.View.Undo();
        fixture.Paragraph.PlainText.Should().Be(original);
        fixture.View.SelectedText.Should().Be(control);
        client.Selection.Should().Be(new TextSelection(2, 2 + control.Length));
    }, CancellationToken.None);

    [Fact]
    public Task BodyCrLfRetainsBothSourceUnitsAndRejectsAnInteriorNativeEndpoint() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "ab\r\ncd");
        var client = Client(fixture.View);
        client.SurroundingText.Should().Be("ab\r\ncd");
        client.Selection = new(3, 4);
        client.Selection.Should().Be(new TextSelection(0, 0));
        client.Selection = new(2, 4);
        client.Selection.Should().Be(new TextSelection(2, 4));
        fixture.View.SelectedText.Should().Be("\r\n");
        fixture.View.RaiseEvent(new TextInputEventArgs { RoutedEvent = DocumentView.TextInputEvent, Text = "文" });
        fixture.Paragraph.PlainText.Should().Be("ab文cd");
        fixture.View.Undo();
        fixture.Paragraph.PlainText.Should().Be("ab\r\ncd");
        client.Selection.Should().Be(new TextSelection(2, 4));
    }, CancellationToken.None);

    [Fact]
    public Task CollapsedCellCrLfCannotBeMistakenForLiteralSourceOffsets() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("cell", "ab\r\ncd");
        Request(fixture.View).Client.Should().BeNull("cell display collapses the two source units into one stop");
        fixture.Paragraph.PlainText.Should().Be("ab\r\ncd");
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Theory]
    [InlineData("styled")]
    [InlineData("leading-break-styled")]
    public Task StyledHeaderBreaksShareNativeCursorSelectionCommitAndUndo(string kind) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("header", "ab\ncd");
        fixture.Paragraph.Runs.Clear();
        fixture.Paragraph.Runs.Add(new Run("a"));
        fixture.Paragraph.Runs.Add(new Run(kind == "leading-break-styled" ? "\nbcd" : "b\ncd")
            { Formatting = RunFormatting.Default with { Bold = true } });
        fixture.View.InvalidateAfterExternalMutation();
        fixture.View.PlaceCaretInHeaderFooter(false, 0, 0);
        var original = kind == "leading-break-styled" ? "a\nbcd" : "ab\ncd";
        var client = Client(fixture.View);
        client.SurroundingText.Should().Be(original);
        var firstLine = client.CursorRectangle;
        var secondLineStart = original.IndexOf('\n') + 1;
        client.Selection = new(secondLineStart, secondLineStart);
        client.CursorRectangle.Y.Should().BeGreaterThan(firstLine.Y);
        client.CursorRectangle.Should().Be(fixture.View.HfCaretRectForTest!.Value);
        fixture.View.AutomationSnapshot().CaretOffset.Should().Be(secondLineStart);
        client.Selection = new(secondLineStart, secondLineStart + 1);
        fixture.View.SelectedText.Should().Be(original.Substring(secondLineStart, 1));
        fixture.View.RaiseEvent(new TextInputEventArgs { RoutedEvent = DocumentView.TextInputEvent, Text = "文" });
        fixture.Paragraph.PlainText.Should().Be(original[..secondLineStart] + "文" + original[(secondLineStart + 1)..]);
        fixture.View.Undo();
        fixture.Paragraph.PlainText.Should().Be(original);
        fixture.View.SelectedText.Should().Be(original.Substring(secondLineStart, 1));
        client.Selection.Should().Be(new TextSelection(secondLineStart, secondLineStart + 1));
    }, CancellationToken.None);

    public static IEnumerable<object[]> HeaderFooterMetadataCases =>
        from story in new[] { "header", "footer" }
        from metadata in new[] { "comment", "hyperlink-url", "hyperlink-anchor", "hyperlink-tooltip",
            "character-style", "bookmark-boundaries", "revision-author", "revision-date",
            "field-lock", "field-code", "empty-format" }
        select new object[] { story, metadata };

    [Theory]
    [MemberData(nameof(HeaderFooterMetadataCases))]
    public Task HeaderFooterMetadataDetachesAnExistingNativeClientWithoutChangingSource(string story, string metadata) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture(story, "abcd");
        var view = fixture.View;
        var client = Client(view);
        var paragraph = fixture.Paragraph;
        var run = paragraph.Runs[0];
        switch (metadata)
        {
            case "comment": run.CommentId = 71; break;
            case "hyperlink-url": run.HyperlinkUrl = "https://example.test/"; break;
            case "hyperlink-anchor": run.HyperlinkAnchor = "destination"; break;
            case "hyperlink-tooltip": run.HyperlinkTooltip = "destination hint"; break;
            case "character-style": run.StyleId = "AuthoredCharacterStyle"; break;
            case "bookmark-boundaries":
                paragraph.BookmarkBoundaries.Add(new("pair", BookmarkBoundaryKind.Start, 0, "span"));
                paragraph.BookmarkBoundaries.Add(new("pair", BookmarkBoundaryKind.End, 1));
                break;
            case "revision-author": run.RevisionAuthor = "Editor"; break;
            case "revision-date": run.RevisionDateXml = "2026-01-01T00:00:00Z"; break;
            case "field-lock": run.FieldLocked = true; break;
            case "field-code": run.FieldCodeVisible = true; break;
            case "empty-format": paragraph.Runs.Add(new Run("") { Formatting = RunFormatting.Default with { Italic = true } }); break;
        }
        var originalRuns = paragraph.Runs.ToArray();
        var originalBoundaries = paragraph.BookmarkBoundaries.ToArray();
        view.InvalidateAfterExternalMutation();

        Request(view).Client.Should().BeNull("the HF atom edit path cannot retain this source metadata");
        client.SurroundingText.Should().BeEmpty();
        client.Selection = new(0, 2);
        view.SelectedText.Should().BeEmpty("a detached native client must not change the host selection");
        paragraph.PlainText.Should().Be("abcd");
        paragraph.Runs.Should().Equal(originalRuns);
        paragraph.BookmarkBoundaries.Should().Equal(originalBoundaries);
        view.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    [InlineData("header")]
    [InlineData("footer")]
    [InlineData("shape")]
    public Task NestedRevisionMetadataDoesNotOfferANativeReplacementClient(string story) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture(story, "abcd");
        var client = Client(fixture.View);
        var run = fixture.Paragraph.Runs[0];
        var revision = new NestedRevisionInfo(RevisionKind.Inserted, "Editor", "2026-01-01T00:00:00Z");
        run.NestedRevision = revision;
        fixture.View.InvalidateAfterExternalMutation();
        Request(fixture.View).Client.Should().BeNull();
        client.Selection = new(0, 2);
        fixture.View.SelectedText.Should().BeEmpty();
        fixture.Paragraph.Runs[0].Should().BeSameAs(run);
        run.NestedRevision.Should().BeSameAs(revision);
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Fact]
    public Task ShapeBookmarkBoundariesDetachNativeReplacementBeforeTheirRunIndexesCanBecomeStale() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("shape", "abcd");
        var client = Client(fixture.View);
        var paragraph = fixture.Paragraph;
        var run = paragraph.Runs[0];
        paragraph.BookmarkBoundaries.Add(new("pair", BookmarkBoundaryKind.Start, 0, "span"));
        paragraph.BookmarkBoundaries.Add(new("pair", BookmarkBoundaryKind.End, 1));
        var boundaries = paragraph.BookmarkBoundaries.ToArray();
        fixture.View.InvalidateAfterExternalMutation();
        Request(fixture.View).Client.Should().BeNull();
        client.Selection = new(1, 3);
        fixture.View.SelectedText.Should().BeEmpty();
        paragraph.Runs[0].Should().BeSameAs(run);
        paragraph.BookmarkBoundaries.Should().Equal(boundaries);
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Theory]
    [InlineData("header")]
    [InlineData("footer")]
    public Task PlainFormattedHeaderFooterNativeCommitPreservesParagraphMetadataAndUndo(string story) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture(story, "abcd");
        var paragraph = fixture.Paragraph;
        paragraph.Runs.Clear();
        var first = new Run("ab");
        var last = new Run("cd") { Formatting = RunFormatting.Default with { Bold = true } };
        paragraph.Runs.AddRange([first, last]);
        paragraph.BookmarkNames.Add("wholeParagraph");
        fixture.View.InvalidateAfterExternalMutation();
        var client = Client(fixture.View);
        client.Selection = new(1, 2);
        fixture.View.RaiseEvent(new TextInputEventArgs { RoutedEvent = DocumentView.TextInputEvent, Text = "文" });
        fixture.Paragraph.PlainText.Should().Be("a文cd");
        fixture.Paragraph.BookmarkNames.Should().Equal("wholeParagraph");
        fixture.Paragraph.Runs.Last().Formatting.Bold.Should().BeTrue();
        client.SurroundingText.Should().Be("a文cd");
        fixture.View.Undo();
        fixture.Paragraph.PlainText.Should().Be("abcd");
        fixture.Paragraph.Runs.Should().Equal(first, last);
        fixture.Paragraph.BookmarkNames.Should().Equal("wholeParagraph");
        fixture.View.SelectedText.Should().Be("b");
        client.Selection.Should().Be(new TextSelection(1, 2));
        fixture.View.CanUndo.Should().BeFalse("the native commit is one history entry");
        fixture.View.Redo();
        fixture.Paragraph.PlainText.Should().Be("a文cd");
    }, CancellationToken.None);

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    [InlineData("shape")]
    public Task AdmittedLiteralStoriesPreserveUnselectedLinkAndSupportedCharacterStyleMetadata(string story) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture(story, "abcd");
        var original = fixture.Paragraph.Runs[0];
        original.StyleId = story == "body" ? null : "AuthoredCharacterStyle";
        original.HyperlinkUrl = "https://example.test/";
        original.HyperlinkAnchor = "destination";
        original.HyperlinkTooltip = "destination hint";
        fixture.View.InvalidateAfterExternalMutation();
        if (story == "cell") fixture.View.PlaceCaretInCell(1, 0, 0, 0, 0);
        var client = Client(fixture.View);
        client.Selection = new(story == "body" ? 0 : 1, 2);
        fixture.View.RaiseEvent(new TextInputEventArgs { RoutedEvent = DocumentView.TextInputEvent, Text = "文" });
        fixture.Paragraph.PlainText.Should().Be(story == "body" ? "文cd" : "a文cd");
        var suffix = fixture.Paragraph.Runs.Last();
        suffix.Text.Should().EndWith("cd");
        suffix.StyleId.Should().Be(original.StyleId);
        suffix.HyperlinkUrl.Should().Be("https://example.test/");
        suffix.HyperlinkAnchor.Should().Be("destination");
        suffix.HyperlinkTooltip.Should().Be("destination hint");
        fixture.View.Undo();
        fixture.Paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(original);
        fixture.View.SelectedText.Should().Be(story == "body" ? "ab" : "b");
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Fact]
    public Task BodyCharacterStylesDetachNativeClientBeforeLegacyCoalescingCanUnlinkTheSuffix() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "abcd");
        var client = Client(fixture.View);
        var run = fixture.Paragraph.Runs[0];
        run.StyleId = "AuthoredCharacterStyle";
        fixture.View.InvalidateAfterExternalMutation();
        Request(fixture.View).Client.Should().BeNull("the body coalescer cannot preserve a styled suffix after prefix replacement");
        client.Selection = new(0, 2);
        fixture.View.SelectedText.Should().BeEmpty();
        fixture.Paragraph.PlainText.Should().Be("abcd");
        fixture.Paragraph.Runs[0].Should().BeSameAs(run);
        run.StyleId.Should().Be("AuthoredCharacterStyle");
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Fact]
    public Task NestedRevisionInAnotherShapeParagraphAlsoDetachesTheCloningReplacementClient() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("shape", "abcd");
        var client = Client(fixture.View);
        var shape = ((Paragraph)fixture.View.Document.Blocks[0]).Runs[1].Shape!;
        var other = new Paragraph("other");
        var nested = new NestedRevisionInfo(RevisionKind.Inserted, "Editor", "2026-01-01T00:00:00Z");
        other.Runs[0].NestedRevision = nested;
        shape.TextParagraphs.Add(other);
        fixture.View.InvalidateAfterExternalMutation();
        Request(fixture.View).Client.Should().BeNull("shape replacement clones even the untouched paragraphs");
        client.Selection = new(0, 2);
        fixture.View.SelectedText.Should().BeEmpty();
        shape.TextParagraphs[1].Should().BeSameAs(other);
        other.Runs[0].NestedRevision.Should().BeSameAs(nested);
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Theory]
    [InlineData("tabbed")]
    [InlineData("character-spacing")]
    public Task UnsupportedHeaderBreakGeometryDoesNotAdvertiseANativeClient(string kind) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("header", "ab\ncd");
        if (kind == "tabbed") fixture.Paragraph.Runs[0].Text = "a\tb\ncd";
        else fixture.Paragraph.Runs[0].Formatting = RunFormatting.Default with { CharacterSpacingPt = 1 };
        fixture.View.InvalidateAfterExternalMutation();
        Request(fixture.View).Client.Should().BeNull("tabs and character spacing are outside the shared HF layout capability");
        fixture.Paragraph.PlainText.Should().Be(kind == "tabbed" ? "a\tb\ncd" : "ab\ncd");
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Theory]
    [InlineData("\0")]
    [InlineData("\a")]
    [InlineData("\f")]
    [InlineData("\u0085")]
    public Task NativeSelectionRechecksUnsupportedControlTextBeforeApplyingIt(string control) => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "abcd");
        var client = Client(fixture.View);
        // An out-of-band model change must not let an old native client select through unsafe text.
        // Do not ask the renderer to format unsupported no-glyph controls to test this input guard.
        var run = fixture.Paragraph.Runs[0];
        run.Text = "ab" + control + "cd";
        try
        {
            Request(fixture.View).Client.Should().BeNull();
            client.Selection = new(2, 3);
            fixture.View.SelectedText.Should().BeEmpty();
            fixture.Paragraph.PlainText.Should().Be("ab" + control + "cd");
            fixture.View.CanUndo.Should().BeFalse();
        }
        finally { run.Text = "abcd"; }
    }, CancellationToken.None);

    [Fact]
    public Task CrossParagraphSelectionAndProtectionDetachAnExistingClient() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "abcd");
        var client = Client(fixture.View);
        fixture.View.Document.Blocks.Add(new Paragraph("next"));
        fixture.View.InvalidateAfterExternalMutation();
        fixture.View.SetSelectionRangePublic(0, 1, 1, 2);
        Request(fixture.View).Client.Should().BeNull();
        client.Selection = new(0, 1);
        fixture.View.SelectedText.Should().Be("bcd\nne");
        fixture.View.MoveCaretToBlockForTest(0, 0);
        fixture.View.SetProtection(ProtectionMode.ReadOnly);
        Request(fixture.View).Client.Should().BeNull();
        client.Selection = new(0, 1);
        fixture.View.SelectedText.Should().BeEmpty();
        fixture.Paragraph.PlainText.Should().Be("abcd");
    }, CancellationToken.None);

    [Fact]
    public Task FocusLossMakesAnOldNativeClientInert() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "abcd");
        var client = Client(fixture.View);
        var other = new TextBox();
        var panel = new StackPanel();
        fixture.Window.Content = null;
        panel.Children.Add(fixture.View);
        panel.Children.Add(other);
        fixture.Window.Content = panel;
        fixture.View.Focus().Should().BeTrue();
        other.Focus().Should().BeTrue();

        Request(fixture.View).Client.Should().BeNull();
        client.SurroundingText.Should().BeEmpty();
        client.CursorRectangle.Should().Be(default(Rect));
        client.Selection = new(0, 2);
        client.SetPreeditText("未確定");
        fixture.View.SelectedText.Should().BeEmpty();
        fixture.Paragraph.PlainText.Should().Be("abcd");
        fixture.View.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    [Fact]
    public Task CursorRectangleRemainsLocalWhenTheViewIsScaled() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "abcd");
        var client = Client(fixture.View);
        client.Selection = new(2, 2);
        var local = client.CursorRectangle;
        fixture.View.RenderTransformOrigin = RelativePoint.TopLeft;
        fixture.View.RenderTransform = new ScaleTransform(2, 2);
        client.CursorRectangle.Should().Be(local);
        var transform = fixture.View.TransformToVisual(fixture.Window);
        transform.Should().NotBeNull();
        local.TransformToAABB(transform!.Value).Width.Should().BeApproximately(local.Width * 2, 0.01);
    }, CancellationToken.None);

    [Fact]
    public Task PreeditCapabilityRemainsDisabledUntilInlineCompositionCanReflow() => Session.Dispatch(() =>
    {
        using var fixture = new Fixture("body", "abcd");
        var client = Client(fixture.View);
        client.SupportsPreedit.Should().BeFalse();
        client.SetPreeditText("未確定", 1);
        fixture.Paragraph.PlainText.Should().Be("abcd");
        fixture.View.CanUndo.Should().BeFalse();
        client.Selection.Should().Be(new TextSelection(0, 0));
    }, CancellationToken.None);

    private static TextInputMethodClientRequestedEventArgs Request(DocumentView view)
    {
        var request = new TextInputMethodClientRequestedEventArgs
        { RoutedEvent = DocumentView.TextInputMethodClientRequestedEvent };
        view.RaiseEvent(request);
        return request;
    }

    private static TextInputMethodClient Client(DocumentView view)
    {
        var client = Request(view).Client;
        client.Should().NotBeNull();
        return client!;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _story;
        public Window Window { get; }
        public DocumentView View { get; }
        public Paragraph Paragraph => _story switch
        {
            "cell" => ((Table)View.Document.Blocks[1]).Rows[0].Cells[0].Paragraphs[0],
            "header" => View.Document.FinalSectionHeadersFooters.Header!.Paragraphs[0],
            "footer" => View.Document.FinalSectionHeadersFooters.Footer!.Paragraphs[0],
            "shape" => ((Paragraph)View.Document.Blocks[0]).Runs[1].Shape!.TextParagraphs[0],
            _ => (Paragraph)View.Document.Blocks[0],
        };

        public Fixture(string story, string text)
        {
            _story = story;
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph(story == "body" ? text : "body untouched"));
            if (story == "cell")
            {
                var table = Table.Create(1, 1);
                table.Rows[0].Cells[0] = new TableCell(text);
                document.Blocks.Add(table);
            }
            else if (story == "header")
            {
                document.FinalSectionHeadersFooters.Header = new HeaderFooter(text);
            }
            else if (story == "footer")
            {
                document.FinalSectionHeadersFooters.Footer = new HeaderFooter(text);
            }
            else if (story == "shape")
            {
                var shape = new Shape(ShapeKind.TextBox, 144, 108, "#FFFFFF")
                { Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront } };
                shape.TextParagraphs.Add(new Paragraph(text));
                ((Paragraph)document.Blocks[0]).Runs.Add(new Run(text) { Shape = shape });
            }
            View = new DocumentView();
            View.LoadDocument(document);
            Window = new Window { Width = 900, Height = 700, Content = View };
            Window.Show();
            View.Focus().Should().BeTrue();
            View.Measure(new Size(816, 4000));
            if (story == "cell") View.PlaceCaretInCell(1, 0, 0, 0, 0);
            else if (story is "header" or "footer") View.PlaceCaretInHeaderFooter(story == "footer", 0, 0);
            else if (story == "shape")
            {
                View.SelectFloating(0, 1);
                View.EnterSelectedShapeTextEditing().Should().BeTrue();
                View.SelectShapeTextRangeForTest(0, 0, 0);
            }
            else View.MoveCaretToBlockForTest(0, 0);
        }

        public void Dispose() => Window.Close();
    }
}

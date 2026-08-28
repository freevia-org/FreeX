using Free.Shared.AppServices;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWClipboardApplicationWorkflowTests
{
    [Fact]
    public async Task ReadPasteSpecialAsync_ProjectsTextAndParsedRtfInOneTransfer()
    {
        const string rtf = @"{\rtf1\ansi\b Bold\b0  plain}";
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "plain",
                    CustomData: [PlatformClipboardData.FromText(
                        FreeWClipboardApplicationWorkflow.RichTextFormat,
                        rtf)])),
        };

        var result = await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard);

        result.IsSuccess.Should().BeTrue();
        result.Payload!.Text.Should().Be("plain");
        result.Payload.RichDocument.Should().NotBeNull();
        clipboard.LastReadRequest.Should().BeSameAs(
            FreeWClipboardApplicationWorkflow.PasteSpecialReadRequest);
    }

    [Fact]
    public async Task ReadPasteSpecialAsync_FallsBackToHtmlTableWhenNoRtfIsOnTheClipboard()
    {
        // R143 clip-1: FreeX never places "Rich Text Format" on the clipboard for a cell-range copy --
        // it places plain text plus a CF_HTML table fragment (matching FreeX.Core.Commands
        // ClipboardHtmlSerializer.Serialize/WrapAsCfHtml). Model that payload here (full CF_HTML header
        // + <html> wrapper, exactly what the WPF host's "HTML Format" carries) and confirm the rich
        // paste path recovers a formatted document from it instead of degrading to plain text.
        const string cfHtml =
            "Version:0.9\r\n" +
            "StartHTML:0000000097\r\n" +
            "EndHTML:0000000350\r\n" +
            "StartFragment:0000000133\r\n" +
            "EndFragment:0000000307\r\n" +
            "<html><head><meta charset=\"utf-8\"></head><body>\r\n<!--StartFragment-->" +
            "<table border=\"1\" cellspacing=\"0\" style=\"border-collapse:collapse\">" +
            "<tr><td style=\"font-weight:bold\">Name</td><td>Score</td></tr>" +
            "<tr><td>Ann</td><td>92</td></tr>" +
            "</table>" +
            "<!--EndFragment-->\r\n</body></html>";
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "Name\tScore\r\nAnn\t92",
                    CustomData: [PlatformClipboardData.FromText("HTML Format", cfHtml)])),
        };

        var result = await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard);

        result.IsSuccess.Should().BeTrue();
        result.Payload!.RichDocument.Should().NotBeNull();
        result.Payload.RichDocument!.PlainText.Should().Contain("Name").And.Contain("Ann");

        var plan = FreeWClipboardApplicationWorkflow.PlanPaste(
            result.Payload,
            PasteSpecialOption.KeepSourceFormatting);
        plan.PreferRichDocument.Should().BeTrue("a FreeX cell-range copy's formatting must survive Keep Source Formatting paste into FreeW");
    }

    // meta F3 (round 162): a foreign application's HTML clipboard payload carrying a non-default list
    // marker (a CSS square bullet, a CSS lower-roman numbered list) must keep that marker through the
    // HTML clipboard fallback path -- ReadAsync's HTML fallback reuses HtmlFileAdapter (see
    // TryParseHtmlDocument above), so this is the clipboard half of the same fix HtmlFileAdapter got.
    [Fact]
    public async Task ReadPasteSpecialAsync_PreservesForeignBulletGlyphAndNumberFormatFromHtmlClipboard()
    {
        const string html =
            "<html><body>" +
            "<ul style=\"list-style-type: square\"><li>Alpha</li></ul>" +
            "<ol style=\"list-style-type: lower-roman\"><li>One</li></ol>" +
            "</body></html>";
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "Alpha\r\nOne",
                    CustomData: [PlatformClipboardData.FromText("HTML Format", html)])),
        };

        var result = await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard);

        result.IsSuccess.Should().BeTrue();
        var paragraphs = result.Payload!.RichDocument!.Paragraphs.ToList();

        paragraphs.Should().Contain(p => p.Formatting.ListKind == ListKind.Bullet
                && p.Formatting.ListMarkerText == "▪",
            "a pasted CSS square bullet must not be silently normalized to FreeW's default round bullet");
        paragraphs.Should().Contain(p => p.Formatting.ListKind == ListKind.Number
                && p.Formatting.ListNumberFormat == ListNumberFormat.LowerRoman,
            "a pasted CSS lower-roman numbered list must not be silently normalized to FreeW's decimal default");
    }

    // freew-paste-formats F1: a clipboard carrying ONLY a bitmap (a screenshot, Paint's Ctrl+C, a PDF
    // viewer's "Copy image") has none of the text-shaped custom formats above and no Text, so before the
    // fix ReadPasteSpecialAsync -- which backs the default Ctrl+V/ribbon Paste and Paste Special's Keep
    // Source Formatting -- came back Empty with the false "Clipboard does not contain text" message.
    // Confirm the read now recovers an inline-picture RichDocument, and that it survives PlanPaste for
    // the exact option the default Ctrl+V path uses, which is what actually reaches the caret through
    // ApplyClipboardPastePlan (FreeW.App.Host/Editing/DocumentView.cs and the Avalonia MainWindow twin).
    [Fact]
    public async Task ReadPasteSpecialAsync_WrapsImageOnlyClipboardAsInlinePicture()
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Image: new PlatformClipboardImage(OnePixelPngBytes, PixelWidth: 1, PixelHeight: 1))),
        };

        var result = await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard);

        result.IsSuccess.Should().BeTrue(
            "a bitmap-only clipboard must not be reported as containing no text");
        result.Payload!.HasContent.Should().BeTrue();
        result.Payload.RichDocument.Should().NotBeNull();
        var run = result.Payload.RichDocument!.Paragraphs.SelectMany(p => p.Runs).Single();
        run.Image.Should().NotBeNull();
        run.Image!.DisplayBytes.Should().BeEquivalentTo(OnePixelPngBytes);

        clipboard.LastReadRequest!.IncludeImage.Should().BeTrue(
            "the default-paste request must ask the platform clipboard for an image, or it never arrives here");

        var plan = FreeWClipboardApplicationWorkflow.PlanPaste(
            result.Payload,
            PasteSpecialOption.KeepSourceFormatting);
        plan.PreferRichDocument.Should().BeTrue(
            "the default Ctrl+V option (KeepSourceFormatting) must carry the image through to the caret-insert path");
    }

    // Sibling no-regression: the plain Ctrl+V-as-text path (bound to "Paste Text Only", not the default
    // gesture) intentionally never asks for or reads an image -- text-only paste of an image-only
    // clipboard must keep reporting Empty exactly as before this fix.
    [Fact]
    public async Task ReadTextAsync_StillIgnoresImagesAndReportsEmptyForTheTextOnlyPastePath()
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Image: new PlatformClipboardImage(OnePixelPngBytes, PixelWidth: 1, PixelHeight: 1))),
        };

        var result = await FreeWClipboardApplicationWorkflow.ReadTextAsync(clipboard);

        clipboard.LastReadRequest!.IncludeImage.Should().BeFalse(
            "Paste Text Only must not request an image it could never render as text");
        result.Status.Should().Be(FreeWClipboardTransferStatus.Empty);
        result.FeedbackMessage.Should().Be(FreeWClipboardApplicationWorkflow.EmptyClipboardMessage);
    }

    // Sibling no-regression: when the platform clipboard reports a bitmap with no usable pixel
    // dimensions (the Avalonia reader's decoded-bitmap-was-null branch), the PNG's own IHDR chunk must
    // still be decoded rather than the paste being silently dropped.
    [Fact]
    public async Task ReadPasteSpecialAsync_RecoversImageDimensionsFromPngHeaderWhenThePlatformReportsNone()
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Image: new PlatformClipboardImage(OnePixelPngBytes, PixelWidth: null, PixelHeight: null))),
        };

        var result = await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard);

        result.IsSuccess.Should().BeTrue();
        var run = result.Payload!.RichDocument!.Paragraphs.SelectMany(p => p.Runs).Single();
        run.Image!.OriginalPixelWidth.Should().Be(1);
        run.Image.OriginalPixelHeight.Should().Be(1);
    }

    [Fact]
    public async Task ReadTextAsync_ClassifiesEmptyAndFailureWithoutRendererExceptions()
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Empty(),
        };

        var empty = await FreeWClipboardApplicationWorkflow.ReadTextAsync(clipboard);
        empty.Status.Should().Be(FreeWClipboardTransferStatus.Empty);
        empty.FeedbackMessage.Should().Be(FreeWClipboardApplicationWorkflow.EmptyClipboardMessage);

        clipboard.ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Failed("busy");
        var failed = await FreeWClipboardApplicationWorkflow.ReadTextAsync(clipboard);
        failed.Status.Should().Be(FreeWClipboardTransferStatus.Failed);
        failed.FeedbackMessage.Should().Contain("busy");
    }

    [Fact]
    public async Task WriteSelectionAsync_PreservesCutCommitPolicyAndPayloadCreation()
    {
        var clipboard = new FakeClipboard
        {
            WriteResult = PlatformClipboardWriteResult.Unavailable(),
        };

        var unavailable = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(clipboard, "selected");
        unavailable.CanCommitCut.Should().BeTrue();
        clipboard.LastWrittenContent!.Text.Should().Be("selected");

        var empty = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(clipboard, string.Empty);
        empty.CanCommitCut.Should().BeFalse();
        empty.Status.Should().Be(FreeWClipboardTransferStatus.Empty);
    }

    // shell-clipboard F2: the Avalonia shell's editor is a custom control with no native rich-text
    // clipboard behaviour (unlike WPF's RichTextBox, which places RTF/Xaml on Copy automatically), so
    // WriteSelectionAsync must be given the resolved-formatting sub-document itself and place it as
    // an HTML clipboard payload -- otherwise a Bold/Italic run copied and pasted degrades to plain
    // text even within the same FreeW-Avalonia session. This is the failing-before-fix case.
    [Fact]
    public async Task WriteSelectionAsync_WritesHtmlCustomDataWhenRichDocumentIsProvided()
    {
        var clipboard = new FakeClipboard();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Bold", new RunFormatting { Bold = true }));
        var richDocument = new TextDocument();
        richDocument.Blocks.Add(paragraph);

        var result = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(
            clipboard,
            "Bold",
            richDocument);

        result.IsSuccess.Should().BeTrue();
        clipboard.LastWrittenContent!.Text.Should().Be("Bold");
        var html = clipboard.LastWrittenContent.GetText("text/html");
        html.Should().NotBeNull("a rich selection must carry an HTML clipboard payload so Paste can recover formatting");
        html.Should().Contain("<strong>Bold</strong>");
        clipboard.LastWrittenContent.GetText("HTML Format").Should().Be(html,
            "the Windows-named HTML format mirrors the cross-platform one so either read path recovers formatting");
    }

    // Sibling no-regression case: a plain Copy (no rich document, e.g. from a plain-text-only
    // selection context or a caller that never resolved one) must keep behaving exactly as before --
    // text-only clipboard content, no HTML CustomData fabricated out of nothing.
    [Fact]
    public async Task WriteSelectionAsync_WritesPlainTextOnlyWhenNoRichDocumentIsProvided()
    {
        var clipboard = new FakeClipboard();

        var result = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(clipboard, "plain only");

        result.IsSuccess.Should().BeTrue();
        clipboard.LastWrittenContent!.Text.Should().Be("plain only");
        clipboard.LastWrittenContent.CustomData.Should().BeEmpty();
    }

    [Fact]
    public void BuildSelectionRichDocument_SlicesRunsToTheSelectedOffsetsWithResolvedFormatting()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("plain "));
        paragraph.Runs.Add(new Run("bold", new RunFormatting { Bold = true }));
        var source = new TextDocument();
        source.Blocks.Add(paragraph);

        // Selects only "bold" (offsets 6..10 of "plain bold").
        var ranges = new[] { new DocumentFormattingTextRange(paragraph, 6, 10) };

        var richDocument = FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(source, ranges);

        richDocument.Should().NotBeNull();
        var slicedParagraph = richDocument!.Blocks.Should().ContainSingle().Subject.Should().BeOfType<Paragraph>().Subject;
        var run = slicedParagraph.Runs.Should().ContainSingle().Subject;
        run.Text.Should().Be("bold");
        run.Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void BuildSelectionRichDocument_ReturnsNullForAnEmptyRangeList()
    {
        var source = TextDocument.CreateEmpty();

        FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(source, ranges: []).Should().BeNull();
        FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(source, ranges: null).Should().BeNull();
    }

    [Theory]
    [InlineData(PasteSpecialOption.KeepTextOnly, DocumentPasteTextKind.TextOnly, false)]
    [InlineData(PasteSpecialOption.MergeFormatting, DocumentPasteTextKind.MergeFormatting, false)]
    [InlineData(PasteSpecialOption.KeepSourceFormatting, DocumentPasteTextKind.MergeFormatting, true)]
    public void PlanPaste_OwnsPasteSpecialFallbackPolicy(
        PasteSpecialOption option,
        DocumentPasteTextKind expectedKind,
        bool expectsRichDocument)
    {
        var document = TextDocument.CreateEmpty();
        var plan = FreeWClipboardApplicationWorkflow.PlanPaste(
            new FreeWClipboardPayload("text", document),
            option);

        plan.TextKind.Should().Be(expectedKind);
        plan.PreferRichDocument.Should().Be(expectsRichDocument);
    }

    /// <summary>
    /// FreeW's own clipboard flavour: RTF and HTML are what other applications read, and neither can
    /// express a content control, a tracked change's author, or a comment anchor — so a copy/paste
    /// within FreeW used to hand back plain formatted text. The native payload is the selection written
    /// in the format the document itself is saved in.
    /// </summary>
    [Fact]
    public async Task WriteSelectionAsync_AlsoWritesTheNativeFlavourAlongsideHtml()
    {
        var document = SelectionSource();
        var ranges = AllRanges(document);
        var clipboard = new FakeClipboard();

        await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(
            clipboard,
            document.PlainText,
            FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(document, ranges),
            FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(document, ranges));

        var written = clipboard.LastWrittenContent.Should().NotBeNull().And.Subject.As<PlatformClipboardContent>();
        written.Text.Should().Be(document.PlainText);
        written.GetText("text/html").Should().NotBeNullOrEmpty("other applications still get HTML");
        written.GetBytes(
                FreeWClipboardApplicationWorkflow.NativeDocumentFormat,
                PlatformClipboardFormatScope.Application)
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ReadPasteSpecialAsync_PrefersTheNativeFlavourAndKeepsWhatHtmlCannotCarry()
    {
        var document = SelectionSource();
        var ranges = AllRanges(document);
        var content = FreeWClipboardApplicationWorkflow.CreateWriteContent(
            document.PlainText,
            FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(document, ranges),
            FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(document, ranges))!;
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(content),
        };

        var payload = (await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard)).Payload!;

        var pastedRuns = payload.RichDocument.Should().NotBeNull().And.Subject
            .As<TextDocument>().Paragraphs.SelectMany(paragraph => paragraph.Runs).ToList();
        pastedRuns.Should().Contain(run => run.Control != null, "the form field survives the round trip");
        var field = pastedRuns.Single(run => run.Control != null);
        field.Text.Should().Be("Bob");
        field.Control!.Tag.Should().Be("Applicant");
        pastedRuns.Should().Contain(run => run.Revision == RevisionKind.Inserted,
            "a tracked insertion keeps its revision mark");
        pastedRuns.Single(run => run.Revision == RevisionKind.Inserted).RevisionAuthor.Should().Be("Ada");
    }

    [Fact]
    public async Task ReadPasteSpecialAsync_StillFallsBackToHtmlForAForeignClipboard()
    {
        // A payload from any other application has no FreeW flavour; the existing paths must still run.
        var document = SelectionSource();
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                FreeWClipboardApplicationWorkflow.CreateWriteContent(
                    document.PlainText,
                    FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(document, AllRanges(document)))!),
        };

        var payload = (await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard)).Payload!;

        payload.RichDocument.Should().NotBeNull();
        payload.RichDocument!.PlainText.Should().Contain("Bob");
    }

    [Fact]
    public void BuildSelectionNativeDocument_ClonesRunMarksAndCarriesTheSourceStyles()
    {
        var document = SelectionSource();

        var native = FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(
            document,
            [new DocumentFormattingTextRange(document.Paragraphs.First(), 6, 9)])!;

        var run = native.Paragraphs.Single().Runs.Should().ContainSingle().Subject;
        run.Text.Should().Be("Bob", "the slice is exactly the selected range");
        run.Control!.Tag.Should().Be("Applicant");
        native.Styles.Should().ContainKey("Quote", "the source's styles ride along for a cross-document paste");
        FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(document, ranges: []).Should().BeNull();
    }

    /// <summary>
    /// freew-bookmarks-hyperlinks F1: a whole-paragraph bookmark (the shape Insert &gt; Bookmark actually
    /// produces -- a name in <see cref="Paragraph.BookmarkNames"/> with no run-anchored boundary, see
    /// <see cref="BookmarkCommands.SetParagraphBookmarkNameCommand"/>) used to vanish from
    /// <see cref="FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument"/>'s output because the
    /// sliced <see cref="Paragraph"/> it built never copied <see cref="Paragraph.BookmarkNames"/> at all.
    /// A hyperlink pasted alongside it (an ordinary run mark, already preserved by
    /// <see cref="RevisionEditPlanner.CloneRunWithText"/>) would then resolve to nothing.
    /// </summary>
    [Fact]
    public void BuildSelectionNativeDocument_PreservesTheWholeParagraphBookmarkAndTheHyperlinkThatTargetsIt()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var target = new Paragraph();
        target.Runs.Add(new Run("Target paragraph"));
        target.BookmarkNames.Add("MyBookmark");
        document.Blocks.Add(target);

        var linker = new Paragraph();
        linker.Runs.Add(new Run("See here") { HyperlinkAnchor = "MyBookmark" });
        document.Blocks.Add(linker);

        var native = FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(
            document,
            AllRanges(document))!;

        native.Paragraphs.ElementAt(0).BookmarkNames.Should().Contain(
            "MyBookmark", "the bookmark travels with a full copy of the paragraph that carries it");
        native.Paragraphs.ElementAt(1).Runs.Single().HyperlinkAnchor.Should().Be(
            "MyBookmark", "the hyperlink anchor was already preserved -- it must still resolve to a name that also survived");
    }

    /// <summary>
    /// freew-bookmarks-hyperlinks F1 (regression): a copy that only spans PART of a paragraph must remap a
    /// run-anchored <see cref="BookmarkBoundary"/> (the shape <c>InsertCrossReferenceCommand</c>'s
    /// auto-bookmark and an imported Word bookmark use) into the new, shorter run list -- not copy its
    /// <see cref="BookmarkBoundary.RunIndex"/> verbatim, which would point at the wrong run (or past the end
    /// of the slice) once leading runs outside the selection are dropped.
    /// </summary>
    [Fact]
    public void BuildSelectionNativeDocument_RemapsABookmarkBoundaryThatWrapsOnlyPartOfAPartialSelection()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("AAA")); // offsets 0-3, excluded from the selection below
        paragraph.Runs.Add(new Run("BBB")); // offsets 3-6, the bookmarked run
        paragraph.Runs.Add(new Run("CCC")); // offsets 6-9
        paragraph.BookmarkNames.Add("_Ref1");
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("auto:_Ref1", BookmarkBoundaryKind.Start, 1, "_Ref1"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("auto:_Ref1", BookmarkBoundaryKind.End, 2, "_Ref1"));
        document.Blocks.Add(paragraph);

        // Select "BBBCCC" -- offsets [3,9) -- dropping the leading "AAA" run entirely, so the sliced
        // paragraph has 2 runs (not 3) and the boundary's original RunIndex values (1 and 2) no longer
        // name the right positions.
        var native = FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(
            document,
            [new DocumentFormattingTextRange(paragraph, 3, 9)])!;

        var sliced = native.Paragraphs.Single();
        sliced.PlainText.Should().Be("BBBCCC");
        sliced.BookmarkBoundaries.Should().HaveCount(2);
        var start = sliced.BookmarkBoundaries.Single(b => b.Kind == BookmarkBoundaryKind.Start);
        var end = sliced.BookmarkBoundaries.Single(b => b.Kind == BookmarkBoundaryKind.End);
        start.RunIndex.Should().Be(0, "the bookmarked run is now the first run of the slice");
        end.RunIndex.Should().Be(1, "the bookmark closes before the second (and last) run of the slice");
    }


    /// <summary>
    /// clip-RTF: the shell without a native editor (Avalonia) wrote only HTML, so a copy into an
    /// application that reads RTF and not HTML arrived as unformatted text.
    /// </summary>
    [Fact]
    public void CreateWriteContent_AlsoWritesRtfForApplicationsThatReadNoHtml()
    {
        var document = SelectionSource();
        var ranges = AllRanges(document);

        var content = FreeWClipboardApplicationWorkflow.CreateWriteContent(
            document.PlainText,
            FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(document, ranges),
            FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(document, ranges))!;

        var rtf = content.GetText(FreeWClipboardApplicationWorkflow.RichTextFormat);
        rtf.Should().StartWith(@"{\rtf1", "a receiving application expects a whole RTF document");
        rtf.Should().Contain("Bob");
    }

    /// <summary>
    /// The WPF shell hands over the RTF its native editor produced, which is richer than a re-render of
    /// the parsed model — it must replace the derived one, not sit beside it as a duplicate flavour.
    /// </summary>
    [Fact]
    public void CreateWriteContentFromRtf_KeepsTheNativeEditorsRtfAsTheOnlyRtfFlavour()
    {
        const string nativeRtf = @"{\rtf1\ansi\b Bold\b0  plain}";

        var content = FreeWClipboardApplicationWorkflow.CreateWriteContentFromRtf(
            "Bold plain",
            nativeRtf,
            nativeDocument: SelectionSource())!;

        content.CustomData
            .Where(data => data.Format.Name == FreeWClipboardApplicationWorkflow.RichTextFormat)
            .Should().ContainSingle().Which.Text.Should().Be(nativeRtf);
    }

    /// <summary>
    /// "Rich Text Format" is the Windows clipboard name; on Linux and macOS the format string is a MIME
    /// type, so writing only the Windows name left the RTF flavour invisible on exactly the shell that
    /// needed it. Both names carry the same payload, and a paste reads either.
    /// </summary>
    [Fact]
    public async Task Rtf_is_written_and_read_under_both_the_windows_name_and_the_mime_type()
    {
        var document = SelectionSource();
        var content = FreeWClipboardApplicationWorkflow.CreateWriteContent(
            document.PlainText,
            FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(document, AllRanges(document)))!;

        var windowsName = content.GetText(FreeWClipboardApplicationWorkflow.RichTextFormat);
        var mimeName = content.CustomData
            .Single(data => data.Format.Name == "text/rtf").Text;
        mimeName.Should().Be(windowsName).And.StartWith(@"{\rtf1");

        // A clipboard that offers ONLY the MIME name — a Linux source — still pastes as rich text.
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: document.PlainText,
                    CustomData: [PlatformClipboardData.FromText("text/rtf", mimeName)])),
        };

        var payload = (await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard)).Payload!;
        payload.RichDocument.Should().NotBeNull();
        payload.RichDocument!.PlainText.Should().Contain("Bob");
    }
    private static TextDocument SelectionSource()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["Quote"] = new DocumentStyle { Id = "Quote", Name = "Quote" };
        var paragraph = new Paragraph { StyleId = "Quote" };
        paragraph.Runs.Add(new Run("Name: "));
        paragraph.Runs.Add(Run.PlainTextControl("Bob", tag: "Applicant"));
        paragraph.Runs.Add(new Run(" added")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Ada",
        });
        document.Blocks.Add(paragraph);
        return document;
    }

    private static readonly byte[] OnePixelPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>
    /// r165 remediation. Carrying bookmark boundaries into a partial copy clamped EVERY boundary into
    /// the selected range, so a bookmark the user did not select was manufactured into the pasted text
    /// as a zero-width pair at offset 0. That is the mirror image of the bug the carrying fixes, and it
    /// was newly possible: before that change Copy/Cut never touched boundaries at all.
    /// </summary>
    [Fact]
    public void BuildSelectionNativeDocument_DoesNotInventABookmarkTheSelectionExcludes()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("AAA"));
        paragraph.Runs.Add(new Run("BBB"));
        paragraph.Runs.Add(new Run("CCC"));
        // The bookmark spans only the first run; the selection below starts after it ends.
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("pair-1", BookmarkBoundaryKind.Start, 0, "_Ref1"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("pair-1", BookmarkBoundaryKind.End, 1));
        document.Blocks.Add(paragraph);

        var native = FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(
            document,
            [new DocumentFormattingTextRange(paragraph, 6, 9)])!;

        native.Paragraphs.Single().BookmarkBoundaries.Should().BeEmpty(
            "a bookmark that ends before the selection begins was never copied, so it must not appear in the paste");
    }

    /// <summary>
    /// r166. The r165 guard located a bookmark's other half inside the SAME paragraph, and treated a
    /// missing partner as "the span continues past this paragraph, so it overlaps". A bookmark that
    /// spans a paragraph break records its Start only in the first paragraph and its End only in the
    /// last -- so the partner is always absent, the guard always said yes, and the phantom it was
    /// written to prevent came straight back for exactly the bookmarks most likely to exist.
    /// A half-pair is an open-ended span: a Start covers what follows it, an End what precedes it.
    /// </summary>
    [Fact]
    public void BuildSelectionNativeDocument_DoesNotInventACrossParagraphBookmarkTheSelectionPrecedes()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var first = new Paragraph();
        first.Runs.Add(new Run("Intro"));
        first.Runs.Add(new Run(" tail"));
        // The bookmark opens at the very END of this paragraph and closes in a later one, so only the
        // Start lives here -- the shape the model documents for a cross-paragraph bookmark.
        first.BookmarkBoundaries.Add(new BookmarkBoundary("pair-x", BookmarkBoundaryKind.Start, 2, "MyBookmark"));
        document.Blocks.Add(first);

        var last = new Paragraph();
        last.Runs.Add(new Run("End here"));
        last.BookmarkBoundaries.Add(new BookmarkBoundary("pair-x", BookmarkBoundaryKind.End, 1));
        document.Blocks.Add(last);

        // Copy only "Intro" -- entirely before the bookmark opens.
        var native = FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(
            document,
            [new DocumentFormattingTextRange(first, 0, 5)])!;

        native.Paragraphs.Single().BookmarkBoundaries.Should().BeEmpty(
            "the bookmark opens after this selection ends, so none of the copied text was inside it");
    }

    /// <summary>
    /// Sibling to the case above: text taken from AFTER a cross-paragraph bookmark opens really is
    /// inside it, so the Start must still travel. Without this, dropping half-pairs outright would
    /// trade one silent bookmark loss for another.
    /// </summary>
    [Fact]
    public void BuildSelectionNativeDocument_KeepsACrossParagraphBookmarkTheSelectionIsInside()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var first = new Paragraph();
        first.Runs.Add(new Run("Intro"));
        first.Runs.Add(new Run(" tail"));
        first.BookmarkBoundaries.Add(new BookmarkBoundary("pair-x", BookmarkBoundaryKind.Start, 1, "MyBookmark"));
        document.Blocks.Add(first);

        var native = FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(
            document,
            [new DocumentFormattingTextRange(first, 5, 10)])!;

        native.Paragraphs.Single().BookmarkBoundaries.Should().ContainSingle()
            .Which.Name.Should().Be("MyBookmark");
    }

    private static IReadOnlyList<DocumentFormattingTextRange> AllRanges(TextDocument document) =>
        document.Paragraphs
            .Select(paragraph => new DocumentFormattingTextRange(paragraph, 0, paragraph.PlainText.Length))
            .ToList();

    private sealed class FakeClipboard : IPlatformClipboard
    {
        public PlatformClipboardReadResult<PlatformClipboardContent> ReadResult { get; set; } =
            PlatformClipboardReadResult<PlatformClipboardContent>.Empty();

        public PlatformClipboardWriteResult WriteResult { get; set; } =
            PlatformClipboardWriteResult.Success();

        public PlatformClipboardReadRequest? LastReadRequest { get; private set; }

        public PlatformClipboardContent? LastWrittenContent { get; private set; }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default)
        {
            LastReadRequest = request;
            return ValueTask.FromResult(ReadResult);
        }

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default)
        {
            LastWrittenContent = content;
            return ValueTask.FromResult(WriteResult);
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}

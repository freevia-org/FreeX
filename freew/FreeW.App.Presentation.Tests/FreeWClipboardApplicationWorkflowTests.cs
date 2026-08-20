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

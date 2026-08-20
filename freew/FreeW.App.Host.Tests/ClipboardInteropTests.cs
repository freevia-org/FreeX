using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// clipboard-interop F1/F2: proves the WPF host's Paste/Copy/Cut now go through the shared
/// <see cref="FreeWClipboardApplicationWorkflow"/> and the injected <see cref="IPlatformClipboard"/>
/// instead of falling through to native RichTextBox handling (which talks directly to
/// <see cref="System.Windows.Clipboard"/> and never touches the app's own abstraction at all). Every
/// assertion below distinguishes "went through the fix" from "fell through to native" by whether the
/// injected <see cref="FakeClipboard"/> was read/written -- before the fix that never happens for
/// Paste/Copy/Cut, so these are deterministic regardless of the OS-focus-timing flakiness this test
/// project's other suites warn about for actual native keystroke-driven FlowDocument mutation (not
/// exercised here: the paste path that runs is <c>DocumentView.PasteKeepSourceFormatting()</c>'s
/// model-level structural paste, and Cut's own deletion is a direct <c>TextRange.Text</c> assignment,
/// neither of which depends on real window focus). Runs on an STA thread ([StaFact]) because
/// RichTextBox needs one.
/// </summary>
public sealed class ClipboardInteropTests
{
    // F1: default Paste must recover rich formatting through the shared workflow ---------------------

    [StaFact]
    public void Paste_RecoversRichFormatting_FromTheInjectedClipboard_NotNativeHandling()
    {
        // Seed the FAKE clipboard with RTF carrying a bold run, and the REAL system clipboard with
        // unrelated plain text, so the two are distinguishable: only a Paste that reads through
        // FreeWClipboardApplicationWorkflow (this fix) can produce the bold run below. Before the fix,
        // Paste fell through to native RichTextBox handling, which reads System.Windows.Clipboard
        // directly and ignores the app's own IPlatformClipboard entirely.
        const string rtf = @"{\rtf1\ansi\b Bold\b0  plain}";
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "Bold plain",
                    CustomData: [PlatformClipboardData.FromText(
                        FreeWClipboardApplicationWorkflow.RichTextFormat,
                        rtf)])),
        };
        SeedSystemClipboardText("SystemOnly-must-never-appear-in-the-model");

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView(clipboard);
        view.LoadModel(document);
        var wpfParagraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = wpfParagraph.ContentStart;

        ApplicationCommands.Paste.CanExecute(null, view).Should().BeTrue();
        ApplicationCommands.Paste.Execute(null, view);

        view.CommitToModel();
        var resultParagraph = (Paragraph)view.Model.Blocks[0];
        resultParagraph.Runs.Should().Contain(
            run => run.Formatting.Bold && run.Text.Contains("Bold"),
            "ordinary Paste must recover the SAME formatting Paste Special already recovers from this clipboard content");
        resultParagraph.PlainText.Should().NotContain("SystemOnly",
            "Paste must read the app's own clipboard abstraction, not fall through to native handling of the real OS clipboard");
    }

    // Sibling no-regression case for F1: a plain-text-only clipboard (no RTF/HTML) must keep pasting as
    // plain text exactly as before -- the fix must not require rich content to be present.
    [StaFact]
    public void Paste_PlainTextOnlyClipboard_StillPastesPlainText()
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(Text: "Plain only")),
        };

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView(clipboard);
        view.LoadModel(document);
        var wpfParagraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = wpfParagraph.ContentStart;

        ApplicationCommands.Paste.Execute(null, view);

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("Plain only");
    }

    // R159 remediation: a clipboard carrying BOTH a bitmap and independent plain text (a screenshot
    // tool that also copies the saved file path, say) used to lose the text. PlanPaste(KeepSourceFormatting)
    // prefers RichDocument unconditionally over Text, and the synthesized image RichDocument
    // (freew-paste-formats F1's TryBuildImageDocument) carries none of the clipboard's Text the way an
    // HTML/RTF RichDocument does. ApplyClipboardPastePlan only fell back to Text when the rich insert
    // FAILED -- never when it succeeded -- so a successful image paste silently discarded the
    // accompanying text. Before this fix, the same clipboard pasted the text (there was no image branch
    // yet); this proves the text is not lost now that the image branch exists.
    [StaFact]
    public void Paste_ImageWithAccompanyingText_KeepsBothTheImageAndTheText()
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "C:\\Users\\ann\\Pictures\\screenshot.png",
                    Image: new PlatformClipboardImage(OnePixelPngBytes, PixelWidth: 1, PixelHeight: 1))),
        };

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView(clipboard);
        view.LoadModel(document);
        var wpfParagraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = wpfParagraph.ContentStart;

        ApplicationCommands.Paste.Execute(null, view);

        view.CommitToModel();
        view.Model.Paragraphs.SelectMany(p => p.Runs).Should().Contain(
            run => run.Image != null,
            "the pasted bitmap must still land as an inline picture");
        view.Model.Paragraphs.Any(p => p.PlainText.Contains("screenshot.png")).Should().BeTrue(
            "the clipboard's independent plain text must survive alongside the image, not be silently discarded");
    }

    // Sibling no-regression: an HTML/RTF-derived RichDocument already contains the clipboard's Text
    // (RTF/HTML rendering IS the text plus formatting), so the extra text-insert this fix adds must not
    // fire for it -- otherwise pasting "Bold plain" would land as "Bold plainBold plain".
    [StaFact]
    public void Paste_RtfWithAccompanyingText_DoesNotDuplicateTheText()
    {
        const string rtf = @"{\rtf1\ansi\b Bold\b0  plain}";
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "Bold plain",
                    CustomData: [PlatformClipboardData.FromText(
                        FreeWClipboardApplicationWorkflow.RichTextFormat,
                        rtf)])),
        };

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView(clipboard);
        view.LoadModel(document);
        var wpfParagraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = wpfParagraph.ContentStart;

        ApplicationCommands.Paste.Execute(null, view);

        view.CommitToModel();
        var plainText = string.Concat(view.Model.Paragraphs.Select(p => p.PlainText));
        plainText.Should().Be(
            "Bold plain",
            "an HTML/RTF RichDocument already contains the clipboard's Text, so it must not be inserted a second time");
    }

    [StaFact]
    public void Paste_CanExecute_False_WhenReadOnly()
    {
        // Regression guard: the new Paste CommandBinding must still refuse when the surface is
        // read-only, exactly like the native handling it replaced.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body"));
        document.Blocks.Add(paragraph);
        var view = new DocumentView(new FakeClipboard());
        view.LoadModel(document);
        view.IsReadOnly = true;

        ApplicationCommands.Paste.CanExecute(null, view).Should().BeFalse();
    }

    // F2: Copy/Cut must write an HTML flavor through the shared workflow -----------------------------

    private static TextDocument BuildBoldPlainDocument()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Bold", new RunFormatting { Bold = true }));
        paragraph.Runs.Add(new Run("plain"));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        return document;
    }

    private static void SelectWholeParagraph(DocumentView view)
    {
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
    }

    [StaFact]
    public void Copy_WritesHtmlFlavor_ThroughTheInjectedClipboard()
    {
        // Before the fix, Copy fell through to native RichTextBox handling, which writes
        // Text/RTF/Xaml directly to System.Windows.Clipboard and never calls the app's own
        // IPlatformClipboard.WriteAsync at all -- so LastWrittenContent stays null. After the fix, Copy
        // always writes through the shared workflow, and the written content carries an HTML flavor
        // (what browsers/webmail read) alongside the RTF (so RTF-only consumers keep working).
        var clipboard = new FakeClipboard();
        var view = new DocumentView(clipboard);
        view.LoadModel(BuildBoldPlainDocument());
        SelectWholeParagraph(view);

        ApplicationCommands.Copy.CanExecute(null, view).Should().BeTrue();
        ApplicationCommands.Copy.Execute(null, view);

        clipboard.LastWrittenContent.Should().NotBeNull(
            "Copy must write through the app's own clipboard abstraction, not fall through to native handling");
        var content = clipboard.LastWrittenContent!;
        var html = content.GetText("text/html");
        html.Should().NotBeNullOrEmpty("Copy must place an HTML flavor so browsers/webmail recover formatting");
        html.Should().Contain("Bold");
        content.GetText(FreeWClipboardApplicationWorkflow.RichTextFormat).Should().NotBeNullOrEmpty(
            "the RTF flavor native Copy always wrote must still be present -- RTF-only consumers must keep working");
    }

    [StaFact]
    public void Cut_WritesHtmlFlavor_AndDeletesTheSelection()
    {
        var clipboard = new FakeClipboard();
        var view = new DocumentView(clipboard);
        view.LoadModel(BuildBoldPlainDocument());
        SelectWholeParagraph(view);

        ApplicationCommands.Cut.CanExecute(null, view).Should().BeTrue();
        ApplicationCommands.Cut.Execute(null, view);

        clipboard.LastWrittenContent.Should().NotBeNull();
        clipboard.LastWrittenContent!.GetText("text/html").Should().NotBeNullOrEmpty();

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().BeEmpty(
            "Cut must delete the selected content the same way native Cut always did");
    }

    // Sibling no-regression case for F2: an empty selection must still refuse Copy, matching native
    // behavior -- the fix must not make Copy fire (and crash trying to export an empty RTF range) when
    // there is nothing selected.
    [StaFact]
    public void Copy_CanExecute_False_WhenSelectionIsEmpty()
    {
        var view = new DocumentView(new FakeClipboard());
        view.LoadModel(BuildBoldPlainDocument());

        ApplicationCommands.Copy.CanExecute(null, view).Should().BeFalse();
    }

    private static readonly byte[] OnePixelPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static void SeedSystemClipboardText(string text)
    {
        // Best-effort, same tolerance as ContentControlKeyboardLockTests.SeedClipboardText: this heavily
        // parallel dev environment can produce transient CLIPBRD_E_CANT_OPEN contention.
        const int MaxAttempts = 3;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch when (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(10);
            }
            catch
            {
                // Final attempt failed -- stays best-effort.
            }
        }
    }

    private sealed class FakeClipboard : IPlatformClipboard
    {
        public PlatformClipboardReadResult<PlatformClipboardContent> ReadResult { get; set; } =
            PlatformClipboardReadResult<PlatformClipboardContent>.Empty();

        public PlatformClipboardWriteResult WriteResult { get; set; } =
            PlatformClipboardWriteResult.Success();

        public PlatformClipboardContent? LastWrittenContent { get; private set; }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadResult);

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

using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Avalonia twin of FreeW.App.Host.Tests.ClipboardInteropTests (F1): proves ordinary Paste (Ctrl+V,
/// entered exactly as the user would -- a real KeyDown routed through MainWindow_KeyDown and
/// FreeWApplicationCommandRouter, not a direct call to the implementation method) now recovers rich
/// formatting from the injected <see cref="IPlatformClipboard"/> instead of always degrading to plain
/// text. Before this fix, <c>MainWindow.PasteAsync</c> called
/// <c>FreeWClipboardApplicationWorkflow.ReadTextAsync</c>, which reads with includeRichDocument:false
/// and so could never recover formatting -- even though the WPF host's Ctrl+V already did, and even
/// though this same Avalonia shell's own Paste Special &gt; Keep Source Formatting already worked from
/// the identical clipboard content.
/// </summary>
public sealed class PasteRichFormattingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static KeyEventArgs CtrlV() => new() { Key = Key.V, KeyModifiers = KeyModifiers.Control };

    private static MainWindow CreateWindow(IPlatformClipboard clipboard)
    {
        var window = new MainWindow(
            [],
            null,
            InMemoryApplicationOptionsStore<FreeWOptions>.ForProductFile(
                PlatformApplicationDataPathProvider.LocalInstance),
            platformClipboard: clipboard);

        // The shell's default startup document (FreeWSampleDocumentFactory) is non-empty and its
        // title run is itself bold, which would make a rich-paste assertion pass for the wrong
        // reason. Load a single-empty-paragraph document -- the same shape the WPF regression test
        // starts from -- so the only way a bold run can appear is via the fix under test.
        var empty = TextDocument.CreateEmpty();
        empty.Blocks.Clear();
        empty.Blocks.Add(new Paragraph());
        window.Editor.LoadDocument(empty);

        return window;
    }

    [Fact]
    public async Task CtrlV_RecoversRichFormatting_FromTheInjectedClipboard()
    {
        // Seed a fake clipboard with RTF carrying a bold run -- the same shape the WPF regression
        // test uses -- so only a Paste that reads through FreeWClipboardApplicationWorkflow's rich
        // path (this fix) can produce the bold run below.
        const string rtf = @"{\rtf1\ansi\b Bold\b0  plain}";
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "Bold plain",
                    CustomData: [PlatformClipboardData.FromText("Rich Text Format", rtf)])),
        };

        await Session.Dispatch(() =>
        {
            var window = CreateWindow(clipboard);
            try
            {
                var args = CtrlV();
                window.RaiseKeyDownForTest(args);
                args.Handled.Should().BeTrue("Ctrl+V must be claimed by the Paste command, not fall through unhandled");

                var paragraph = (Paragraph)window.Editor.Document.Blocks[0];
                paragraph.Runs.Should().Contain(
                    run => run.Formatting.Bold && run.Text.Contains("Bold"),
                    "ordinary Ctrl+V must recover the SAME formatting Paste Special already recovers from this clipboard content");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    // Sibling no-regression case for this fix: a plain-text-only clipboard (no RTF/HTML) must keep
    // pasting as plain text exactly as before -- the fix must not require rich content to be present.
    [Fact]
    public async Task CtrlV_PlainTextOnlyClipboard_StillPastesPlainText()
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(Text: "Plain only")),
        };

        await Session.Dispatch(() =>
        {
            var window = CreateWindow(clipboard);
            try
            {
                window.RaiseKeyDownForTest(CtrlV());

                var paragraph = (Paragraph)window.Editor.Document.Blocks[0];
                paragraph.PlainText.Should().Be("Plain only");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private sealed class FakeClipboard : IPlatformClipboard
    {
        public PlatformClipboardReadResult<PlatformClipboardContent> ReadResult { get; set; } =
            PlatformClipboardReadResult<PlatformClipboardContent>.Empty();

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadResult);

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}

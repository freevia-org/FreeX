using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// The Avalonia paste path must orphan an internal slide-jump hyperlink the destination deck
/// cannot resolve, matching the WPF editors. Without the destination slide ids the editor keeps
/// the captured target, so decks edited outside a presentation are unaffected.
/// </summary>
public sealed class AvaloniaCrossPresentationHyperlinkPasteTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private const string SourceSlideId = "source-slide-id";
    private const string DestinationSlideId = "destination-slide-id";

    [Fact]
    public async Task Paste_DropsSlideJumpTargetMissingFromTheDestinationDeck()
    {
        await Session.Dispatch(async () =>
        {
            var editor = PasteTargetEditor(
                HyperlinkedClipboard(SourceSlideId),
                () => new[] { DestinationSlideId });

            (await editor.PasteClipboardAsync()).Should().BeTrue();

            var hyperlink = SoleHyperlink(editor.EditedBody);
            hyperlink.TargetSlideId.Should().BeNull();
            hyperlink.Url.Should().Be("https://example.test");
            hyperlink.Tooltip.Should().Be("jump");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Paste_KeepsSlideJumpTargetPresentInTheDestinationDeck()
    {
        await Session.Dispatch(async () =>
        {
            var editor = PasteTargetEditor(
                HyperlinkedClipboard(DestinationSlideId),
                () => new[] { DestinationSlideId });

            (await editor.PasteClipboardAsync()).Should().BeTrue();

            SoleHyperlink(editor.EditedBody).TargetSlideId.Should().Be(DestinationSlideId);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Paste_WithoutASlideIdProviderKeepsTheCapturedTarget()
    {
        await Session.Dispatch(async () =>
        {
            var editor = PasteTargetEditor(
                HyperlinkedClipboard(SourceSlideId),
                destinationSlideIdsProvider: null);

            (await editor.PasteClipboardAsync()).Should().BeTrue();

            SoleHyperlink(editor.EditedBody).TargetSlideId.Should().Be(SourceSlideId);
            return true;
        }, CancellationToken.None);
    }

    private static AvaloniaRichTextEditor PasteTargetEditor(
        IPlatformClipboard clipboard,
        Func<IReadOnlyCollection<string>?>? destinationSlideIdsProvider)
    {
        var editor = new AvaloniaRichTextEditor(
            InCanvasRichClipboardPayload.FromPlainText("target").Body,
            backgroundAlpha: 0xCC,
            clipboard: clipboard,
            destinationSlideIdsProvider: destinationSlideIdsProvider);
        editor.SelectionStart = 0;
        editor.SelectionEnd = editor.Text.Length;
        return editor;
    }

    /// <summary>
    /// A clipboard holding only the neutral rich payload, so the paste resolves through
    /// <see cref="InCanvasRichClipboardPlanner.Apply"/> rather than the plain-text fallback.
    /// </summary>
    private static IPlatformClipboard HyperlinkedClipboard(string targetSlideId)
    {
        var body = InCanvasRichClipboardPayload.FromPlainText("Jump").Body;
        body.Paragraphs[0].Runs[0].Hyperlink = new Hyperlink
        {
            Url = "https://example.test",
            TargetSlideId = targetSlideId,
            Tooltip = "jump",
        };
        var payload = InCanvasRichClipboardPlanner.Capture(
            body,
            new InCanvasEditorTextSelection(0, body.Paragraphs[0].Runs[0].Text.Length));

        return new StubClipboard(PresentationClipboardPlatformMapper.ToPlatformContent(
            new PresentationClipboardContent
            {
                Text = payload.PlainText,
                RichTextBytes = InCanvasRichClipboardPlanner.Serialize(payload),
            },
            PlatformClipboardFormatScope.Platform));
    }

    private static Hyperlink SoleHyperlink(TextBody body) =>
        body.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Hyperlink)
            .Where(link => link is not null)
            .Should().ContainSingle().Subject!;

    private sealed class StubClipboard : IPlatformClipboard
    {
        private readonly PlatformClipboardContent _content;

        internal StubClipboard(PlatformClipboardContent content) => _content = content;

        public bool IsAvailable => true;

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                PlatformClipboardReadResult<PlatformClipboardContent>.Success(_content));

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}

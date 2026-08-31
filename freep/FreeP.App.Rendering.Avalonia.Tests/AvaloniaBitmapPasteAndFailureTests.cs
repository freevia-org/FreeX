using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Compositor;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// Avalonia always consumes Ctrl+V, so it never had WPF's accidental native-paste fallback --
/// which meant a bitmap-only clipboard silently did nothing here while it pasted on WPF, and a
/// clipboard the editor could not read was indistinguishable from an empty one.
/// </summary>
public sealed class AvaloniaBitmapPasteAndFailureTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");

    [Fact]
    public async Task Paste_InsertsABitmapOnlyClipboardAsAnInlineImage()
    {
        await Session.Dispatch(async () =>
        {
            var editor = Editor(new StubClipboard
            {
                ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                    PresentationClipboardPlatformMapper.ToPlatformContent(
                        new PresentationClipboardContent(PngBytes: Png))),
            });

            (await editor.PasteClipboardAsync()).Should().BeTrue();

            editor.EditedBody.Paragraphs
                .SelectMany(paragraph => paragraph.Runs)
                .Should().ContainSingle(run => run.InlineImage != null);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Paste_ReportsAFailedClipboardReadInsteadOfLookingEmpty()
    {
        await Session.Dispatch(async () =>
        {
            var editor = Editor(new StubClipboard
            {
                ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Failed("busy"),
            });

            (await editor.PasteClipboardAsync()).Should().BeFalse();

            editor.LastClipboardFailureMessage.Should().Be("busy");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Paste_ClearsAStaleFailureOnceAReadSucceeds()
    {
        await Session.Dispatch(async () =>
        {
            var clipboard = new StubClipboard
            {
                ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Failed("busy"),
            };
            var editor = Editor(clipboard);
            await editor.PasteClipboardAsync();
            editor.LastClipboardFailureMessage.Should().Be("busy");

            clipboard.ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                PresentationClipboardPlatformMapper.ToPlatformContent(
                    new PresentationClipboardContent { Text = "pasted" }));
            await editor.PasteClipboardAsync();

            editor.LastClipboardFailureMessage.Should().BeNull();
            return true;
        }, CancellationToken.None);
    }

    private static AvaloniaRichTextEditor Editor(IPlatformClipboard clipboard)
    {
        var editor = new AvaloniaRichTextEditor(
            InCanvasRichClipboardPayload.FromPlainText("target").Body,
            backgroundAlpha: 0xCC,
            clipboard: clipboard);
        editor.SelectionStart = 0;
        editor.SelectionEnd = editor.Text.Length;
        return editor;
    }

    private sealed class StubClipboard : IPlatformClipboard
    {
        public bool IsAvailable => true;

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

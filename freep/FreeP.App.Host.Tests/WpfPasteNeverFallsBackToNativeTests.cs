using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Ctrl+V must always be consumed by the adapter. Leaving it unhandled hands the key to the
/// RichTextBox's own paste, which writes the clipboard into the document without the payload
/// path -- no inline OLE routing, no table cell styles, and a slide-jump target from whichever
/// deck the XamlPackage came from. Copy and cut keep their deliberate unhandled-on-failure
/// fallback; only paste changes here.
/// </summary>
public sealed class WpfPasteNeverFallsBackToNativeTests
{
    [StaFact]
    public async Task PreviewKeyDown_FailedClipboardReadHandlesTheKeyAndReportsTheFailure()
    {
        await RunWithBox(async (box, body) =>
        {
            var clipboard = new StubClipboard
            {
                ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Failed("busy"),
            };
            var eventArgs = PreviewKey(box, Key.V);

            var result = await WpfRichTextClipboardAdapter.HandlePreviewKeyDownAsync(
                eventArgs,
                box,
                body,
                clipboard);

            result.Handled.Should().BeTrue();
            eventArgs.Handled.Should().BeTrue();
            result.FailureMessage.Should().Be("busy");
            result.UpdatedBody.Should().BeNull();
        });
    }

    [StaFact]
    public async Task PreviewKeyDown_EmptyClipboardStillHandlesTheKeyWithoutAnUpdatedBody()
    {
        await RunWithBox(async (box, body) =>
        {
            var clipboard = new StubClipboard
            {
                ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                    PresentationClipboardPlatformMapper.ToPlatformContent(
                        new PresentationClipboardContent())),
            };
            var eventArgs = PreviewKey(box, Key.V);

            var result = await WpfRichTextClipboardAdapter.HandlePreviewKeyDownAsync(
                eventArgs,
                box,
                body,
                clipboard);

            result.Handled.Should().BeTrue();
            eventArgs.Handled.Should().BeTrue();
            // No body means nothing was applied -- the editors key off UpdatedBody, not Handled,
            // so a consumed-but-empty paste must not look like a paste that produced an empty body.
            result.UpdatedBody.Should().BeNull();
            result.FailureMessage.Should().BeNull();
        });
    }

    [StaFact]
    public async Task PreviewKeyDown_BitmapOnlyClipboardPastesThroughThePayloadPath()
    {
        await RunWithBox(async (box, body) =>
        {
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
            var clipboard = new StubClipboard
            {
                ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                    PresentationClipboardPlatformMapper.ToPlatformContent(
                        new PresentationClipboardContent(PngBytes: png))),
            };
            var eventArgs = PreviewKey(box, Key.V);

            var result = await WpfRichTextClipboardAdapter.HandlePreviewKeyDownAsync(
                eventArgs,
                box,
                body,
                clipboard);

            result.Handled.Should().BeTrue();
            result.UpdatedBody!.Paragraphs
                .SelectMany(paragraph => paragraph.Runs)
                .Should().ContainSingle(run => run.InlineImage != null);
        });
    }

    private static async Task RunWithBox(Func<RichTextBox, TextBody, Task> body)
    {
        var model = InCanvasRichClipboardPayload.FromPlainText("target").Body;
        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(model, 12));
        box.SelectAll();
        var window = new Window { Content = box };
        window.Show();
        try
        {
            await body(box, model);
        }
        finally
        {
            window.Close();
        }
    }

    private static KeyEventArgs PreviewKey(RichTextBox box, Key key)
    {
        var source = PresentationSource.FromVisual(box)
            ?? throw new InvalidOperationException("WPF RichTextBox has no presentation source.");
        return new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent,
        };
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

using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Compositor;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class AvaloniaRichTextClipboardBoundaryTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    [Fact]
    public async Task CopyAndPaste_RouteRichFormatsThroughNeutralBoundary()
    {
        await Session.Dispatch(async () =>
        {
            var clipboard = new RecordingClipboard();
            var editor = new AvaloniaRichTextEditor(
                InCanvasRichClipboardPayload.FromPlainText("Avalonia rich text").Body,
                backgroundAlpha: 0xCC,
                clipboard: clipboard);
            editor.SelectionStart = 0;
            editor.SelectionEnd = editor.Text.Length;
            using var cancellation = new CancellationTokenSource();

            (await editor.CopySelectionAsync(cancellation.Token)).Should().BeTrue();

            clipboard.WriteToken.Should().Be(cancellation.Token);
            clipboard.Written.Should().NotBeNull();
            var written = PresentationClipboardPlatformMapper.FromPlatformContent(
                clipboard.Written!);
            written.Text.Should().Be("Avalonia rich text");
            InCanvasRichClipboardPlanner.Deserialize(written.RichTextBytes)!.PlainText
                .Should().Be("Avalonia rich text");
            written.XamlPackageBytes.Should().NotBeNullOrEmpty();
            written.RtfBytes.Should().NotBeNullOrEmpty();

            clipboard.ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                clipboard.Written!);
            editor.Text = "target";
            editor.SelectionStart = 0;
            editor.SelectionEnd = editor.Text.Length;

            (await editor.PasteClipboardAsync()).Should().BeTrue();
            editor.Text.Should().Be("Avalonia rich text");
            clipboard.ReadRequest.Should().BeSameAs(
                PresentationClipboardPlatformMapper.RichTextReadRequest);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedWriteDoesNotCut_AndCancellationReachesBoundary()
    {
        await Session.Dispatch(async () =>
        {
            var clipboard = new RecordingClipboard
            {
                WriteResult = PlatformClipboardWriteResult.Unavailable("busy"),
            };
            var editor = new AvaloniaRichTextEditor(
                InCanvasRichClipboardPayload.FromPlainText("keep me").Body,
                backgroundAlpha: 0xCC,
                clipboard: clipboard);
            editor.SelectionStart = 0;
            editor.SelectionEnd = editor.Text.Length;

            (await editor.CutSelectionAsync()).Should().BeFalse();
            editor.Text.Should().Be("keep me");

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                editor.CopySelectionAsync(cancellation.Token));
        }, CancellationToken.None);
    }

    private sealed class RecordingClipboard : IPlatformClipboard
    {
        public bool IsAvailable => true;

        public PlatformClipboardReadRequest? ReadRequest { get; private set; }

        public PlatformClipboardContent? Written { get; private set; }

        public CancellationToken WriteToken { get; private set; }

        public PlatformClipboardReadResult<PlatformClipboardContent> ReadResult { get; set; } =
            PlatformClipboardReadResult<PlatformClipboardContent>.Empty();

        public PlatformClipboardWriteResult WriteResult { get; init; } =
            PlatformClipboardWriteResult.Success();

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadRequest = request;
            return ValueTask.FromResult(ReadResult);
        }

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Written = content;
            WriteToken = cancellationToken;
            return ValueTask.FromResult(WriteResult);
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}

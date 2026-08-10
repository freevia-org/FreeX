using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;

namespace FreeP.App.Host.Tests;

public sealed class WpfRichTextClipboardBoundaryTests
{
    [StaFact]
    public async Task CopyAsync_WritesRichNativeFormatsThroughNeutralBoundary()
    {
        var body = InCanvasRichClipboardPayload.FromPlainText("WPF rich text").Body;
        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(body, 12));
        box.SelectAll();
        var clipboard = new RecordingClipboard();
        using var cancellation = new CancellationTokenSource();

        (await WpfRichTextClipboardAdapter.TryCopyAsync(
            box,
            body,
            clipboard,
            cancellation.Token)).Should().BeTrue();

        clipboard.WriteToken.Should().Be(cancellation.Token);
        clipboard.Written.Should().NotBeNull();
        var content = PresentationClipboardPlatformMapper.FromPlatformContent(
            clipboard.Written!);
        content.Text.Should().Be("WPF rich text");
        InCanvasRichClipboardPlanner.Deserialize(content.RichTextBytes)!.PlainText
            .Should().Be("WPF rich text");
        content.XamlPackageBytes.Should().NotBeNullOrEmpty();
        content.RtfBytes.Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public async Task CutAsync_FailedWritePreservesSelectionAndText()
    {
        var body = InCanvasRichClipboardPayload.FromPlainText("keep me").Body;
        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(body, 12));
        box.SelectAll();
        var clipboard = new RecordingClipboard
        {
            WriteResult = PlatformClipboardWriteResult.Failed("busy"),
        };

        (await WpfRichTextClipboardAdapter.TryCutAsync(box, body, clipboard))
            .Should().BeFalse();

        box.Selection.Text.TrimEnd('\r', '\n').Should().Be("keep me");
    }

    [StaFact]
    public async Task PasteAsync_UsesNeutralReadOutcomeAndPropagatesCancellation()
    {
        var source = InCanvasRichClipboardPayload.FromPlainText("pasted");
        var clipboard = new RecordingClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                PresentationClipboardPlatformMapper.ToPlatformContent(
                    PresentationRichTextClipboardWorkflow.CreateWriteContent(
                        source,
                        xamlPackageBytes: null,
                        rtfBytes: null))),
        };
        var target = InCanvasRichClipboardPayload.FromPlainText("target").Body;
        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        box.SelectAll();

        var result = await WpfRichTextClipboardAdapter.TryPasteAsync(box, target, clipboard);

        result.Applied.Should().BeTrue();
        result.UpdatedBody!.Paragraphs.Single().Runs.Single().Text.Should().Be("pasted");
        clipboard.ReadRequest.Should().BeSameAs(
            PresentationClipboardPlatformMapper.RichTextReadRequest);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WpfRichTextClipboardAdapter.TryPasteAsync(
                box,
                result.UpdatedBody,
                clipboard,
                cancellation.Token).AsTask());
    }

    [StaFact]
    public async Task PreviewKeyDown_FailedClipboardWriteLeavesEventUnhandled()
    {
        var body = InCanvasRichClipboardPayload.FromPlainText("native fallback").Body;
        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(body, 12));
        box.SelectAll();
        var clipboard = new RecordingClipboard
        {
            WriteResult = PlatformClipboardWriteResult.Failed("busy"),
        };
        var window = new Window { Content = box };
        window.Show();
        try
        {
            var eventArgs = PreviewKey(box, Key.C);

            var result = await WpfRichTextClipboardAdapter.HandlePreviewKeyDownAsync(
                eventArgs,
                box,
                body,
                clipboard);

            result.Handled.Should().BeFalse();
            eventArgs.Handled.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public async Task PreviewKeyDown_SuccessfulClipboardWriteHandlesEvent()
    {
        var body = InCanvasRichClipboardPayload.FromPlainText("shared copy").Body;
        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(body, 12));
        box.SelectAll();
        var window = new Window { Content = box };
        window.Show();
        try
        {
            var eventArgs = PreviewKey(box, Key.C);

            var result = await WpfRichTextClipboardAdapter.HandlePreviewKeyDownAsync(
                eventArgs,
                box,
                body,
                new RecordingClipboard());

            result.Handled.Should().BeTrue();
            eventArgs.Handled.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ShapeAndTablePreviewHandlersUseResultDrivenClipboardBridge()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var shapeEditor = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "InCanvasTextEditor.cs"));
        var tableEditor = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "InCanvasTableCellEditor.cs"));

        foreach (var source in new[] { shapeEditor, tableEditor })
        {
            var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
            normalized.Should().Contain("await WpfRichTextClipboardAdapter.HandlePreviewKeyDownAsync(");
            normalized.Should().NotContain("e.Handled = true;\n                _ = await WpfRichTextClipboardAdapter.Try");
        }
    }

    private static KeyEventArgs PreviewKey(RichTextBox box, Key key)
    {
        var source = PresentationSource.FromVisual(box)
            ?? throw new InvalidOperationException("WPF RichTextBox has no presentation source.");
        return new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
    }

    private sealed class RecordingClipboard : IPlatformClipboard
    {
        public PlatformClipboardReadRequest? ReadRequest { get; private set; }

        public PlatformClipboardContent? Written { get; private set; }

        public CancellationToken WriteToken { get; private set; }

        public PlatformClipboardReadResult<PlatformClipboardContent> ReadResult { get; init; } =
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

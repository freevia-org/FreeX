using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Thin WPF adapter for the renderer-neutral in-canvas rich clipboard payload. WPF still
/// publishes RTF/Xaml/text so ordinary RichTextBox and Office interoperability remains intact.
/// </summary>
internal static class WpfRichTextClipboardAdapter
{
    internal static async ValueTask<bool> TryCopyAsync(
        RichTextBox box,
        TextBody? originalBody,
        IPlatformClipboard? clipboard = null,
        CancellationToken cancellationToken = default)
    {
        var payload = CreatePayload(box, originalBody);
        if (payload is null)
            return false;

        var content = BuildClipboardContent(box, payload);
        var result = await PresentationRichTextClipboardWorkflow.WriteAsync(
            clipboard ?? new WpfPlatformClipboard(),
            content,
            PlatformClipboardFormatScope.Platform,
            DataFormats.XamlPackage,
            DataFormats.Rtf,
            cancellationToken);
        return result.IsSuccess;
    }

    internal static InCanvasRichClipboardPayload? CreatePayload(
        RichTextBox box,
        TextBody? originalBody)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (box.Selection.IsEmpty)
            return null;

        var currentBody = TextBodyFlowDocumentConverter.FromFlowDocument(box.Document, originalBody);
        var selection = CurrentSelection(box.Document, box.Selection);
        var payload = InCanvasRichClipboardPlanner.Capture(currentBody, selection);
        return payload.PlainText.Length == 0 ? null : payload;
    }

    internal static async ValueTask<bool> TryCutAsync(
        RichTextBox box,
        TextBody? originalBody,
        IPlatformClipboard? clipboard = null,
        CancellationToken cancellationToken = default)
    {
        if (!await TryCopyAsync(box, originalBody, clipboard, cancellationToken))
            return false;

        box.Selection.Text = string.Empty;
        return true;
    }

    internal static async ValueTask<WpfRichTextClipboardPasteResult> TryPasteAsync(
        RichTextBox box,
        TextBody? originalBody,
        IPlatformClipboard? clipboard = null,
        CancellationToken cancellationToken = default)
    {
        var result = await PresentationRichTextClipboardWorkflow.ReadAsync(
            clipboard ?? new WpfPlatformClipboard(),
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
            return default;

        return TryPasteContent(box, originalBody, result.Value, out var updatedBody)
            ? new WpfRichTextClipboardPasteResult(true, updatedBody)
            : default;
    }

    internal static bool TryPasteDataObject(
        RichTextBox box,
        TextBody? originalBody,
        IDataObject? data,
        out TextBody? updatedBody)
    {
        var read = WpfPlatformClipboard.ReadDataObject(
            data,
            PresentationClipboardPlatformMapper.RichTextReadRequest);
        var content = read.IsSuccess && read.Value is not null
            ? PresentationClipboardPlatformMapper.FromPlatformContent(read.Value)
            : new PresentationClipboardContent();
        return TryPasteContent(box, originalBody, content, out updatedBody);
    }

    private static bool TryPasteContent(
        RichTextBox box,
        TextBody? originalBody,
        PresentationClipboardContent content,
        out TextBody? updatedBody)
    {
        updatedBody = null;

        var payload = InCanvasRichClipboardFormatResolver.Resolve(content).Payload;
        if (payload is null)
            return false;

        var currentBody = TextBodyFlowDocumentConverter.FromFlowDocument(box.Document, originalBody);
        var selection = CurrentSelection(box.Document, box.Selection);
        var body = InCanvasRichClipboardPlanner.Apply(
            currentBody,
            selection,
            payload,
            out var caret);
        var fallbackPt = InCanvasRichTextEditorDefaults.ResolveFallbackFontSize(
            body,
            InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt);
        box.Document = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackPt);
        SelectLogicalRange(box, caret, caret);
        updatedBody = body;
        return true;
    }

    internal static DataObject BuildDataObject(
        RichTextBox box,
        InCanvasRichClipboardPayload payload)
    {
        var content = BuildClipboardContent(box, payload);
        return WpfPlatformClipboard.BuildDataObject(
            PresentationClipboardPlatformMapper.ToPlatformContent(
                content,
                PlatformClipboardFormatScope.Platform,
                DataFormats.XamlPackage,
                DataFormats.Rtf));
    }

    private static PresentationClipboardContent BuildClipboardContent(
        RichTextBox box,
        InCanvasRichClipboardPayload payload)
    {
        var range = new TextRange(box.Selection.Start, box.Selection.End);
        return PresentationRichTextClipboardWorkflow.CreateWriteContent(
            payload,
            TrySaveNativeFormat(range, DataFormats.XamlPackage),
            TrySaveNativeFormat(range, DataFormats.Rtf));
    }

    private static byte[]? TrySaveNativeFormat(TextRange range, string format)
    {
        try
        {
            using var stream = new MemoryStream();
            range.Save(stream, format);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static InCanvasEditorTextSelection CurrentSelection(
        FlowDocument document,
        TextSelection selection) =>
        new(
            LogicalOffsetAt(document, selection.Start),
            LogicalOffsetAt(document, selection.End));

    private static int LogicalOffsetAt(FlowDocument document, TextPointer position) =>
        TextBodyFlowDocumentConverter.LogicalOffsetAt(document, position);

    private static void SelectLogicalRange(RichTextBox box, int start, int end)
    {
        var startPointer = TextPointerAtLogicalOffset(box.Document, start);
        var endPointer = TextPointerAtLogicalOffset(box.Document, end);
        if (startPointer is not null && endPointer is not null)
        {
            box.Selection.Select(startPointer, endPointer);
            box.Focus();
        }
    }

    private static TextPointer? TextPointerAtLogicalOffset(
        FlowDocument document,
        int logicalOffset)
    {
        int remaining = Math.Max(0, logicalOffset);
        bool firstParagraph = true;
        foreach (var paragraph in document.Blocks.OfType<System.Windows.Documents.Paragraph>())
        {
            if (!firstParagraph)
            {
                if (remaining == 0)
                    return paragraph.ContentStart;
                remaining--;
            }
            firstParagraph = false;

            foreach (var inline in TextBodyFlowDocumentConverter.EnumerateEditableLeafInlines(paragraph.Inlines))
            {
                if (inline is System.Windows.Documents.Run run)
                {
                    int length = run.Text?.Length ?? 0;
                    if (remaining <= length)
                        return run.ContentStart.GetPositionAtOffset(remaining) ?? run.ContentEnd;
                    remaining -= length;
                }
                else if (inline is LineBreak)
                {
                    if (remaining <= 1)
                        return inline.ContentStart;
                    remaining--;
                }
                else if (inline is InlineUIContainer)
                {
                    if (remaining <= 1)
                        return inline.ContentStart;
                    remaining--;
                }
            }
        }

        return remaining == 0 ? document.ContentEnd : null;
    }
}

internal readonly record struct WpfRichTextClipboardPasteResult(
    bool Applied,
    TextBody? UpdatedBody);

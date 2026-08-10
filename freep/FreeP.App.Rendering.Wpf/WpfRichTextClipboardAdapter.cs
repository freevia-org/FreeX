using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Thin WPF adapter for the renderer-neutral in-canvas rich clipboard payload. WPF still
/// publishes RTF/Xaml/text so ordinary RichTextBox and Office interoperability remains intact.
/// </summary>
internal static class WpfRichTextClipboardAdapter
{
    /// <summary>
    /// Test seam: when set, replaces the real <see cref="Clipboard.SetDataObject(DataObject, bool)"/>
    /// call so tests can force an in-place Copy/Cut write failure deterministically without locking
    /// the process's actual OS clipboard. Null (default) uses the real WPF clipboard.
    /// </summary>
    internal static Action<DataObject>? SetDataObjectForTests { get; set; }

    internal static bool TryCopy(RichTextBox box, TextBody? originalBody) =>
        TryCopy(box, originalBody, out _);

    /// <param name="errorMessage">
    /// Set to the OS-clipboard write failure's message when this returns false because the write
    /// itself failed (locked by another process, unsupported format, etc.). Null when there was
    /// nothing to copy (not a failure) or the copy succeeded, so callers can distinguish a real
    /// failure worth surfacing to the user from an ordinary empty-selection no-op.
    /// </param>
    internal static bool TryCopy(RichTextBox box, TextBody? originalBody, out string? errorMessage)
    {
        errorMessage = null;
        var payload = CreatePayload(box, originalBody);
        if (payload is null)
            return false;

        try
        {
            var data = BuildDataObject(box, payload);
            if (SetDataObjectForTests is { } testWrite)
                testWrite(data);
            else
                Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (Exception ex)
        {
            // The OS clipboard write failed (locked by another process, unsupported format, etc.).
            // Record the message so the caller can surface it instead of the user believing the
            // in-place copy succeeded and later pasting stale data.
            errorMessage = ex.Message;
            return false;
        }
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

    internal static bool TryCut(RichTextBox box, TextBody? originalBody) =>
        TryCut(box, originalBody, out _);

    /// <param name="errorMessage">See <see cref="TryCopy(RichTextBox, TextBody?, out string?)"/>.</param>
    internal static bool TryCut(RichTextBox box, TextBody? originalBody, out string? errorMessage)
    {
        if (!TryCopy(box, originalBody, out errorMessage))
            return false;

        box.Selection.Text = string.Empty;
        return true;
    }

    internal static bool TryPaste(
        RichTextBox box,
        TextBody? originalBody,
        out TextBody? updatedBody)
    {
        try
        {
            return TryPasteDataObject(box, originalBody, Clipboard.GetDataObject(), out updatedBody);
        }
        catch
        {
            updatedBody = null;
            return false;
        }
    }

    internal static bool TryPasteDataObject(
        RichTextBox box,
        TextBody? originalBody,
        IDataObject? data,
        out TextBody? updatedBody)
    {
        updatedBody = null;
        byte[]? bytes;
        try
        {
            bytes = ReadBytes(data, PresentationClipboardFormats.RichText);
        }
        catch
        {
            return false;
        }

        var payload = InCanvasRichClipboardPlanner.Deserialize(bytes);
        if (payload is null)
        {
            payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
                ReadBytes(data, DataFormats.XamlPackage));
        }

        if (payload is null)
        {
            // WPF's native RTF loader is authoritative for the control itself. FreeP's
            // TextBody has no inline table node, however, so use the shared bounded planner
            // before plain-text fallback to preserve the same logical tab/row projection in
            // both hosts without making the shared parser a platform fork.
            try
            {
                var externalPayload = ExternalRichTextClipboardPlanner.TryParseRtf(
                    ReadBytes(data, DataFormats.Rtf));
                payload = externalPayload ?? ReadPlainTextPayload(data);
            }
            catch
            {
                return false;
            }
        }

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
        var data = new DataObject();
        var range = new TextRange(box.Selection.Start, box.Selection.End);

        TrySetNativeFormat(data, range, DataFormats.Rtf);
        TrySetNativeFormat(data, range, DataFormats.XamlPackage);
        data.SetText(payload.PlainText, TextDataFormat.UnicodeText);
        data.SetData(
            PresentationClipboardFormats.RichText,
            new MemoryStream(InCanvasRichClipboardPlanner.Serialize(payload)),
            autoConvert: false);
        return data;
    }

    private static void TrySetNativeFormat(DataObject data, TextRange range, string format)
    {
        try
        {
            using var stream = new MemoryStream();
            range.Save(stream, format);
            data.SetData(format, new MemoryStream(stream.ToArray(), writable: false), autoConvert: false);
        }
        catch
        {
            // Some WPF hosts do not expose every rich format; Unicode text and FreeP's payload
            // remain available in that case.
        }
    }

    private static byte[]? ReadBytes(IDataObject? data, string format)
    {
        if (data is null || !data.GetDataPresent(format, autoConvert: false))
            return null;

        var value = data.GetData(format, autoConvert: false);
        return value switch
        {
            byte[] bytes => bytes,
            MemoryStream stream => stream.ToArray(),
            Stream stream => ReadStream(stream),
            string text => Encoding.Default.GetBytes(text),
            _ => null,
        };
    }

    private static InCanvasRichClipboardPayload? ReadPlainTextPayload(IDataObject? data)
    {
        if (data is null || !data.GetDataPresent(DataFormats.UnicodeText, autoConvert: false))
            return null;

        return data.GetData(DataFormats.UnicodeText, autoConvert: false) is string text
            ? InCanvasRichClipboardPayload.FromPlainText(text)
            : null;
    }

    private static byte[]? ReadStream(Stream stream)
    {
        try
        {
            if (stream.CanSeek)
                stream.Position = 0;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
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

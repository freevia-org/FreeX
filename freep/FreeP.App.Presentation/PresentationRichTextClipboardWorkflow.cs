using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

public static class PresentationRichTextClipboardWorkflow
{
    public static PresentationClipboardContent CreateWriteContent(
        InCanvasRichClipboardPayload payload,
        byte[]? xamlPackageBytes,
        byte[]? rtfBytes)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new PresentationClipboardContent(
            Text: ToExternalPlainText(payload.PlainText),
            RichTextBytes: InCanvasRichClipboardPlanner.Serialize(payload),
            XamlPackageBytes: xamlPackageBytes,
            RtfBytes: rtfBytes);
    }

    /// <summary>
    /// U+FFFC (OBJECT REPLACEMENT CHARACTER) anchors inline objects -- images, tables, OLE -- inside
    /// the internal text so caret and selection offsets line up with the rich body. It must not reach
    /// the external text/plain flavor: the object itself cannot follow, so a plain-text target would
    /// show a stray placeholder glyph where nothing was copied. The private rich format still carries
    /// the anchor and the object with it.
    /// </summary>
    private static string ToExternalPlainText(string text) =>
        string.IsNullOrEmpty(text) ? text : text.Replace("￼", string.Empty, StringComparison.Ordinal);

    public static ValueTask<PlatformClipboardWriteResult> WriteAsync(
        IPlatformClipboard clipboard,
        PresentationClipboardContent content,
        PlatformClipboardFormatScope nativeScope,
        string xamlPackageFormat,
        string rtfFormat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(content);
        return clipboard.WriteAsync(
            PresentationClipboardPlatformMapper.ToPlatformContent(
                content,
                nativeScope,
                xamlPackageFormat,
                rtfFormat),
            cancellationToken);
    }

    public static async ValueTask<PlatformClipboardReadResult<PresentationClipboardContent>> ReadAsync(
        IPlatformClipboard clipboard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        var result = await clipboard.ReadAsync(
            PresentationClipboardPlatformMapper.RichTextReadRequest,
            cancellationToken);
        return result.Status switch
        {
            PlatformClipboardReadStatus.Success when result.Value is not null =>
                PlatformClipboardReadResult<PresentationClipboardContent>.Success(
                    PresentationClipboardPlatformMapper.FromPlatformContent(result.Value)),
            PlatformClipboardReadStatus.Success or PlatformClipboardReadStatus.Empty =>
                PlatformClipboardReadResult<PresentationClipboardContent>.Empty(),
            PlatformClipboardReadStatus.Unavailable =>
                PlatformClipboardReadResult<PresentationClipboardContent>.Unavailable(
                    result.ErrorMessage),
            PlatformClipboardReadStatus.Unsupported =>
                PlatformClipboardReadResult<PresentationClipboardContent>.Unsupported(
                    result.ErrorMessage),
            _ => PlatformClipboardReadResult<PresentationClipboardContent>.Failed(
                result.ErrorMessage),
        };
    }
}

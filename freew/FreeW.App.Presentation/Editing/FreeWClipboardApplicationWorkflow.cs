using System.Text;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public enum FreeWClipboardTransferStatus
{
    Succeeded,
    Empty,
    Unavailable,
    Unsupported,
    Failed,
}

public sealed record FreeWClipboardPayload(
    string? Text,
    TextDocument? RichDocument)
{
    public bool HasText => PasteText.Normalize(Text).Length > 0;

    public bool HasContent => HasText || RichDocument is not null;
}

public sealed record FreeWClipboardTransferResult(
    FreeWClipboardTransferStatus Status,
    FreeWClipboardPayload? Payload = null,
    string? FeedbackMessage = null)
{
    public bool IsSuccess => Status == FreeWClipboardTransferStatus.Succeeded;

    // The existing shells treat an unavailable clipboard as a valid local cut.
    public bool CanCommitCut => IsSuccess || Status == FreeWClipboardTransferStatus.Unavailable;
}

public sealed record FreeWClipboardPastePlan(
    DocumentPasteTextKind TextKind,
    string? Text,
    TextDocument? RichDocument)
{
    public bool PreferRichDocument => RichDocument is not null;
}

/// <summary>
/// Owns renderer-neutral clipboard transfer decisions for the FreeW application shell. Native adapters
/// provide clipboard transport and realize the returned text or document in their editor controls.
/// </summary>
public static class FreeWClipboardApplicationWorkflow
{
    public const string RichTextFormat = "Rich Text Format";
    public const string EmptyClipboardMessage = "Clipboard does not contain text.";
    public const string ClipboardUnavailableMessage = "The clipboard is unavailable.";
    public const string ClipboardUnsupportedMessage = "This clipboard operation is not supported.";
    public const string ClipboardFailureMessage = "The clipboard operation failed.";

    // clip-1 (R143): FreeX (and any other HTML-aware source: browsers, LibreOffice Calc, Word) never
    // places "Rich Text Format" on the clipboard for a cell-range copy -- it places plain text plus an
    // HTML table fragment (CF_HTML), under "text/html" cross-platform and/or the Windows "HTML Format"
    // name (mirroring FreeX.App.Avalonia's own MainWindow.ClipboardHtml.cs HtmlClipboardFormat /
    // HtmlWindowsClipboardFormat pair). Reading only RTF meant a FreeX->FreeW rich paste always
    // silently degraded to unformatted text. Request both HTML format names alongside RTF so
    // ReadAsync below can fall back to the HTML payload -- parsed with the SAME HtmlFileAdapter this
    // project already uses for whole-file ".html" import -- whenever no RTF is present. This is the
    // cheaper, correct-for-both-directions fix: teaching FreeW to read HTML reuses the existing
    // AngleSharp-based HtmlFileAdapter table/paragraph reader, whereas making FreeX emit RTF would mean
    // building and maintaining an entire RTF table serializer (font/color/border table + \trowd/\cellx
    // layout) in FreeX purely to satisfy FreeW, when FreeX's CF_HTML export already carries the same
    // formatting (bold/fill/alignment/borders/merges) that a from-Word RTF paste would.
    private static readonly PlatformClipboardFormat RichTextClipboardFormat = new(
        RichTextFormat,
        PlatformClipboardDataKind.Text);

    private const string HtmlFormat = "text/html";
    private const string HtmlWindowsFormat = "HTML Format";

    private static readonly PlatformClipboardFormat HtmlClipboardFormat = new(
        HtmlFormat,
        PlatformClipboardDataKind.Text);

    private static readonly PlatformClipboardFormat HtmlWindowsClipboardFormat = new(
        HtmlWindowsFormat,
        PlatformClipboardDataKind.Text);

    public static PlatformClipboardReadRequest PasteSpecialReadRequest { get; } = new(
        IncludeText: true,
        CustomFormats: [RichTextClipboardFormat, HtmlClipboardFormat, HtmlWindowsClipboardFormat]);

    public static PlatformClipboardContent? CreateWriteContent(string? selectedText) =>
        string.IsNullOrEmpty(selectedText)
            ? null
            : new PlatformClipboardContent(Text: selectedText);

    public static async ValueTask<FreeWClipboardTransferResult> WriteSelectionAsync(
        IPlatformClipboard clipboard,
        string? selectedText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (CreateWriteContent(selectedText) is not { } content)
            return Empty();

        var result = await clipboard.WriteAsync(content, cancellationToken);
        return result.Status switch
        {
            PlatformClipboardWriteStatus.Success => Succeeded(),
            PlatformClipboardWriteStatus.Unavailable =>
                new(FreeWClipboardTransferStatus.Unavailable),
            PlatformClipboardWriteStatus.Unsupported =>
                Failed(FreeWClipboardTransferStatus.Unsupported, result.ErrorMessage, ClipboardUnsupportedMessage),
            _ => Failed(FreeWClipboardTransferStatus.Failed, result.ErrorMessage, ClipboardFailureMessage),
        };
    }

    public static ValueTask<FreeWClipboardTransferResult> ReadTextAsync(
        IPlatformClipboard clipboard,
        CancellationToken cancellationToken = default) =>
        ReadAsync(clipboard, PlatformClipboardReadRequest.Text, includeRichDocument: false, cancellationToken);

    public static ValueTask<FreeWClipboardTransferResult> ReadPasteSpecialAsync(
        IPlatformClipboard clipboard,
        CancellationToken cancellationToken = default) =>
        ReadAsync(clipboard, PasteSpecialReadRequest, includeRichDocument: true, cancellationToken);

    public static FreeWClipboardPastePlan PlanPaste(
        FreeWClipboardPayload payload,
        PasteSpecialOption option)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return option switch
        {
            PasteSpecialOption.KeepTextOnly =>
                new(DocumentPasteTextKind.TextOnly, payload.Text, RichDocument: null),
            PasteSpecialOption.KeepSourceFormatting when payload.RichDocument is not null =>
                new(DocumentPasteTextKind.MergeFormatting, payload.Text, payload.RichDocument),
            _ => new(DocumentPasteTextKind.MergeFormatting, payload.Text, RichDocument: null),
        };
    }

    private static async ValueTask<FreeWClipboardTransferResult> ReadAsync(
        IPlatformClipboard clipboard,
        PlatformClipboardReadRequest request,
        bool includeRichDocument,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        var result = await clipboard.ReadAsync(request, cancellationToken);
        if (result.Status != PlatformClipboardReadStatus.Success || result.Value is null)
        {
            return result.Status switch
            {
                PlatformClipboardReadStatus.Unavailable =>
                    Failed(FreeWClipboardTransferStatus.Unavailable, result.ErrorMessage, ClipboardUnavailableMessage),
                PlatformClipboardReadStatus.Unsupported =>
                    Failed(FreeWClipboardTransferStatus.Unsupported, result.ErrorMessage, ClipboardUnsupportedMessage),
                PlatformClipboardReadStatus.Failed =>
                    Failed(FreeWClipboardTransferStatus.Failed, result.ErrorMessage, ClipboardFailureMessage),
                _ => Empty(),
            };
        }

        TextDocument? richDocument = null;
        if (includeRichDocument)
        {
            var rtf = result.Value.GetText(RichTextFormat);
            if (RtfClipboardDocumentParser.TryParse(rtf, out var parsed))
                richDocument = parsed;

            // clip-1 (R143): no RTF on the clipboard (FreeX and other HTML-only sources never write
            // it) -- fall back to whichever HTML format is present.
            if (richDocument is null)
            {
                var html = result.Value.GetText(HtmlFormat) ?? result.Value.GetText(HtmlWindowsFormat);
                if (TryParseHtmlDocument(html, out var parsedHtml))
                    richDocument = parsedHtml;
            }
        }

        var payload = new FreeWClipboardPayload(result.Value.Text, richDocument);
        return payload.HasContent ? Succeeded(payload) : Empty();
    }

    /// <summary>
    /// Parses an HTML clipboard payload into a <see cref="TextDocument"/> via the same
    /// <see cref="HtmlFileAdapter"/> this project already uses for ".html" file import (AngleSharp
    /// under the hood, table/paragraph/style aware). <paramref name="html"/> may be a bare fragment
    /// (as FreeX's Avalonia shell writes under "text/html") or a full CF_HTML payload -- a plain-text
    /// header (<c>Version:0.9\r\nStartHTML:...</c>) followed by an <c>&lt;html&gt;...&lt;/html&gt;</c>
    /// wrapper around <c>&lt;!--StartFragment--&gt;...&lt;!--EndFragment--&gt;</c> (as the WPF host's
    /// "HTML Format" and FreeX's own <c>ClipboardHtmlSerializer.WrapAsCfHtml</c> write). The header is
    /// not valid HTML, so it is stripped down to the first <c>&lt;html</c> tag before parsing --
    /// otherwise AngleSharp's lenient parser would fold the raw header text into the document as a
    /// spurious leading paragraph. The StartFragment/EndFragment markers themselves are ordinary HTML
    /// comments and need no special handling; the parser simply skips them.
    /// </summary>
    private static bool TryParseHtmlDocument(string? html, out TextDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(html))
            return false;

        var htmlStart = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        var markup = htmlStart > 0 ? html[htmlStart..] : html;

        TextDocument parsed;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markup));
            parsed = new HtmlFileAdapter().Load(stream);
        }
        catch
        {
            return false;
        }

        if (parsed.Blocks.Count == 0)
            return false;

        document = parsed;
        return true;
    }

    private static FreeWClipboardTransferResult Succeeded(FreeWClipboardPayload? payload = null) =>
        new(FreeWClipboardTransferStatus.Succeeded, payload);

    private static FreeWClipboardTransferResult Empty() =>
        new(FreeWClipboardTransferStatus.Empty, FeedbackMessage: EmptyClipboardMessage);

    private static FreeWClipboardTransferResult Failed(
        FreeWClipboardTransferStatus status,
        string? detail,
        string fallback) =>
        new(
            status,
            FeedbackMessage: string.IsNullOrWhiteSpace(detail)
                ? fallback
                : $"{fallback} {detail}");
}

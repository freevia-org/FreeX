using System.Text;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
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
        CreateWriteContent(selectedText, richDocument: null);

    // shell-clipboard F2: a native rich-text control (WPF's RichTextBox, which the WPF shell's
    // Copy/Cut fall through to natively -- see DocumentView.cs's "freew-cc-5" comment) places RTF
    // and an HTML/Xaml payload on the clipboard alongside plain text automatically. The Avalonia
    // shell's editor has no such native control, so its Copy/Cut must build that rich payload
    // itself or every Copy+Paste round trip -- even within the same document -- silently drops all
    // character formatting. <paramref name="richDocument"/> is a (typically small, selection-only)
    // document a caller builds via <see cref="BuildSelectionRichDocument"/>; serializing it to HTML
    // reuses the same <see cref="HtmlFileAdapter"/> this class already reads HTML clipboard payloads
    // with (see clip-1 above), so the format this method WRITES is exactly the format ReadAsync
    // below already knows how to read back -- including from FreeW itself.
    public static PlatformClipboardContent? CreateWriteContent(string? selectedText, TextDocument? richDocument)
    {
        if (string.IsNullOrEmpty(selectedText))
            return null;

        List<PlatformClipboardData>? customData = null;
        if (richDocument is not null && TryRenderHtml(richDocument) is { } html)
        {
            customData =
            [
                PlatformClipboardData.FromText(HtmlFormat, html),
                PlatformClipboardData.FromText(HtmlWindowsFormat, html),
            ];
        }

        return new PlatformClipboardContent(Text: selectedText, CustomData: customData);
    }

    /// <summary>
    /// Builds a small standalone <see cref="TextDocument"/> covering only <paramref name="ranges"/>
    /// (as resolved by a renderer, e.g. <c>DocumentView.GetSelectionRichSnapshot</c>), with each
    /// run's character formatting fully resolved through <paramref name="source"/>'s default-run and
    /// paragraph/character style cascade (<see cref="DocumentRunFormattingResolver"/>) into direct
    /// formatting on the copied run. Flattening the cascade this way means the returned document
    /// renders correctly through <see cref="HtmlFileAdapter"/> standalone, without needing to carry
    /// a copy of <paramref name="source"/>'s style dictionary. Returns null when the ranges contain
    /// no actual run content (e.g. an empty or collapsed selection).
    /// </summary>
    public static TextDocument? BuildSelectionRichDocument(
        TextDocument source,
        IReadOnlyList<DocumentFormattingTextRange>? ranges)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ranges is null || ranges.Count == 0)
            return null;

        var document = new TextDocument();
        var wroteAnyRun = false;
        foreach (var range in ranges)
        {
            var paragraph = range.Paragraph;
            var textLength = paragraph.PlainText.Length;
            var start = Math.Clamp(Math.Min(range.StartOffset, range.EndOffset), 0, textLength);
            var end = Math.Clamp(Math.Max(range.StartOffset, range.EndOffset), 0, textLength);

            var sliced = new Paragraph { Formatting = paragraph.Formatting };
            var position = 0;
            foreach (var run in paragraph.Runs)
            {
                var runStart = position;
                var runText = run.Text;
                position = runStart + runText.Length;

                var overlapStart = Math.Max(start, runStart);
                var overlapEnd = Math.Min(end, position);
                if (overlapEnd <= overlapStart)
                    continue;

                var sliceText = runText.Substring(overlapStart - runStart, overlapEnd - overlapStart);
                if (sliceText.Length == 0)
                    continue;

                var resolved = DocumentRunFormattingResolver.Resolve(source, paragraph, run);
                sliced.Runs.Add(new Run(sliceText, resolved));
                wroteAnyRun = true;
            }

            if (sliced.Runs.Count > 0)
                document.Blocks.Add(sliced);
        }

        return wroteAnyRun ? document : null;
    }

    private static string? TryRenderHtml(TextDocument richDocument)
    {
        try
        {
            using var stream = new MemoryStream();
            new HtmlFileAdapter().Save(richDocument, stream);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            // A clipboard write must never crash the editor over an HTML-serialization edge case --
            // the plain-text payload written alongside this one is always a safe fallback.
            return null;
        }
    }

    public static async ValueTask<FreeWClipboardTransferResult> WriteSelectionAsync(
        IPlatformClipboard clipboard,
        string? selectedText,
        TextDocument? richDocument = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (CreateWriteContent(selectedText, richDocument) is not { } content)
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

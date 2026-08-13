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

    private static readonly PlatformClipboardFormat RichTextClipboardFormat = new(
        RichTextFormat,
        PlatformClipboardDataKind.Text);

    public static PlatformClipboardReadRequest PasteSpecialReadRequest { get; } = new(
        IncludeText: true,
        CustomFormats: [RichTextClipboardFormat]);

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
        }

        var payload = new FreeWClipboardPayload(result.Value.Text, richDocument);
        return payload.HasContent ? Succeeded(payload) : Empty();
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

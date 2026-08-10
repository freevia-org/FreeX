using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Editing;

public sealed record WorkbookClipboardSnapshot(
    GridRange SourceRange,
    IReadOnlyList<(CellAddress Source, Cell Cell)> Cells,
    IReadOnlyList<(CellAddress Source, PictureCellSnapshot Snapshot)> PictureCells,
    string Text,
    bool IsCut,
    IReadOnlyList<GridRange>? SourceAreas = null,
    string? Marker = null);

public readonly record struct WorkbookClipboardReadObservation(
    bool Available,
    string? Text,
    string? Marker,
    bool ReadFailed);

public sealed record WorkbookClipboardPasteResolution(
    ClipboardPastePlan Plan,
    WorkbookClipboardSnapshot? Snapshot);

/// <summary>
/// Owns the renderer-neutral lifetime and source-selection policy for a copied workbook range.
/// Native clipboard reads/writes and marquee realization remain in the host shells.
/// </summary>
public sealed class WorkbookClipboardSession
{
    public const string MarkerFormatName = "FreeX.InternalClipboard";

    public static PlatformClipboardFormat MarkerFormat { get; } = new(
        MarkerFormatName,
        PlatformClipboardDataKind.Text,
        PlatformClipboardFormatScope.Application);

    public static PlatformClipboardReadRequest PasteReadRequest { get; } = new(
        IncludeText: true,
        CustomFormats: [MarkerFormat]);

    public WorkbookClipboardSnapshot? Content { get; private set; }

    public bool HasContent => Content is not null;

    public WorkbookClipboardSnapshot Capture(WorkbookClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Cells);
        ArgumentNullException.ThrowIfNull(snapshot.PictureCells);
        ArgumentNullException.ThrowIfNull(snapshot.Text);

        var marker = string.IsNullOrWhiteSpace(snapshot.Marker)
            ? CreateMarker()
            : snapshot.Marker;
        Content = new WorkbookClipboardSnapshot(
            snapshot.SourceRange,
            snapshot.Cells.ToArray(),
            snapshot.PictureCells.ToArray(),
            snapshot.Text,
            snapshot.IsCut,
            snapshot.SourceAreas?.ToArray(),
            marker);
        return Content;
    }

    public void Clear() => Content = null;

    public WorkbookClipboardPasteResolution ResolvePaste(WorkbookClipboardReadObservation observation)
    {
        var snapshot = Content;
        if (snapshot is null)
        {
            return new WorkbookClipboardPasteResolution(
                ClipboardPastePlanner.PlanPaste(null, observation.Text, observation.ReadFailed),
                null);
        }

        if (snapshot.Marker is not null &&
            string.Equals(snapshot.Marker, observation.Marker, StringComparison.Ordinal))
        {
            return new WorkbookClipboardPasteResolution(
                ClipboardPastePlan.UseInternalClipboard,
                snapshot);
        }

        var plan = ClipboardPastePlanner.PlanPaste(
            snapshot.Text,
            observation.Text,
            observation.ReadFailed);
        if (plan == ClipboardPastePlan.UseExternalClipboardText)
            Clear();

        return new WorkbookClipboardPasteResolution(
            plan,
            plan == ClipboardPastePlan.UseInternalClipboard ? snapshot : null);
    }

    public void CompletePaste(WorkbookClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.IsCut && Content == snapshot)
            Clear();
    }

    public static bool ShouldPreferExternalImage(string? clipboardText) =>
        string.IsNullOrWhiteSpace(clipboardText);

    public static string CreateMarker() => Guid.NewGuid().ToString("N");

    public static PlatformClipboardContent AttachMarker(
        PlatformClipboardContent content,
        string? marker)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(marker))
            return content;

        var customData = content.CustomData
            .Where(item => item.Format != MarkerFormat)
            .Append(PlatformClipboardData.FromText(
                MarkerFormatName,
                marker,
                PlatformClipboardFormatScope.Application))
            .ToArray();
        return new PlatformClipboardContent(
            content.Text,
            content.FilePaths,
            content.Image,
            customData);
    }

    public static WorkbookClipboardReadObservation Observe(
        PlatformClipboardReadResult<PlatformClipboardContent> result)
    {
        var content = result.Value;
        return result.Status switch
        {
            PlatformClipboardReadStatus.Success => new WorkbookClipboardReadObservation(
                Available: true,
                content?.Text,
                content?.GetText(MarkerFormatName, PlatformClipboardFormatScope.Application),
                ReadFailed: false),
            PlatformClipboardReadStatus.Empty => new WorkbookClipboardReadObservation(
                Available: true,
                Text: null,
                Marker: null,
                ReadFailed: false),
            PlatformClipboardReadStatus.Unavailable => new WorkbookClipboardReadObservation(
                Available: false,
                Text: null,
                Marker: null,
                ReadFailed: false),
            _ => new WorkbookClipboardReadObservation(
                Available: true,
                Text: null,
                Marker: null,
                ReadFailed: true),
        };
    }
}

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

    /// <summary>
    /// R143-remediation (clip-2-regression): opaque token identifying whoever last <see
    /// cref="Capture"/>d the current <see cref="Content"/>. Set alongside <see cref="Content"/>
    /// and cleared alongside it, so it is always meaningful only while <see cref="HasContent"/>
    /// is true. This session became a DI-wide singleton shared by every open window in the
    /// process (see App.xaml.cs), so a caller that wants to clear this session as a side effect
    /// of a purely LOCAL, no-clipboard-intent gesture (Escape, Delete, Backspace, committing an
    /// unrelated cell edit) must first check <see cref="IsOwnedBy"/>/use <see
    /// cref="ClearIfOwnedBy"/> instead of the unconditional <see cref="Clear"/> -- otherwise that
    /// gesture in window B silently destroys content window A copied and is still showing
    /// marching ants around. A genuine new Copy (in any window) is a real "the clipboard changed"
    /// event and should keep overwriting <see cref="Content"/>/<see cref="Owner"/> unconditionally
    /// via <see cref="Capture"/>, exactly like the real OS clipboard.
    /// </summary>
    public object? Owner { get; private set; }

    /// <summary>True when this session currently has content AND it was captured by <paramref name="owner"/>.</summary>
    public bool IsOwnedBy(object? owner) =>
        Content is not null && owner is not null && ReferenceEquals(Owner, owner);

    /// <summary>
    /// Clears <see cref="Content"/>/<see cref="Owner"/> only if <paramref name="owner"/> is the
    /// one who captured the current content -- a no-op (and safe to call unconditionally) for a
    /// window that never owned it, including one that owns nothing right now. See <see
    /// cref="Owner"/> for why this exists.
    /// </summary>
    public void ClearIfOwnedBy(object? owner)
    {
        if (IsOwnedBy(owner))
            Clear();
    }

    public WorkbookClipboardSnapshot Capture(WorkbookClipboardSnapshot snapshot, object? owner = null)
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
        Owner = owner;
        return Content;
    }

    public void Clear()
    {
        Content = null;
        Owner = null;
    }

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

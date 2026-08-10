using System;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Editing;

/// <summary>
/// The outcome of <see cref="ClipboardPastePlanner.PlanPaste"/>: which clipboard source (if any)
/// a paste gesture should draw from.
/// </summary>
public enum ClipboardPastePlan
{
    /// <summary>Paste using the captured internal clipboard (formula-adjusted, styled, etc.).</summary>
    UseInternalClipboard,

    /// <summary>Paste using the freshly read external/OS clipboard text.</summary>
    UseExternalClipboardText,

    /// <summary>
    /// The OS clipboard could not be read (transient failure). The caller must not guess — skip the
    /// paste and surface a status message instead of falling back to a possibly-stale clipboard.
    /// </summary>
    ReadFailed
}

public static class ClipboardPastePlanner
{
    public static PasteCellsMode ToCorePasteMode(PasteMode mode) =>
        mode switch
        {
            PasteMode.All => PasteCellsMode.All,
            PasteMode.Values => PasteCellsMode.Values,
            PasteMode.Formulas => PasteCellsMode.Formulas,
            PasteMode.Formats => PasteCellsMode.Formats,
            _ => PasteCellsMode.All
        };

    public static bool ShouldUseInternalClipboard(string internalClipboardText, string? currentClipboardText) =>
        currentClipboardText is null ||
        string.Equals(internalClipboardText, currentClipboardText, StringComparison.Ordinal);

    /// <summary>
    /// Decides whether a paste gesture should fall back to the captured internal clipboard, a real
    /// external clipboard read, or bail out entirely because the OS clipboard could not be read.
    /// </summary>
    /// <param name="internalClipboardText">The serialized text captured at internal-copy time.</param>
    /// <param name="currentClipboardText">
    /// The text just read from the OS clipboard, or <c>null</c> if the read produced no text
    /// (clipboard empty/non-text, or the read failed).
    /// </param>
    /// <param name="clipboardReadFailed">
    /// <c>true</c> when the OS clipboard read itself threw/failed (e.g. another process transiently
    /// holds the clipboard open) rather than succeeding with no text. Callers that cannot distinguish
    /// a failed read from a successful-but-empty one should treat the read as failed only when they
    /// actually caught an exception — otherwise pass <c>false</c>, which reproduces the historical
    /// "no text means unchanged" fallback via <see cref="ShouldUseInternalClipboard(string, string?)"/>.
    /// </param>
    public static ClipboardPastePlan PlanPaste(
        string? internalClipboardText,
        string? currentClipboardText,
        bool clipboardReadFailed)
    {
        if (clipboardReadFailed)
        {
            // A transient read failure must never be silently reinterpreted as "clipboard unchanged" —
            // that would risk pasting a stale internal copy over content the user just copied elsewhere.
            // Surface the failure so the caller can skip the paste and tell the user, instead of guessing.
            return ClipboardPastePlan.ReadFailed;
        }

        if (internalClipboardText is not null &&
            ShouldUseInternalClipboard(internalClipboardText, currentClipboardText))
        {
            return ClipboardPastePlan.UseInternalClipboard;
        }

        return ClipboardPastePlan.UseExternalClipboardText;
    }

    public static bool ShouldPasteClipboardImageForNormalPaste(PasteMode mode, string? clipboardText, bool hasImage) =>
        mode == PasteMode.All &&
        hasImage &&
        WorkbookClipboardSession.ShouldPreferExternalImage(clipboardText);

    public static bool ShouldPreserveClipboardVisualAfterPaste(bool isCut) => !isCut;

    // An arithmetic Operation (Add/Subtract/Multiply/Divide) must still tile across a larger
    // selected destination just like a plain paste — Excel applies the operation cell-by-cell
    // to every destination cell, tiling the (possibly 1-cell) clipboard source across the whole
    // selection, not just the anchor cell (R16-paste-special-matrix-1). The same is true for
    // "All merging conditional formats": Core.Commands' PasteCommandFactory tiles its copied
    // values/formats exactly like every other Paste Special content kind
    // (R25-clipboard-paste-remaining-2) — the caller must expand the destination for this content
    // kind too, or that tiling code path is unreachable from the real paste flow (R99-clipboard-
    // paste-merge-cf-tile).
    public static bool ShouldFillSelectedDestinationRange(bool isCut, PasteSpecialOptions options) =>
        !isCut;

    public static bool ShouldClearCutSourceAfterPaste(
        bool isCut,
        GridRange sourceRange,
        GridRange targetRange,
        PasteMode mode,
        PasteSpecialOptions options,
        bool keepColumnWidths)
    {
        if (!isCut || mode == PasteMode.Formats || keepColumnWidths)
            return false;

        var pastedRange = CreatePastedRange(sourceRange, targetRange.Start, options.Transpose);

        return !sourceRange.Overlaps(pastedRange);
    }

    private static GridRange CreatePastedRange(GridRange sourceRange, CellAddress targetStart, bool transpose)
    {
        var pastedRows = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var pastedCols = transpose ? sourceRange.RowCount : sourceRange.ColCount;
        return new GridRange(
            targetStart,
            new CellAddress(
                targetStart.Sheet,
                targetStart.Row + pastedRows - 1,
                targetStart.Col + pastedCols - 1));
    }
}

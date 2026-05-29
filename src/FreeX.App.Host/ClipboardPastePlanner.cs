using System;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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

    public static bool ShouldPasteClipboardImageForNormalPaste(PasteMode mode, string? clipboardText, bool hasImage) =>
        mode == PasteMode.All &&
        hasImage &&
        string.IsNullOrWhiteSpace(clipboardText);

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

public enum PasteMode
{
    All,
    Values,
    Formulas,
    Formats
}

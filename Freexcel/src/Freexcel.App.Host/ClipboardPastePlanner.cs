using System;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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

        var pastedRows = options.Transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var pastedCols = options.Transpose ? sourceRange.RowCount : sourceRange.ColCount;
        var pastedRange = new GridRange(
            targetRange.Start,
            new CellAddress(
                targetRange.Start.Sheet,
                targetRange.Start.Row + pastedRows - 1,
                targetRange.Start.Col + pastedCols - 1));

        return !sourceRange.Overlaps(pastedRange);
    }
}

public enum PasteMode
{
    All,
    Values,
    Formulas,
    Formats
}

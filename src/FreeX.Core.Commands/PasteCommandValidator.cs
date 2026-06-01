using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class PasteCommandValidator
{
    public static string? ValidateInternalPaste(
        SheetId targetSheetId,
        GridRange sourceRange,
        IEnumerable<CellAddress> sourceAddresses,
        CellAddress destination,
        bool transpose)
    {
        if (destination.Sheet != targetSheetId)
            return "Paste destination must be on the target sheet.";

        if (!WorksheetBounds.IsValidAddress(sourceRange.Start) ||
            !WorksheetBounds.IsValidAddress(sourceRange.End) ||
            !WorksheetBounds.IsValidAddress(destination))
        {
            return "Paste range is outside the worksheet bounds.";
        }

        var rowCount = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var colCount = transpose ? sourceRange.RowCount : sourceRange.ColCount;
        if (!WorksheetBounds.TryGetRectangleEnd(destination, rowCount, colCount, out _))
            return "Paste destination range is outside the worksheet bounds.";

        var seenSources = new HashSet<CellAddress>();
        foreach (var sourceAddress in sourceAddresses)
        {
            if (!sourceRange.Contains(sourceAddress))
                return "Paste source cells must be inside the source range.";

            if (!seenSources.Add(sourceAddress))
                return "Paste source cells map to duplicate destination cells.";
        }

        return null;
    }
}

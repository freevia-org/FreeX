using FreeX.Core.Model;

namespace FreeX.App.Presentation.Consolidate;

/// <summary>
/// Deterministic state used only by the cross-platform Consolidate visual-evidence lanes.
/// Production openers still derive their initial values from the user's current selection.
/// </summary>
public static class ConsolidateParityFixture
{
    public const string SourceReference = "A1:C4";
    public const string DestinationReference = "H2";

    public static ConsolidateDialogInitialState CreateDialogInitialState() =>
        new(SourceReference, DestinationReference);

    public static GridRange CreateSourceRange(SheetId sheetId) =>
        new(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 3));

    public static CellAddress CreateDestinationCell(SheetId sheetId) =>
        new(sheetId, 2, 8);
}

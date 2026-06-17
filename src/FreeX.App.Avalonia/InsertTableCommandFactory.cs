using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// UI-free factory that turns a sheet selection into the Core <see cref="CreateStructuredTableCommand"/>
/// the shell executes to create (or format-as) a structured table. Kept portable (no Avalonia types) so
/// the resolved range and header flag are unit testable without a running shell. Header detection is read
/// from the same <see cref="QuickAnalysisSelectionReader"/> heuristic the rest of the shell uses, so the
/// menu "Insert Table" and (future) Quick Analysis Tables paths agree on whether the first row is a
/// header. The Avalonia grid already paints structured-table styling from the model on the next refresh.
/// </summary>
public static class InsertTableCommandFactory
{
    /// <summary>
    /// Builds a <see cref="CreateStructuredTableCommand"/> over <paramref name="selection"/> on
    /// <paramref name="sheetId"/>. When <paramref name="firstRowHasHeaders"/> is true the first selected
    /// row supplies the column names; otherwise Core generates <c>Column1…ColumnN</c> headers. No style name
    /// is set, matching the shell's plain "Insert Table" path.
    /// </summary>
    public static CreateStructuredTableCommand Build(
        SheetId sheetId, GridRange selection, bool firstRowHasHeaders) =>
        new(sheetId, selection, styleName: null, firstRowHasHeaders: firstRowHasHeaders);
}

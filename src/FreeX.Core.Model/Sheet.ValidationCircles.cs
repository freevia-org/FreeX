namespace FreeX.Core.Model;

public sealed partial class Sheet
{
    /// <summary>
    /// Cells currently flagged by Data &gt; Data Validation &gt; Circle Invalid Data, or
    /// <see langword="null"/> when no circles are showing. Real Excel keeps these as worksheet
    /// annotations that appear in Print Preview and printed/PDF output, not just on screen; this
    /// property is the sheet/session-level store a print renderer can read directly from
    /// <c>workbook.GetSheet(sheetId).ValidationCircleCells</c> (unlike a shell-side
    /// DependencyProperty, which only the interactive grid instance can see) so print/PDF/XPS
    /// output draws the same circles the user sees live.
    /// Transient session state only: never written to or read from the XLSX/native file formats
    /// (Excel does not persist Circle Invalid Data across a save/reopen either -- it is always a
    /// fresh re-check), and deliberately NOT copied by <see cref="Clone(SheetId, string)"/> -- a duplicated
    /// sheet starts with no circles until Circle Invalid Data is re-run against it.
    /// </summary>
    public IReadOnlyList<CellAddress>? ValidationCircleCells { get; set; }
}

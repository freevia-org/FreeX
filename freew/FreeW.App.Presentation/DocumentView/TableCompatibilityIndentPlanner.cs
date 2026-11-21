using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Word positions a left-aligned table differently below compatibility mode 15: in Word 2013+ the table's left
/// border sits at <c>w:tblInd</c>, while in earlier modes <c>w:tblInd</c> locates the first cell's TEXT edge, so
/// the border is outdented by that cell's left padding.
/// </summary>
public static class TableCompatibilityIndentPlanner
{
    /// <summary>
    /// How far (points) the table's left border moves left of the indent position: the first cell's left padding
    /// for a left-aligned table in a pre-2013 compatibility mode, otherwise zero.
    /// </summary>
    public static double ResolveLeftOutdentPt(bool usesLegacyCompatibilityLayout, Table table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (!usesLegacyCompatibilityLayout || table.Alignment != TableAlignment.Left)
            return 0;

        var firstCell = table.Rows.Count > 0 && table.Rows[0].Cells.Count > 0 ? table.Rows[0].Cells[0] : null;
        var margins = firstCell?.Margins ?? table.DefaultCellMargins ?? TableCellMargins.Default;
        return Math.Max(0, margins.LeftPt);
    }
}

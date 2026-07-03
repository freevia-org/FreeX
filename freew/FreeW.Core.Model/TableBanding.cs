namespace FreeW.Core.Model;

/// <summary>
/// Shared Word table banding helpers used by DOCX IO and both renderers.
/// </summary>
public static class TableBanding
{
    public static int BodyRowIndex(int rowIndex, bool hasHeaderRow) =>
        hasHeaderRow ? rowIndex - 1 : rowIndex;

    public static bool IsBandedBodyRow(int rowIndex, bool hasHeaderRow)
    {
        var bodyIndex = BodyRowIndex(rowIndex, hasHeaderRow);
        return bodyIndex >= 0 && bodyIndex % 2 == 0;
    }
}

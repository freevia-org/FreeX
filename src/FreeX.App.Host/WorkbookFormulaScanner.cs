using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class WorkbookFormulaScanner
{
    public static bool HasFormulas(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.HasFormulas)
                return true;
        }

        return false;
    }
}

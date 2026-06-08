using FreeX.Core.Model;

namespace FreeX.App.Services;

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

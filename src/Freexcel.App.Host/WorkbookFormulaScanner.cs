using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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

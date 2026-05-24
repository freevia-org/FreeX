using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class WorkbookFormulaScanner
{
    public static bool HasFormulas(Workbook workbook) =>
        workbook.Sheets.Any(sheet => sheet.EnumerateCells().Any(entry => entry.Cell.HasFormula));
}

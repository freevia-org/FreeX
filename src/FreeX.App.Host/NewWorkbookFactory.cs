using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class NewWorkbookFactory
{
    public static Workbook Create(int defaultSheetCount)
    {
        var workbook = new Workbook("Book1");
        var sheetCount = FreeXOptions.NormalizeDefaultSheetCount(defaultSheetCount);
        for (var sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
            workbook.AddSheet($"Sheet{sheetIndex}");

        return workbook;
    }
}

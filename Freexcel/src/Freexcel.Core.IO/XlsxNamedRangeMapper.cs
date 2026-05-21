using ClosedXML.Excel;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxNamedRangeMapper
{
    public static void Load(XLWorkbook xlWorkbook, Workbook workbook)
    {
        foreach (var namedRange in xlWorkbook.DefinedNames)
        {
            try
            {
                var xlRange = namedRange.Ranges.FirstOrDefault();
                if (xlRange is null)
                    continue;

                var firstCell = xlRange.FirstCell();
                var lastCell = xlRange.LastCell();
                var sheet = workbook.GetSheet(firstCell.Worksheet.Name);
                if (sheet is null)
                    continue;

                var start = new CellAddress(
                    sheet.Id,
                    (uint)firstCell.Address.RowNumber,
                    (uint)firstCell.Address.ColumnNumber);
                var end = new CellAddress(
                    sheet.Id,
                    (uint)lastCell.Address.RowNumber,
                    (uint)lastCell.Address.ColumnNumber);

                workbook.DefineNamedRange(namedRange.Name, new GridRange(start, end));
            }
            catch
            {
                // Skip any named range that cannot be mapped into the workbook model.
            }
        }
    }

    public static void Save(Workbook workbook, XLWorkbook xlWorkbook)
    {
        foreach (var (name, range) in workbook.NamedRanges)
        {
            try
            {
                var sheet = workbook.GetSheet(range.Start.Sheet);
                if (sheet is null)
                    continue;

                if (!xlWorkbook.TryGetWorksheet(sheet.Name, out _))
                    continue;

                var startA1 = range.Start.ToA1();
                var endA1 = range.End.ToA1();
                var sheetName = sheet.Name.Replace("'", "''");
                var address = $"'{sheetName}'!{startA1}:{endA1}";

                xlWorkbook.DefinedNames.Add(name, address);
            }
            catch
            {
                // Skip any named range that cannot be serialized to ClosedXML.
            }
        }
    }
}

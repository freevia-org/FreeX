using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class DataValidationCirclePlanner
{
    public static IReadOnlyList<CellAddress> FindInvalidDataCells(Workbook workbook, Sheet sheet)
    {
        if (sheet.DataValidations.Count == 0 || sheet.CellCount == 0)
            return [];

        var invalidCells = new List<CellAddress>();
        foreach (var address in sheet.EnumerateValueBearingCells())
        {
            var value = sheet.GetValue(address);
            foreach (var rule in DataValidationService.GetApplicable(sheet, address))
            {
                if (DataValidationService.Validate(rule, value, sheet, address, workbook) is null)
                    continue;

                invalidCells.Add(address);
                break;
            }
        }

        return invalidCells;
    }
}

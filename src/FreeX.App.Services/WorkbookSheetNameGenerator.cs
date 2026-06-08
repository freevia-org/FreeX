using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class WorkbookSheetNameGenerator
{
    public static string GenerateUniqueSheetName(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        for (var index = workbook.Sheets.Count + 1; index <= 10_000; index++)
        {
            var name = $"Sheet{index}";
            if (workbook.ValidateSheetName(name) is null)
                return name;
        }

        return $"Sheet{Guid.NewGuid():N}"[..31];
    }
}

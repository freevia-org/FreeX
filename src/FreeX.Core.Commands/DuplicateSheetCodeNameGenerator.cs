using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Generates a unique VBA CodeName for a duplicated sheet. CodeName (sheetPr/@codeName) is the
/// VBA project's internal identifier for a worksheet and must be unique per workbook; Excel
/// assigns a fresh CodeName when duplicating a sheet rather than copying the source's verbatim,
/// which would otherwise produce two sheets sharing the same codeName (invalid OOXML that Excel
/// treats as corrupt and "repairs" on open, silently renaming/dropping a codeName and breaking any
/// VBA that addressed the sheet by its code name).
/// </summary>
internal static class DuplicateSheetCodeNameGenerator
{
    public static string GenerateUniqueCodeName(Workbook workbook)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Sheets)
        {
            if (!string.IsNullOrWhiteSpace(sheet.CodeName))
                existing.Add(sheet.CodeName);
        }

        for (var n = 1; n < 100_000; n++)
        {
            var candidate = $"Sheet{n}";
            if (!existing.Contains(candidate))
                return candidate;
        }

        return $"Sheet{Guid.NewGuid():N}";
    }
}

using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class DuplicateSheetNameGenerator
{
    public static string GenerateCopyName(Workbook workbook, string sourceName)
    {
        for (int n = 2; n < 10_000; n++)
        {
            var suffix = $" ({n})";
            var baseName = sourceName.Length + suffix.Length <= 31
                ? sourceName
                : sourceName[..(31 - suffix.Length)];
            var candidate = baseName + suffix;
            if (workbook.ValidateSheetName(candidate) is null)
                return candidate;
        }

        return $"Sheet{Guid.NewGuid():N}"[..31];
    }
}

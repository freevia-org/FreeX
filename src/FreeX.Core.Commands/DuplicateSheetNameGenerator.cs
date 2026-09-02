using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class DuplicateSheetNameGenerator
{
    public static string GenerateCopyName(Workbook workbook, string sourceName)
    {
        for (int n = 2; n < 10_000; n++)
        {
            var suffix = $" ({n})";
            // r195: text-element aware, like the initial truncation r194 fixed. This loop re-slices at a
            // DIFFERENT cut point (31 minus the suffix), so guarding only the entry point left the
            // same lone-surrogate name reachable by renaming a sheet and duplicating it -- after
            // which every save to .xlsx throws. See SurrogateSafeTruncation.
            var baseName = sourceName.Length + suffix.Length <= 31
                ? sourceName
                : Free.Shared.IO.SurrogateSafeTruncation.LimitToTextElements(sourceName, 31 - suffix.Length);
            var candidate = baseName + suffix;
            if (workbook.ValidateSheetName(candidate) is null)
                return candidate;
        }

        return $"Sheet{Guid.NewGuid():N}"[..31];
    }
}

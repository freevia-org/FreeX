using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class NativeJsonColorMapper
{
    public static string FormatColor(CellColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static CellColor? ParseColor(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(normalized[2..4], NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(normalized[4..6], NumberStyles.HexNumber, null, out var b))
        {
            return null;
        }

        return new CellColor(r, g, b);
    }

    public static WorkbookThemeColorReference? ToThemeColorReference(ThemeColorReferenceDto? dto) =>
        dto is not null && Enum.IsDefined(dto.Slot)
            ? new WorkbookThemeColorReference(dto.Slot, dto.Tint)
            : null;

    public static ThemeColorReferenceDto? FromThemeColorReference(WorkbookThemeColorReference? reference) =>
        reference is null
            ? null
            : new ThemeColorReferenceDto { Slot = reference.Value.Slot, Tint = reference.Value.Tint };
}

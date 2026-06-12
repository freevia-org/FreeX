using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class NativeJsonColorMapper
{
    public static string FormatColor(CellColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static CellColor? ParseColor(string text) =>
        XlsxColorReader.TryParseHexColor(text, out var color) ? color : null;

    public static WorkbookThemeColorReference? ToThemeColorReference(ThemeColorReferenceDto? dto) =>
        dto is not null && Enum.IsDefined(dto.Slot)
            ? new WorkbookThemeColorReference(dto.Slot, dto.Tint)
            : null;

    public static ThemeColorReferenceDto? FromThemeColorReference(WorkbookThemeColorReference? reference) =>
        reference is null
            ? null
            : new ThemeColorReferenceDto { Slot = reference.Value.Slot, Tint = reference.Value.Tint };
}

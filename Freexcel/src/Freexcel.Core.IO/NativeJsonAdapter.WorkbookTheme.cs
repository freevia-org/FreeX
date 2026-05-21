using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorkbookThemeColorReference? ToThemeColorReference(ThemeColorReferenceDto? dto) =>
        NativeJsonColorMapper.ToThemeColorReference(dto);

    private static ThemeColorReferenceDto? FromThemeColorReference(WorkbookThemeColorReference? reference) =>
        NativeJsonColorMapper.FromThemeColorReference(reference);

    private static WorkbookTheme ToWorkbookTheme(WorkbookThemeDto dto)
    {
        var theme = WorkbookTheme.Office
            .WithName(dto.Name ?? WorkbookTheme.Office.Name)
            .WithFonts(dto.MajorFontName ?? WorkbookTheme.Office.MajorFontName,
                dto.MinorFontName ?? WorkbookTheme.Office.MinorFontName)
            .WithEffects(dto.EffectsName ?? WorkbookTheme.Office.EffectsName);

        foreach (var color in dto.Colors ?? [])
        {
            if (Enum.IsDefined(color.Slot) && ParseColor(color.Color ?? "") is { } parsed)
                theme = theme.WithColor(color.Slot, parsed);
        }

        return theme;
    }

    private static WorkbookThemeDto FromWorkbookTheme(WorkbookTheme theme) =>
        new()
        {
            Name = theme.Name,
            MajorFontName = theme.MajorFontName,
            MinorFontName = theme.MinorFontName,
            EffectsName = theme.EffectsName,
            Colors = Enum.GetValues<WorkbookThemeColorSlot>()
                .Select(slot => new WorkbookThemeColorDto
                {
                    Slot = slot,
                    Color = FormatColor(theme.GetColor(slot))
                })
                .ToList()
        };
}

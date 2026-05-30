using FreeX.Core.Model;

namespace FreeX.Core.IO;

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

        return theme
            .WithNativeColorSchemeXml(dto.NativeColorSchemeXml)
            .WithNativeFontSchemeXml(dto.NativeFontSchemeXml)
            .WithNativeFormatSchemeXml(dto.NativeFormatSchemeXml)
            .WithNativeThemeSupplementXml(dto.NativeThemeSupplementXml)
            .WithSupplementalMetadata(
                (dto.AlternateColorSchemes ?? []).Select(ToAlternateColorScheme).ToArray(),
                dto.HasObjectDefaults);
    }

    private static WorkbookThemeDto FromWorkbookTheme(WorkbookTheme theme) =>
        new()
        {
            Name = theme.Name,
            MajorFontName = theme.MajorFontName,
            MinorFontName = theme.MinorFontName,
            EffectsName = theme.EffectsName,
            NativeColorSchemeXml = theme.NativeColorSchemeXml,
            NativeFontSchemeXml = theme.NativeFontSchemeXml,
            NativeFormatSchemeXml = theme.NativeFormatSchemeXml,
            NativeThemeSupplementXml = theme.NativeThemeSupplementXml,
            HasObjectDefaults = theme.HasObjectDefaults,
            AlternateColorSchemes = (theme.AlternateColorSchemes ?? [])
                .Select(FromAlternateColorScheme)
                .ToList(),
            Colors = Enum.GetValues<WorkbookThemeColorSlot>()
                .Select(slot => new WorkbookThemeColorDto
                {
                    Slot = slot,
                    Color = FormatColor(theme.GetColor(slot))
                })
                .ToList()
        };

    private static WorkbookThemeAlternateColorScheme ToAlternateColorScheme(
        WorkbookThemeAlternateColorSchemeDto dto)
    {
        var colors = new Dictionary<WorkbookThemeColorSlot, CellColor>();
        foreach (var color in dto.Colors ?? [])
        {
            if (Enum.IsDefined(color.Slot) && ParseColor(color.Color ?? "") is { } parsed)
                colors[color.Slot] = parsed;
        }

        return new WorkbookThemeAlternateColorScheme(
            string.IsNullOrWhiteSpace(dto.Name) ? "Alternate Colors" : dto.Name.Trim(),
            colors,
            dto.NativeColorSchemeXml);
    }

    private static WorkbookThemeAlternateColorSchemeDto FromAlternateColorScheme(
        WorkbookThemeAlternateColorScheme scheme) =>
        new()
        {
            Name = scheme.Name,
            NativeColorSchemeXml = scheme.NativeColorSchemeXml,
            Colors = Enum.GetValues<WorkbookThemeColorSlot>()
                .Where(slot => scheme.Colors.ContainsKey(slot))
                .Select(slot => new WorkbookThemeColorDto
                {
                    Slot = slot,
                    Color = FormatColor(scheme.Colors[slot])
                })
                .ToList()
        };
}

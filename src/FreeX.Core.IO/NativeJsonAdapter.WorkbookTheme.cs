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
                dto.HasObjectDefaults,
                ToObjectDefaults(dto.ObjectDefaults));
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
            ObjectDefaults = FromObjectDefaults(theme.ObjectDefaults),
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

    private static WorkbookThemeObjectDefaults? ToObjectDefaults(
        WorkbookThemeObjectDefaultsDto? dto)
    {
        if (dto is null)
            return null;

        return new WorkbookThemeObjectDefaults(
            ToShapeObjectDefault(dto.Shape),
            ToLineObjectDefault(dto.Line),
            ToTextObjectDefault(dto.Text),
            dto.NativeObjectDefaultsXml);
    }

    private static WorkbookThemeShapeObjectDefault? ToShapeObjectDefault(
        WorkbookThemeShapeObjectDefaultDto? dto) =>
        dto is null
            ? null
            : new WorkbookThemeShapeObjectDefault(
                ToThemeColorReference(dto.FillThemeColor),
                ParseColor(dto.FillColor ?? ""),
                ToThemeColorReference(dto.OutlineThemeColor),
                ParseColor(dto.OutlineColor ?? ""),
                dto.OutlineWidthPoints);

    private static WorkbookThemeLineObjectDefault? ToLineObjectDefault(
        WorkbookThemeLineObjectDefaultDto? dto) =>
        dto is null
            ? null
            : new WorkbookThemeLineObjectDefault(
                ToThemeColorReference(dto.StrokeThemeColor),
                ParseColor(dto.StrokeColor ?? ""),
                dto.StrokeWidthPoints);

    private static WorkbookThemeTextObjectDefault? ToTextObjectDefault(
        WorkbookThemeTextObjectDefaultDto? dto) =>
        dto is null
            ? null
            : new WorkbookThemeTextObjectDefault(
                ToThemeColorReference(dto.TextThemeColor),
                ParseColor(dto.TextColor ?? ""),
                dto.Typeface);

    private static WorkbookThemeObjectDefaultsDto? FromObjectDefaults(
        WorkbookThemeObjectDefaults? defaults) =>
        defaults is null
            ? null
            : new WorkbookThemeObjectDefaultsDto
            {
                Shape = FromShapeObjectDefault(defaults.Shape),
                Line = FromLineObjectDefault(defaults.Line),
                Text = FromTextObjectDefault(defaults.Text),
                NativeObjectDefaultsXml = defaults.NativeObjectDefaultsXml
            };

    private static WorkbookThemeShapeObjectDefaultDto? FromShapeObjectDefault(
        WorkbookThemeShapeObjectDefault? shape) =>
        shape is null
            ? null
            : new WorkbookThemeShapeObjectDefaultDto
            {
                FillThemeColor = FromThemeColorReference(shape.FillThemeColor),
                FillColor = shape.FillColor is { } fillColor ? FormatColor(fillColor) : null,
                OutlineThemeColor = FromThemeColorReference(shape.OutlineThemeColor),
                OutlineColor = shape.OutlineColor is { } outlineColor ? FormatColor(outlineColor) : null,
                OutlineWidthPoints = shape.OutlineWidthPoints
            };

    private static WorkbookThemeLineObjectDefaultDto? FromLineObjectDefault(
        WorkbookThemeLineObjectDefault? line) =>
        line is null
            ? null
            : new WorkbookThemeLineObjectDefaultDto
            {
                StrokeThemeColor = FromThemeColorReference(line.StrokeThemeColor),
                StrokeColor = line.StrokeColor is { } strokeColor ? FormatColor(strokeColor) : null,
                StrokeWidthPoints = line.StrokeWidthPoints
            };

    private static WorkbookThemeTextObjectDefaultDto? FromTextObjectDefault(
        WorkbookThemeTextObjectDefault? text) =>
        text is null
            ? null
            : new WorkbookThemeTextObjectDefaultDto
            {
                TextThemeColor = FromThemeColorReference(text.TextThemeColor),
                TextColor = text.TextColor is { } textColor ? FormatColor(textColor) : null,
                Typeface = text.Typeface
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

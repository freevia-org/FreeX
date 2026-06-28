using FreeX.Core.Model;
using PresentationPlanner = FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatPresetGalleryPlanner;

namespace FreeX.App.Host;

public sealed record ConditionalFormatDataBarPreset(
    string Style,
    string Label,
    string Category,
    string KeyTip,
    RgbColor Color,
    bool Gradient);

public sealed record ConditionalFormatColorScalePreset(
    string Style,
    string Label,
    string Category,
    string KeyTip,
    RgbColor MinColor,
    RgbColor? MidColor,
    RgbColor MaxColor);

public sealed record ConditionalFormatPresetGalleryGroup<TPreset>(
    string Name,
    IReadOnlyList<TPreset> Options);

public static class ConditionalFormatPresetGalleryPlanner
{
    public static IReadOnlyList<ConditionalFormatDataBarPreset> DataBarOptions =>
        PresentationPlanner.DataBarOptions.Select(Localize).ToArray();

    public static IReadOnlyList<ConditionalFormatColorScalePreset> ColorScaleOptions =>
        PresentationPlanner.ColorScaleOptions.Select(Localize).ToArray();

    public static IReadOnlyList<ConditionalFormatPresetGalleryGroup<ConditionalFormatDataBarPreset>> DataBarGroups =>
        PresentationPlanner.DataBarGroups
            .Select(group => new ConditionalFormatPresetGalleryGroup<ConditionalFormatDataBarPreset>(
                UiText.Get(group.CategoryKey),
                group.Options.Select(Localize).ToArray()))
            .ToArray();

    public static IReadOnlyList<ConditionalFormatPresetGalleryGroup<ConditionalFormatColorScalePreset>> ColorScaleGroups =>
        PresentationPlanner.ColorScaleGroups
            .Select(group => new ConditionalFormatPresetGalleryGroup<ConditionalFormatColorScalePreset>(
                UiText.Get(group.CategoryKey),
                group.Options.Select(Localize).ToArray()))
            .ToArray();

    public static ConditionalFormat? CreateDataBarRule(string? style, GridRange range) =>
        PresentationPlanner.CreateDataBarRule(style, range);

    public static ConditionalFormat? CreateColorScaleRule(string? style, GridRange range) =>
        PresentationPlanner.CreateColorScaleRule(style, range);

    private static ConditionalFormatDataBarPreset Localize(
        FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatDataBarPreset option) =>
        new(
            option.Style,
            UiText.Get(option.LabelKey),
            UiText.Get(option.CategoryKey),
            option.KeyTip,
            option.Color,
            option.Gradient);

    private static ConditionalFormatColorScalePreset Localize(
        FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatColorScalePreset option) =>
        new(
            option.Style,
            UiText.Get(option.LabelKey),
            UiText.Get(option.CategoryKey),
            option.KeyTip,
            option.MinColor,
            option.MidColor,
            option.MaxColor);
}

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ChartTypePickerOption(ChartType Type, string DisplayName, bool IsRecommended = false);

public sealed record ChartTypePickerCategory(string Name, IReadOnlyList<ChartTypePickerOption> Options);

public sealed record ChartTypeGalleryChoice(
    ChartType Type,
    string CategoryName,
    string SubtypeName,
    string PreviewText,
    bool IsRecommended = false);

public static class ChartTypePickerPlanner
{
    private static readonly ChartTypePickerOption[] Options =
    [
        new(ChartType.Column, UiText.Get("ChartType_ClusteredColumn"), true),
        new(ChartType.StackedColumn, UiText.Get("ChartType_StackedColumn")),
        new(ChartType.PercentStackedColumn, UiText.Get("ChartType_PercentStackedColumn")),
        new(ChartType.ThreeDColumn, UiText.Get("ChartType_ThreeDColumn")),
        new(ChartType.Line, UiText.Get("ChartType_Line"), true),
        new(ChartType.ThreeDLine, UiText.Get("ChartType_ThreeDLine")),
        new(ChartType.Pie, UiText.Get("ChartType_Pie"), true),
        new(ChartType.ThreeDPie, UiText.Get("ChartType_ThreeDPie")),
        new(ChartType.Doughnut, UiText.Get("ChartType_Doughnut")),
        new(ChartType.Bar, UiText.Get("ChartType_ClusteredBar"), true),
        new(ChartType.StackedBar, UiText.Get("ChartType_StackedBar")),
        new(ChartType.PercentStackedBar, UiText.Get("ChartType_PercentStackedBar")),
        new(ChartType.ThreeDBar, UiText.Get("ChartType_ThreeDBar")),
        new(ChartType.Scatter, UiText.Get("ChartType_Scatter"), true),
        new(ChartType.Bubble, UiText.Get("ChartType_Bubble")),
        new(ChartType.Area, UiText.Get("ChartType_Area")),
        new(ChartType.ThreeDArea, UiText.Get("ChartType_ThreeDArea")),
        new(ChartType.Radar, UiText.Get("ChartType_Radar")),
        new(ChartType.Stock, UiText.Get("ChartType_Stock")),
        new(ChartType.Surface, UiText.Get("ChartType_Surface")),
        new(ChartType.ThreeDSurface, UiText.Get("ChartType_ThreeDSurface"))
    ];

    public static IReadOnlyList<ChartTypePickerOption> GetSupportedOptions() =>
        Options.Where(option => ChartTypeSupport.IsRenderable(option.Type)).ToList();

    public static IReadOnlyList<ChartTypePickerOption> GetRecommendedOptions() =>
        new[]
        {
            ChartType.Column,
            ChartType.Line,
            ChartType.Bar,
            ChartType.Pie,
            ChartType.Scatter
        }
        .Select(type => Options.Single(option => option.Type == type))
        .Where(option => option.IsRecommended && ChartTypeSupport.IsRenderable(option.Type))
        .ToList();

    public static IReadOnlyList<ChartTypePickerCategory> GetCategories()
    {
        var supported = GetSupportedOptions();
        return new (string Name, ChartType[] Types)[]
            {
                (UiText.Get("ChartTypeCategory_Column"), [ChartType.Column, ChartType.StackedColumn, ChartType.PercentStackedColumn, ChartType.ThreeDColumn]),
                (UiText.Get("ChartTypeCategory_Line"), [ChartType.Line, ChartType.ThreeDLine]),
                (UiText.Get("ChartTypeCategory_Pie"), [ChartType.Pie, ChartType.ThreeDPie, ChartType.Doughnut]),
                (UiText.Get("ChartTypeCategory_Bar"), [ChartType.Bar, ChartType.StackedBar, ChartType.PercentStackedBar, ChartType.ThreeDBar]),
                (UiText.Get("ChartTypeCategory_Area"), [ChartType.Area, ChartType.ThreeDArea]),
                (UiText.Get("ChartTypeCategory_Scatter"), [ChartType.Scatter, ChartType.Bubble]),
                (UiText.Get("ChartTypeCategory_Stock"), [ChartType.Stock]),
                (UiText.Get("ChartTypeCategory_Radar"), [ChartType.Radar]),
                (UiText.Get("ChartTypeCategory_Surface"), [ChartType.Surface, ChartType.ThreeDSurface])
            }
            .Select(category => new ChartTypePickerCategory(
                category.Name,
                category.Types
                    .Select(type => supported.FirstOrDefault(option => option.Type == type))
                    .OfType<ChartTypePickerOption>()
                    .ToList()))
            .Where(category => category.Options.Count > 0)
            .ToList();
    }

    public static IReadOnlyList<ChartTypeGalleryChoice> GetGalleryChoices(string categoryName) =>
        GetCategories()
            .Where(category => category.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(category => category.Options.Select(option => new ChartTypeGalleryChoice(
                option.Type,
                category.Name,
                option.DisplayName,
                UiText.Format("ChartTypePicker_PreviewTextFormat", option.DisplayName),
                option.IsRecommended)))
            .ToList();

    public static IReadOnlyList<ChartTypeGalleryChoice> GetRecommendedGalleryChoices() =>
        GetRecommendedOptions()
            .Select(option => new ChartTypeGalleryChoice(
                option.Type,
                UiText.Get("ChartTypePicker_RecommendedCategory"),
                option.DisplayName,
                UiText.Format("ChartTypePicker_PreviewTextFormat", option.DisplayName),
                IsRecommended: true))
            .ToList();
}

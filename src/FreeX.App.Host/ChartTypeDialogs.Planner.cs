using FreeX.App.Presentation.Charts.Editing;
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
    private static readonly HashSet<ChartType> RecommendedTypes = ChartTypeChangePlanner
        .GetRecommendedTypes()
        .ToHashSet();

    public static IReadOnlyList<ChartTypePickerOption> GetSupportedOptions() =>
        ChartTypeChangePlanner.GetSupportedChoices()
            .Select(choice => CreateOption(choice.Type))
            .ToList();

    public static IReadOnlyList<ChartTypePickerOption> GetRecommendedOptions() =>
        ChartTypeChangePlanner.GetRecommendedTypes()
        .Select(CreateOption)
        .ToList();

    public static IReadOnlyList<ChartTypePickerCategory> GetCategories()
    {
        return ChartTypeChangePlanner.GetCategories()
            .Select(category => new ChartTypePickerCategory(
                UiText.Get(category.NameKey),
                category.Choices
                    .Select(choice => CreateOption(choice.Type))
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

    private static ChartTypePickerOption CreateOption(ChartType type) =>
        new(
            type,
            UiText.Get(ChartTypeChangePlanner.DisplayNameKey(type)),
            RecommendedTypes.Contains(type));
}

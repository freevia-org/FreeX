using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

public sealed record ChartTypePickerOptionPlan(
    ChartType Type,
    string DisplayNameKey,
    string FallbackDisplayName,
    bool IsRecommended = false);

public sealed record ChartTypePickerCategoryPlan(
    string NameKey,
    IReadOnlyList<ChartTypePickerOptionPlan> Options);

public sealed record ChartTypeGalleryChoicePlan(
    ChartType Type,
    string CategoryNameKey,
    string SubtypeNameKey,
    string PreviewTextFormatKey,
    bool IsRecommended = false);

/// <summary>
/// Renderer-neutral planner for chart type picker option/category/gallery data. Shells resolve the
/// returned resource keys through their own localization layer and bind the projected values to UI.
/// </summary>
public static class ChartTypePickerPlanner
{
    public const string RecommendedCategoryKey = "ChartTypePicker_RecommendedCategory";
    public const string PreviewTextFormatKey = "ChartTypePicker_PreviewTextFormat";

    private static readonly HashSet<ChartType> RecommendedTypes = ChartTypeChangePlanner
        .GetRecommendedTypes()
        .ToHashSet();

    public static IReadOnlyList<ChartTypePickerOptionPlan> GetSupportedOptions() =>
        ChartTypeChangePlanner.GetSupportedChoices()
            .Select(choice => CreateOption(choice.Type))
            .ToList();

    public static IReadOnlyList<ChartTypePickerOptionPlan> GetRecommendedOptions() =>
        ChartTypeChangePlanner.GetRecommendedTypes()
            .Select(CreateOption)
            .ToList();

    public static IReadOnlyList<ChartTypePickerCategoryPlan> GetCategories()
    {
        return ChartTypeChangePlanner.GetCategories()
            .Select(category => new ChartTypePickerCategoryPlan(
                category.NameKey,
                category.Choices
                    .Select(choice => CreateOption(choice.Type))
                    .ToList()))
            .Where(category => category.Options.Count > 0)
            .ToList();
    }

    public static IReadOnlyList<ChartTypeGalleryChoicePlan> GetGalleryChoices(string categoryNameKey) =>
        GetCategories()
            .Where(category => category.NameKey.Equals(categoryNameKey, StringComparison.OrdinalIgnoreCase))
            .SelectMany(category => category.Options.Select(option => new ChartTypeGalleryChoicePlan(
                option.Type,
                category.NameKey,
                option.DisplayNameKey,
                PreviewTextFormatKey,
                option.IsRecommended)))
            .ToList();

    public static IReadOnlyList<ChartTypeGalleryChoicePlan> GetRecommendedGalleryChoices() =>
        GetRecommendedOptions()
            .Select(option => new ChartTypeGalleryChoicePlan(
                option.Type,
                RecommendedCategoryKey,
                option.DisplayNameKey,
                PreviewTextFormatKey,
                IsRecommended: true))
            .ToList();

    private static ChartTypePickerOptionPlan CreateOption(ChartType type) =>
        new(
            type,
            ChartTypeChangePlanner.DisplayNameKey(type),
            ChartTypeChangePlanner.DisplayName(type),
            RecommendedTypes.Contains(type));
}

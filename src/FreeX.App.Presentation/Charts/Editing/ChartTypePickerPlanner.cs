using FreeX.Core.Model;
using FreeX.App.Presentation.Localization;

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

public sealed record ChartTypePickerOption(
    ChartType Type,
    string DisplayName,
    bool IsRecommended = false);

public sealed record ChartTypePickerCategory(
    string Name,
    IReadOnlyList<ChartTypePickerOption> Options);

public sealed record ChartTypeGalleryChoice(
    ChartType Type,
    string CategoryName,
    string SubtypeName,
    string PreviewText,
    bool IsRecommended = false);

public enum ChartTypePickerPanelKind
{
    Recommended,
    AllCharts,
}

public sealed record ChartTypePickerPreviewDescriptor(
    string TitleResourceKey,
    string BodyResourceKey,
    string SampleLabelResourceKey);

public sealed record ChartTypePickerPanelDescriptor(
    ChartTypePickerPanelKind Kind,
    string HeadingResourceKey,
    string HelpResourceKey,
    string SubtypeGalleryAutomationNameResourceKey,
    ChartTypePickerPreviewDescriptor Preview,
    string? CategoryListAutomationNameResourceKey = null);

/// <summary>
/// Renderer-neutral planner for chart type picker option/category/gallery data. Shells resolve the
/// returned resource keys through their own localization layer and bind the projected values to UI.
/// </summary>
public static class ChartTypePickerPlanner
{
    public const string ChooseChartTypeHeadingKey = "ChartTypePicker_ChooseChartTypeHeading";
    public const string RecommendedCategoryKey = "ChartTypePicker_RecommendedCategory";
    public const string PreviewTextFormatKey = "ChartTypePicker_PreviewTextFormat";

    private static readonly HashSet<ChartType> RecommendedTypes = ChartTypeChangePlanner
        .GetRecommendedTypes()
        .ToHashSet();

    private static readonly ChartTypePickerPreviewDescriptor RecommendedPreview = new(
        "ChartTypePicker_PreviewTitle",
        "ChartTypePicker_RecommendedPreviewBody",
        "ChartTypePicker_PreviewSampleLabel");

    private static readonly ChartTypePickerPreviewDescriptor AllChartsPreview = new(
        "ChartTypePicker_PreviewTitle",
        "ChartTypePicker_ChartPreviewBody",
        "ChartTypePicker_PreviewSampleLabel");

    private static readonly ChartTypePickerPanelDescriptor RecommendedPanel = new(
        ChartTypePickerPanelKind.Recommended,
        ChooseChartTypeHeadingKey,
        "ChartTypePicker_RecommendedHelpText",
        "ChartTypePicker_SubtypeGalleryAutomationName",
        RecommendedPreview);

    private static readonly ChartTypePickerPanelDescriptor AllChartsPanel = new(
        ChartTypePickerPanelKind.AllCharts,
        "ChartTypePicker_AllChartsHeading",
        "ChartTypePicker_AllChartsHelpText",
        "ChartTypePicker_SubtypeGalleryAutomationName",
        AllChartsPreview,
        "ChartTypePicker_CategoriesAutomationName");

    public static IReadOnlyList<ChartTypePickerOptionPlan> GetSupportedOptions() =>
        ChartTypeChangePlanner.GetSupportedChoices()
            .Select(choice => CreateOption(choice.Type))
            .ToList();

    public static IReadOnlyList<ChartTypePickerOption> GetSupportedOptions(ResourceKeyTextResolver text) =>
        GetSupportedOptions().Select(option => CreateOption(option, text)).ToList();

    public static IReadOnlyList<ChartTypePickerOptionPlan> GetRecommendedOptions() =>
        ChartTypeChangePlanner.GetRecommendedTypes()
            .Select(CreateOption)
            .ToList();

    public static IReadOnlyList<ChartTypePickerOption> GetRecommendedOptions(ResourceKeyTextResolver text) =>
        GetRecommendedOptions().Select(option => CreateOption(option, text)).ToList();

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

    public static IReadOnlyList<ChartTypePickerCategory> GetCategories(ResourceKeyTextResolver text) =>
        GetCategories()
            .Select(category => new ChartTypePickerCategory(
                text.Get(category.NameKey),
                category.Options.Select(option => CreateOption(option, text)).ToList()))
            .ToList();

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

    public static IReadOnlyList<ChartTypeGalleryChoice> GetGalleryChoices(
        string categoryName,
        ResourceKeyTextResolver text)
    {
        var category = GetCategories().FirstOrDefault(candidate =>
            candidate.NameKey.Equals(categoryName, StringComparison.OrdinalIgnoreCase) ||
            text.Get(candidate.NameKey).Equals(categoryName, StringComparison.OrdinalIgnoreCase));
        return category is null
            ? []
            : GetGalleryChoices(category.NameKey)
                .Select(choice => CreateGalleryChoice(choice, text))
                .ToList();
    }

    public static IReadOnlyList<ChartTypeGalleryChoicePlan> GetRecommendedGalleryChoices() =>
        GetRecommendedOptions()
            .Select(option => new ChartTypeGalleryChoicePlan(
                option.Type,
                RecommendedCategoryKey,
                option.DisplayNameKey,
                PreviewTextFormatKey,
                IsRecommended: true))
            .ToList();

    public static IReadOnlyList<ChartTypeGalleryChoice> GetRecommendedGalleryChoices(ResourceKeyTextResolver text) =>
        GetRecommendedGalleryChoices().Select(choice => CreateGalleryChoice(choice, text)).ToList();

    public static ChartTypePickerPanelDescriptor GetRecommendedPanel() => RecommendedPanel;

    public static ChartTypePickerPanelDescriptor GetAllChartsPanel() => AllChartsPanel;

    private static ChartTypePickerOptionPlan CreateOption(ChartType type) =>
        new(
            type,
            ChartTypeChangePlanner.DisplayNameKey(type),
            ChartTypeChangePlanner.DisplayName(type),
            RecommendedTypes.Contains(type));

    private static ChartTypePickerOption CreateOption(
        ChartTypePickerOptionPlan option,
        ResourceKeyTextResolver text) =>
        new(option.Type, text.Get(option.DisplayNameKey), option.IsRecommended);

    private static ChartTypeGalleryChoice CreateGalleryChoice(
        ChartTypeGalleryChoicePlan choice,
        ResourceKeyTextResolver text)
    {
        var subtypeName = text.Get(choice.SubtypeNameKey);
        return new ChartTypeGalleryChoice(
            choice.Type,
            text.Get(choice.CategoryNameKey),
            subtypeName,
            text.Format(choice.PreviewTextFormatKey, subtypeName),
            choice.IsRecommended);
    }
}

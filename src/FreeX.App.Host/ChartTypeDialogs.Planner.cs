using FreeX.Core.Model;
using ChartTypeGalleryChoicePlan = FreeX.App.Presentation.Charts.Editing.ChartTypeGalleryChoicePlan;
using ChartTypePickerCategoryPlan = FreeX.App.Presentation.Charts.Editing.ChartTypePickerCategoryPlan;
using ChartTypePickerOptionPlan = FreeX.App.Presentation.Charts.Editing.ChartTypePickerOptionPlan;
using PresentationChartTypePickerPlanner = FreeX.App.Presentation.Charts.Editing.ChartTypePickerPlanner;

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
    public static IReadOnlyList<ChartTypePickerOption> GetSupportedOptions() =>
        PresentationChartTypePickerPlanner.GetSupportedOptions()
            .Select(CreateOption)
            .ToList();

    public static IReadOnlyList<ChartTypePickerOption> GetRecommendedOptions() =>
        PresentationChartTypePickerPlanner.GetRecommendedOptions()
            .Select(CreateOption)
            .ToList();

    public static IReadOnlyList<ChartTypePickerCategory> GetCategories()
    {
        return PresentationChartTypePickerPlanner.GetCategories()
            .Select(category => new ChartTypePickerCategory(
                UiText.Get(category.NameKey),
                category.Options
                    .Select(CreateOption)
                    .ToList()))
            .Where(category => category.Options.Count > 0)
            .ToList();
    }

    public static IReadOnlyList<ChartTypeGalleryChoice> GetGalleryChoices(string categoryName)
    {
        var category = FindCategoryPlan(categoryName);
        if (category is null)
            return [];

        return PresentationChartTypePickerPlanner.GetGalleryChoices(category.NameKey)
            .Select(CreateGalleryChoice)
            .ToList();
    }

    public static IReadOnlyList<ChartTypeGalleryChoice> GetRecommendedGalleryChoices() =>
        PresentationChartTypePickerPlanner.GetRecommendedGalleryChoices()
            .Select(CreateGalleryChoice)
            .ToList();

    private static ChartTypePickerOption CreateOption(ChartTypePickerOptionPlan plan) =>
        new(
            plan.Type,
            UiText.Get(plan.DisplayNameKey),
            plan.IsRecommended);

    private static ChartTypeGalleryChoice CreateGalleryChoice(ChartTypeGalleryChoicePlan plan)
    {
        var subtypeName = UiText.Get(plan.SubtypeNameKey);
        return new ChartTypeGalleryChoice(
            plan.Type,
            UiText.Get(plan.CategoryNameKey),
            subtypeName,
            UiText.Format(plan.PreviewTextFormatKey, subtypeName),
            plan.IsRecommended);
    }

    private static ChartTypePickerCategoryPlan? FindCategoryPlan(string categoryName)
    {
        foreach (var category in PresentationChartTypePickerPlanner.GetCategories())
        {
            if (category.NameKey.Equals(categoryName, StringComparison.OrdinalIgnoreCase)
                || UiText.Get(category.NameKey).Equals(categoryName, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return null;
    }
}

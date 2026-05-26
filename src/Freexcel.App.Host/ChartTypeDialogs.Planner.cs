using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        new(ChartType.Column, "Clustered Column", true),
        new(ChartType.StackedColumn, "Stacked Column"),
        new(ChartType.PercentStackedColumn, "100% Stacked Column"),
        new(ChartType.ThreeDColumn, "3D Column"),
        new(ChartType.Line, "Line", true),
        new(ChartType.ThreeDLine, "3D Line"),
        new(ChartType.Pie, "Pie", true),
        new(ChartType.ThreeDPie, "3D Pie"),
        new(ChartType.Doughnut, "Doughnut"),
        new(ChartType.Bar, "Clustered Bar", true),
        new(ChartType.StackedBar, "Stacked Bar"),
        new(ChartType.PercentStackedBar, "100% Stacked Bar"),
        new(ChartType.ThreeDBar, "3D Bar"),
        new(ChartType.Scatter, "Scatter", true),
        new(ChartType.Bubble, "Bubble"),
        new(ChartType.Area, "Area"),
        new(ChartType.ThreeDArea, "3D Area"),
        new(ChartType.Radar, "Radar"),
        new(ChartType.Stock, "Stock"),
        new(ChartType.Surface, "Surface"),
        new(ChartType.ThreeDSurface, "3D Surface")
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
                ("Column", [ChartType.Column, ChartType.StackedColumn, ChartType.PercentStackedColumn, ChartType.ThreeDColumn]),
                ("Line", [ChartType.Line, ChartType.ThreeDLine]),
                ("Pie", [ChartType.Pie, ChartType.ThreeDPie, ChartType.Doughnut]),
                ("Bar", [ChartType.Bar, ChartType.StackedBar, ChartType.PercentStackedBar, ChartType.ThreeDBar]),
                ("Area", [ChartType.Area, ChartType.ThreeDArea]),
                ("X Y (Scatter)", [ChartType.Scatter, ChartType.Bubble]),
                ("Stock", [ChartType.Stock]),
                ("Radar", [ChartType.Radar]),
                ("Surface", [ChartType.Surface, ChartType.ThreeDSurface])
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
                $"Preview: {option.DisplayName}",
                option.IsRecommended)))
            .ToList();

    public static IReadOnlyList<ChartTypeGalleryChoice> GetRecommendedGalleryChoices() =>
        GetRecommendedOptions()
            .Select(option => new ChartTypeGalleryChoice(
                option.Type,
                "Recommended Charts",
                option.DisplayName,
                $"Preview: {option.DisplayName}",
                IsRecommended: true))
            .ToList();
}

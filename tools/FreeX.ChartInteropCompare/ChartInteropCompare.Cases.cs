using System.Collections.Generic;
using System.Linq;
using FreeX.Core.Model;

internal static partial class ChartInteropCompare
{
    private static List<ChartCase> FilterCases(IEnumerable<ChartCase> cases, CompareOptions options)
    {
        var filtered = cases;
        if (options.ChartFilters.Count > 0)
        {
            filtered = filtered.Where(chartCase =>
                options.ChartFilters.Contains(chartCase.Name) ||
                options.ChartFilters.Contains(chartCase.Type.ToString()));
        }

        if (options.FamilyFilters.Count > 0)
            filtered = filtered.Where(chartCase => options.FamilyFilters.Contains(chartCase.Family));

        return filtered.ToList();
    }

    private static List<ChartCase> CreateCases() =>
    [
        new("Column", ChartType.Column, 51, ChartFixtureKind.CategorySeries),
        new("StackedColumn", ChartType.StackedColumn, 52, ChartFixtureKind.CategorySeries),
        new("PercentStackedColumn", ChartType.PercentStackedColumn, 53, ChartFixtureKind.CategorySeries),
        new("ThreeDColumn", ChartType.ThreeDColumn, 54, ChartFixtureKind.CategorySeries),
        new("Line", ChartType.Line, 4, ChartFixtureKind.CategorySeries),
        new("ThreeDLine", ChartType.ThreeDLine, -4101, ChartFixtureKind.CategorySeries),
        new("Pie", ChartType.Pie, 5, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("ThreeDPie", ChartType.ThreeDPie, -4102, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Doughnut", ChartType.Doughnut, -4120, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Bar", ChartType.Bar, 57, ChartFixtureKind.CategorySeries),
        new("StackedBar", ChartType.StackedBar, 58, ChartFixtureKind.CategorySeries),
        new("PercentStackedBar", ChartType.PercentStackedBar, 59, ChartFixtureKind.CategorySeries),
        new("ThreeDBar", ChartType.ThreeDBar, 60, ChartFixtureKind.CategorySeries),
        new("Scatter", ChartType.Scatter, -4169, ChartFixtureKind.Scatter, FirstColIsCategories: false),
        new("Bubble", ChartType.Bubble, 15, ChartFixtureKind.Bubble, FirstColIsCategories: false),
        new("Area", ChartType.Area, 1, ChartFixtureKind.CategorySeries),
        new("ThreeDArea", ChartType.ThreeDArea, -4098, ChartFixtureKind.CategorySeries),
        new("Radar", ChartType.Radar, -4151, ChartFixtureKind.CategorySeries),
        new("Stock", ChartType.Stock, 88, ChartFixtureKind.Stock),
        new("Surface", ChartType.Surface, 85, ChartFixtureKind.Surface),
        new("ThreeDSurface", ChartType.ThreeDSurface, 83, ChartFixtureKind.Surface),
        new("Treemap", ChartType.Treemap, 117, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Sunburst", ChartType.Sunburst, 120, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Histogram", ChartType.Histogram, 118, ChartFixtureKind.Histogram, FirstColIsCategories: false, ShowLegend: false),
        new("Pareto", ChartType.Pareto, 122, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("BoxAndWhisker", ChartType.BoxAndWhisker, 121, ChartFixtureKind.BoxAndWhisker),
        new("Waterfall", ChartType.Waterfall, 119, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Funnel", ChartType.Funnel, 123, ChartFixtureKind.SingleSeries, ShowLegend: false)
    ];
}

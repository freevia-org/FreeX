using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class RecommendedChartPlannerTests
{
    [Fact]
    public void Recommend_PrefersLineForTemporalCategories()
    {
        var sheet = CreateSheet();
        Set(sheet, 1, 1, new TextValue("Month"));
        Set(sheet, 1, 2, new TextValue("Sales"));
        Set(sheet, 2, 1, new DateTimeValue(45292));
        Set(sheet, 2, 2, new NumberValue(10));
        Set(sheet, 3, 1, new DateTimeValue(45323));
        Set(sheet, 3, 2, new NumberValue(15));

        RecommendedChartPlanner.Recommend(sheet, Range(sheet, 1, 1, 3, 2))
            .Should().Equal(ChartType.Line, ChartType.Column, ChartType.Area);
    }

    [Fact]
    public void Recommend_PrefersPieForOneCategoricalSeries()
    {
        var sheet = CreateSheet();
        Set(sheet, 1, 1, new TextValue("Region"));
        Set(sheet, 1, 2, new TextValue("Sales"));
        Set(sheet, 2, 1, new TextValue("North"));
        Set(sheet, 2, 2, new NumberValue(10));
        Set(sheet, 3, 1, new TextValue("South"));
        Set(sheet, 3, 2, new NumberValue(15));

        RecommendedChartPlanner.Recommend(sheet, Range(sheet, 1, 1, 3, 2))
            .Should().Equal(ChartType.Column, ChartType.Pie, ChartType.Bar);
    }

    [Fact]
    public void Recommend_PrefersScatterForTwoNumericColumns()
    {
        var sheet = CreateSheet();
        Set(sheet, 1, 1, new TextValue("Height"));
        Set(sheet, 1, 2, new TextValue("Weight"));
        Set(sheet, 2, 1, new NumberValue(160));
        Set(sheet, 2, 2, new NumberValue(55));
        Set(sheet, 3, 1, new NumberValue(170));
        Set(sheet, 3, 2, new NumberValue(68));

        RecommendedChartPlanner.Recommend(sheet, Range(sheet, 1, 1, 3, 2))
            .Should().Equal(ChartType.Scatter, ChartType.Line, ChartType.Column);
    }

    [Fact]
    public void Recommend_UsesDefaultOptionsWhenTheRangeCannotBeAnalyzed()
    {
        var sheet = CreateSheet();
        Set(sheet, 1, 1, new TextValue("Only text"));
        Set(sheet, 2, 1, new TextValue("No series"));

        RecommendedChartPlanner.Recommend(sheet, Range(sheet, 1, 1, 2, 1))
            .Should().Equal(ChartType.Column, ChartType.Line, ChartType.Bar, ChartType.Pie, ChartType.Scatter);
    }

    private static Sheet CreateSheet() => new Workbook("Charts").AddSheet("Data");

    private static void Set(Sheet sheet, uint row, uint column, ScalarValue value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, column), value);

    private static GridRange Range(Sheet sheet, uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(new CellAddress(sheet.Id, startRow, startColumn), new CellAddress(sheet.Id, endRow, endColumn));
}

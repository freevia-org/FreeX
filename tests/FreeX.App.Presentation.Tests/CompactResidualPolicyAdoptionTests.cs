using FreeX.App.Presentation.Consolidate;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class CompactResidualPolicyAdoptionTests
{
    [Fact]
    public void ConsolidateAggregation_DelegatesToCommandPolicy()
    {
        var source = ReadSource("src", "FreeX.App.Presentation", "Consolidate", "ConsolidateAggregation.cs");

        source.Should().Contain("ConsolidationRules.Aggregate(values, nonEmptyCount, function)");
        source.Should().NotContain("function switch");
        ConsolidateAggregation.Aggregate([2, 4], 3, ConsolidateFunction.Count).Should().Be(3);
        ConsolidateAggregation.Aggregate([2, 4], 3, ConsolidateFunction.Average).Should().Be(3);
    }

    [Fact]
    public void ConsolidatePlanner_TreatsErrorCellsAsNonEmptyButNotNumeric()
    {
        var error = ConsolidateApplyPlanner.ToCellValue(ErrorValue.Ref);
        var source = new ConsolidateSource([[error]]);

        var count = ConsolidatePlanner.Plan([source], new ConsolidateOptions { Function = ConsolidateFunction.Count });
        var countNumbers = ConsolidatePlanner.Plan([source], new ConsolidateOptions { Function = ConsolidateFunction.CountNumbers });
        var sum = ConsolidatePlanner.Plan([source], new ConsolidateOptions { Function = ConsolidateFunction.Sum });

        count.Cells.Single().Number.Should().Be(1);
        countNumbers.Cells.Single().Number.Should().Be(0);
        sum.Cells.Single().Number.Should().Be(0);
    }

    [Fact]
    public void EveryTrendlineSanitizer_UsesCoreModelPolicyWithItsOriginalDepth()
    {
        var layout = ReadSource("src", "FreeX.Core.Commands", "SetChartLayoutCommand.Support.cs");
        var nativeJson = ReadSource("src", "FreeX.Core.IO", "NativeJsonAdapter.ChartSanitization.cs");
        var xlsx = ReadSource("src", "FreeX.Core.IO", "XlsxChartSanitizer.cs");

        layout.Should().Contain("ChartTrendlineSupportPolicy.NormalizeUnsupported");
        layout.Should().Contain("UnsupportedChartTrendlineState.LabelFormatting");
        nativeJson.Should().Contain("ChartTrendlineSupportPolicy.NormalizeUnsupported");
        nativeJson.Should().Contain("UnsupportedChartTrendlineState.ExtendedDefinition");
        xlsx.Should().Contain("ChartTrendlineSupportPolicy.NormalizeUnsupported(chart)");

        foreach (var source in new[] { layout, nativeJson, xlsx })
        {
            source.Should().NotContain("chart.ShowLinearTrendline = false");
            source.Should().NotContain("chart.TrendlineThickness = 1.5");
        }
    }

    [Fact]
    public void CommandsAndFormula_UseCoreModelPivotOccupiedRangeResolver()
    {
        var refresh = ReadSource("src", "FreeX.Core.Commands", "PivotTableRefreshService.cs");
        var formula = ReadSource("src", "FreeX.Core.Formula", "BuiltInFunctions.Pivot.cs");

        refresh.Should().Contain("PivotTableTargetRangeResolver.GetOccupiedRange(sheet, pivotTable)");
        formula.Should().Contain("PivotTableTargetRangeResolver.GetOccupiedRange(pivotSheet, pivotTable)");
        formula.Should().NotContain("GetPivotMaterializedRange");
        refresh.Should().NotContain("for (var row = pivotTable.TargetRange.Start.Row");
        formula.Should().NotContain("for (var row = pivotTable.TargetRange.Start.Row");
    }

    private static string ReadSource(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}

using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Fact]
    public void FieldCaption_FallsBackToOneBasedColumnNameWhenHeaderMissing()
    {
        PivotUiPlanner.FieldCaption(["Region", "Amount"], 1).Should().Be("Amount");
        PivotUiPlanner.FieldCaption(["Region"], 2).Should().Be("Column 3");
    }

    [Fact]
    public void FindFieldIndexes_SearchesHeadersAndDataFieldsCaseInsensitively()
    {
        var pivot = CreatePivot();
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotUiPlanner.FindSourceFieldIndex(["Region", "Quarter", "Amount"], "quarter").Should().Be(1);
        PivotUiPlanner.FindDataFieldIndex(pivot, "sum OF amount").Should().Be(0);
        PivotUiPlanner.FindFieldSourceIndex(["Region"], pivot, "Sum of Amount").Should().Be(2);
    }

    [Fact]
    public void ResolvePivotChartFieldButtonCaption_UsesValuesAxisPageOrDataFallback()
    {
        var pivot = CreatePivot();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.PageFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        var headers = new[] { "Region", "Quarter", "Channel", "Amount" };

        PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, "Values").Should().Be("Sum of Amount");
        PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, "Axis Fields").Should().Be("Region");
        PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, "Legend").Should().Be("Channel");
    }
}

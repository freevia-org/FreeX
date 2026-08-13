using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotFieldListPaneBuilderTests
{
    private static readonly string[] Headers = ["Region", "Product", "Quarter", "Amount", "Units"];

    private static PivotTableModel BuildPivot()
    {
        var pivot = new PivotTableModel { Name = "PivotTable1" };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.PageFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        return pivot;
    }

    [Fact]
    public void Build_DerivesEachBucketFromTheLayoutAreas()
    {
        var pane = PivotFieldListPaneBuilder.Build(BuildPivot(), Headers);

        pane.PivotTableName.Should().Be("PivotTable1");
        pane.Rows.Fields.Select(f => f.SourceFieldIndex).Should().Equal(0);
        pane.Columns.Fields.Select(f => f.SourceFieldIndex).Should().Equal(2);
        pane.Filters.Fields.Select(f => f.SourceFieldIndex).Should().Equal(1);
        pane.Values.Fields.Select(f => f.SourceFieldIndex).Should().Equal(3);
    }

    [Fact]
    public void Build_AvailableBucketExcludesPlacedFields()
    {
        var pane = PivotFieldListPaneBuilder.Build(BuildPivot(), Headers);

        // 0,1,2,3 are placed; only index 4 ("Units") remains available.
        pane.Available.Fields.Select(f => f.SourceFieldIndex).Should().Equal(4);
        pane.Available.Fields.Single().Caption.Should().Be("Units");
    }

    [Fact]
    public void Build_ValuesBucketCarriesDataFieldIndexAndSummaryFunction()
    {
        var pane = PivotFieldListPaneBuilder.Build(BuildPivot(), Headers);

        var value = pane.Values.Fields.Single();
        value.Caption.Should().Be("Sum of Amount");
        value.DataFieldIndex.Should().Be(0);
        value.SummaryFunction.Should().Be("sum");
        value.Bucket.Should().Be(PivotFieldBucket.Values);
    }

    [Fact]
    public void Build_AxisCaptionsComeFromHeaders()
    {
        var pane = PivotFieldListPaneBuilder.Build(BuildPivot(), Headers);

        pane.Rows.Fields.Single().Caption.Should().Be("Region");
        pane.Columns.Fields.Single().Caption.Should().Be("Quarter");
        pane.Filters.Fields.Single().Caption.Should().Be("Product");
    }

    [Fact]
    public void Build_OutOfRangeSourceIndexFallsBackToSynthesizedCaption()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.RowFields.Add(new PivotFieldModel(9));

        var pane = PivotFieldListPaneBuilder.Build(pivot, Headers);

        pane.Rows.Fields.Single().Caption.Should().Be("Column 10");
    }

    [Fact]
    public void Build_EmptyPivotPlacesAllHeadersInAvailable()
    {
        var pane = PivotFieldListPaneBuilder.Build(new PivotTableModel { Name = "P" }, Headers);

        pane.Available.Fields.Select(f => f.Caption).Should().Equal(Headers);
        pane.Rows.IsEmpty.Should().BeTrue();
        pane.Columns.IsEmpty.Should().BeTrue();
        pane.Values.IsEmpty.Should().BeTrue();
        pane.Filters.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AllBuckets_AreInDisplayOrder()
    {
        var pane = PivotFieldListPaneBuilder.Build(BuildPivot(), Headers);

        pane.AllBuckets.Select(b => b.Bucket).Should().Equal(
            PivotFieldBucket.Available,
            PivotFieldBucket.Rows,
            PivotFieldBucket.Columns,
            PivotFieldBucket.Values,
            PivotFieldBucket.Filters);
    }

    [Fact]
    public void Bucket_ReturnsTheRequestedArea()
    {
        var pane = PivotFieldListPaneBuilder.Build(BuildPivot(), Headers);

        pane.Bucket(PivotFieldBucket.Rows).Should().BeSameAs(pane.Rows);
        pane.Bucket(PivotFieldBucket.Values).Should().BeSameAs(pane.Values);
    }

    [Fact]
    public void FilterByCaption_BlankNeedleReturnsEverything()
    {
        var pane = PivotFieldListPaneBuilder.Build(new PivotTableModel { Name = "P" }, Headers);

        PivotFieldListPaneBuilder.FilterByCaption(pane.Available.Fields, "  ")
            .Should().HaveCount(Headers.Length);
    }

    [Fact]
    public void FilterByCaption_MatchesCaseInsensitiveSubstring()
    {
        var pane = PivotFieldListPaneBuilder.Build(new PivotTableModel { Name = "P" }, Headers);

        var result = PivotFieldListPaneBuilder.FilterByCaption(pane.Available.Fields, "u");

        result.Select(f => f.Caption).Should().BeEquivalentTo("Product", "Quarter", "Units", "Amount");
    }

    [Fact]
    public void BuildAvailableFields_KeepsEveryHeaderAndMarksPlacedSourceFields()
    {
        var areas = PivotFieldLayoutPlanner.Capture(BuildPivot());

        var fields = PivotFieldListPaneBuilder.BuildAvailableFields(Headers, areas);

        fields.Select(field => field.SourceFieldIndex).Should().Equal(0, 1, 2, 3, 4);
        fields.Select(field => field.Caption).Should().Equal(Headers);
        fields.Select(field => field.IsChecked).Should().Equal(true, true, true, true, false);
    }

    [Fact]
    public void FilterAvailableFields_PreservesIdentityAndCheckedState()
    {
        var fields = PivotFieldListPaneBuilder.BuildAvailableFields(
            Headers,
            PivotFieldLayoutPlanner.Capture(BuildPivot()));

        var result = PivotFieldListPaneBuilder.FilterAvailableFields(fields, "unit");

        result.Should().Equal(new PivotAvailableFieldItemModel(4, "Units", false));
    }

    [Fact]
    public void GetItemCaption_InterpretsRendererItemsWithoutRendererWrappers()
    {
        PivotFieldListPaneBuilder.GetItemCaption("Region").Should().Be("Region");
        PivotFieldListPaneBuilder.GetItemCaption(new PivotAvailableFieldItemModel(3, "Amount", true))
            .Should()
            .Be("Amount");
        PivotFieldListPaneBuilder.GetItemCaption(new PivotFieldListItemModel(3, "Sum of Amount", PivotFieldBucket.Values))
            .Should()
            .Be("Sum of Amount");
        PivotFieldListPaneBuilder.GetItemCaption(new PivotAvailableFieldItemModel(3, "  ", true))
            .Should()
            .BeNull();
    }
}

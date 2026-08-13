using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Fact]
    public void GetItemCaption_ReadsSharedFieldModelsAndIgnoresBlankCaptions()
    {
        PivotFieldListPaneBuilder.GetItemCaption("Region").Should().Be("Region");
        PivotFieldListPaneBuilder.GetItemCaption(new PivotAvailableFieldItemModel(1, "Amount", true)).Should().Be("Amount");
        PivotFieldListPaneBuilder.GetItemCaption(new PivotAvailableFieldItemModel(2, "  ", false)).Should().BeNull();
        PivotFieldListPaneBuilder.GetItemCaption(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FilterAvailableFields_ReturnsAllFieldsForBlankSearch(string? searchText)
    {
        var fields = new[]
        {
            new PivotAvailableFieldItemModel(0, "Region", true),
            new PivotAvailableFieldItemModel(1, "Amount", false)
        };

        var filtered = PivotFieldListPaneBuilder.FilterAvailableFields(fields, searchText);

        filtered.Should().Equal(fields);
    }

    [Fact]
    public void FilterAvailableFields_MatchesCaptionsCaseInsensitivelyAndPreservesCheckedState()
    {
        var fields = new[]
        {
            new PivotAvailableFieldItemModel(0, "Region", true),
            new PivotAvailableFieldItemModel(1, "Sales Amount", false),
            new PivotAvailableFieldItemModel(2, "Cost", true)
        };

        var filtered = PivotFieldListPaneBuilder.FilterAvailableFields(fields, "amount");

        filtered.Should().Equal(new PivotAvailableFieldItemModel(1, "Sales Amount", false));
    }

    [Fact]
    public void PivotFieldLayoutDraft_CapturesDeferredLayoutIntent()
    {
        var areas = new PivotFieldAreas(
            [new PivotFieldModel(0)],
            [],
            [],
            [new PivotDataFieldModel(1, "Sum of Sales Amount", "sum")]);
        var pending = new PivotFieldLayoutDraft("PivotTable1", areas);

        pending.PivotTableName.Should().Be("PivotTable1");
        pending.RowFields.Should().Equal(new PivotFieldModel(0));
        pending.DataFields.Should().Equal(new PivotDataFieldModel(1, "Sum of Sales Amount", "sum"));
    }

    [Theory]
    [InlineData(-1, new[] { "A", "B", "X" })]
    [InlineData(1, new[] { "A", "X", "B" })]
    [InlineData(3, new[] { "A", "B", "X" })]
    public void InsertOrAppend_InsertsOnlyInsideExistingListBounds(int index, string[] expected)
    {
        var items = new List<string> { "A", "B" };

        PivotUiPlanner.InsertOrAppend(items, "X", index);

        items.Should().Equal(expected);
    }
}

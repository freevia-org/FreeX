using FluentAssertions;
using Free.Shared.Localization;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotFieldFilterSummaryTests
{
    private static readonly ResourceKeyTextResolver Text = new(
        key => key switch
        {
            "PivotFieldFilter_NoItemFilter" => "No item filter",
            "PivotFieldFilter_NoLabelFilter" => "No label filter",
            "PivotFieldFilter_NoValueFilter" => "No value filter",
            _ => key
        },
        (key, _) => key);

    [Fact]
    public void CreateState_ProjectsPartialSelectionAndOwnedFilters()
    {
        var pivot = new PivotTableModel { Name = "PivotTable1" };
        pivot.RowFields.Add(new PivotFieldModel(2, SelectedItems: ["North", "South"]));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Sum of Sales", "sum"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(2, PivotLabelFilterKind.Contains, "th"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(
            0,
            PivotValueFilterKind.GreaterThan,
            ComparisonValue: 100,
            SourceFieldIndex: 2));

        var state = PivotFieldFilterSummary.CreateState(
            pivot,
            2,
            PivotHeaderArea.Row,
            "Region",
            ["North", "South", "West"],
            Text);

        state.HasStoredItemSelection.Should().BeTrue();
        state.HasItemFilter.Should().BeTrue();
        state.HasLabelFilter.Should().BeTrue();
        state.HasValueFilter.Should().BeTrue();
        state.ItemSummary.Should().Be("Item filter: 2 items (\"North\", \"South\")");
        state.OverallSummary.Should().Contain("Active filters for Region");
    }

    [Fact]
    public void CreateState_PreservesStoredSelectionSignalWhenEveryItemIsSelected()
    {
        var pivot = new PivotTableModel { Name = "PivotTable1" };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["A", "B"]));

        var state = PivotFieldFilterSummary.CreateState(
            pivot,
            0,
            PivotHeaderArea.Row,
            "Category",
            ["A", "B"],
            Text);

        state.HasStoredItemSelection.Should().BeTrue();
        state.HasItemFilter.Should().BeFalse();
        state.HasStoredFilter.Should().BeTrue();
        state.ItemSummary.Should().Be("No item filter");

        pivot.RowFields.Clear();
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItem: "(All)"));
        var allMarkerState = PivotFieldFilterSummary.CreateState(
            pivot,
            0,
            PivotHeaderArea.Row,
            "Category",
            ["A", "B"],
            Text);
        allMarkerState.HasStoredItemSelection.Should().BeTrue();
        allMarkerState.HasItemFilter.Should().BeFalse();
        allMarkerState.SelectedItems.Should().BeEmpty();
    }

    [Fact]
    public void CreateState_ReadsSelectionFromRequestedAreaWhenSourceIndexIsRepeated()
    {
        var pivot = new PivotTableModel { Name = "PivotTable1" };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItem: "Row"));
        pivot.ColumnFields.Add(new PivotFieldModel(0, SelectedItem: "Column"));

        var state = PivotFieldFilterSummary.CreateState(
            pivot,
            0,
            PivotHeaderArea.Column,
            "Category",
            ["Row", "Column"],
            Text);

        state.SelectedItems.Should().Equal("Column");
        state.HasStoredItemSelection.Should().BeTrue();
    }
}

using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Fact]
    public void FieldSelectionState_UpdatesMatchingAreaOnly()
    {
        var pivot = new PivotTableModel();
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["East"]));
        pivot.ColumnFields.Add(new PivotFieldModel(1));

        var updated = PivotUiPlanner
            .CreateFieldSelectionState(pivot, PivotHeaderArea.Column, 1)
            .WithSelectedItems(["Q1"]);

        updated.RowFields[0].SelectedItems.Should().Equal("East");
        updated.ColumnFields[0].SelectedItem.Should().Be("Q1");
        updated.ColumnFields[0].SelectedItems.Should().Equal("Q1");
    }
}

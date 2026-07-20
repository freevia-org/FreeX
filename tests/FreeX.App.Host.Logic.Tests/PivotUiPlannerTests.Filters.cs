using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Fact]
    public void SetFieldSelectedItems_UpdatesMatchingFieldOnly()
    {
        var fields = new[]
        {
            new PivotFieldModel(0, SelectedItems: ["East"]),
            new PivotFieldModel(1)
        };

        var updated = PivotUiPlanner.SetFieldSelectedItems(fields, 1, ["Q1"]);

        updated[0].SelectedItems.Should().Equal("East");
        updated[1].SelectedItem.Should().Be("Q1");
        updated[1].SelectedItems.Should().Equal("Q1");
    }
}

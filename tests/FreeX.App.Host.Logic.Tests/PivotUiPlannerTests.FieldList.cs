using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Fact]
    public void GetFieldListCaption_ReadsStringOrFieldListItemAndIgnoresBlankCaptions()
    {
        PivotUiPlanner.GetFieldListCaption("Region").Should().Be("Region");
        PivotUiPlanner.GetFieldListCaption(new PivotFieldListItem("Amount", true)).Should().Be("Amount");
        PivotUiPlanner.GetFieldListCaption(new PivotFieldListItem("  ", false)).Should().BeNull();
        PivotUiPlanner.GetFieldListCaption(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FilterPivotFieldListItems_ReturnsAllFieldsForBlankSearch(string? searchText)
    {
        var fields = new[]
        {
            new PivotFieldListItem("Region", true),
            new PivotFieldListItem("Amount", false)
        };

        var filtered = PivotUiPlanner.FilterPivotFieldListItems(fields, searchText);

        filtered.Should().Equal(fields);
    }

    [Fact]
    public void FilterPivotFieldListItems_MatchesCaptionsCaseInsensitivelyAndPreservesCheckedState()
    {
        var fields = new[]
        {
            new PivotFieldListItem("Region", true),
            new PivotFieldListItem("Sales Amount", false),
            new PivotFieldListItem("Cost", true)
        };

        var filtered = PivotUiPlanner.FilterPivotFieldListItems(fields, "amount");

        filtered.Should().Equal(new PivotFieldListItem("Sales Amount", false));
    }

    [Fact]
    public void PendingPivotLayoutUpdate_CapturesDeferredLayoutIntent()
    {
        var pending = new PendingPivotLayoutUpdate(
            IsDeferred: true,
            AvailableFieldsSearchText: "sales",
            Fields: [new PivotFieldListItem("Sales Amount", true)]);

        pending.IsDeferred.Should().BeTrue();
        pending.AvailableFieldsSearchText.Should().Be("sales");
        pending.Fields.Should().Equal(new PivotFieldListItem("Sales Amount", true));
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

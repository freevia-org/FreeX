using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotSortPlannerTests
{
    [Fact]
    public void InitialMode_DefaultsToLabelAscending_WhenNoSortOrDifferentField()
    {
        PivotSortPlanner.InitialMode(null, 0).Should().Be(PivotSortOptionMode.LabelAscending);

        var otherFieldSort = new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 5);
        PivotSortPlanner.InitialMode(otherFieldSort, 0).Should().Be(PivotSortOptionMode.LabelAscending);
    }

    [Fact]
    public void InitialMode_ReadsLabelAndValueSorts()
    {
        var labelDesc = new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 0);
        PivotSortPlanner.InitialMode(labelDesc, 0).Should().Be(PivotSortOptionMode.LabelDescending);

        var valueAsc = new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Ascending, DataFieldIndex: 1, FieldIndex: 0);
        PivotSortPlanner.InitialMode(valueAsc, 0).Should().Be(PivotSortOptionMode.ValueAscending);
    }

    [Fact]
    public void InitialValueFieldIndex_UsesCurrentValueSort_ClampedToRange()
    {
        var valueSort = new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Ascending, DataFieldIndex: 1, FieldIndex: 0);
        PivotSortPlanner.InitialValueFieldIndex(valueSort, 0, dataFieldCount: 2).Should().Be(1);
        PivotSortPlanner.InitialValueFieldIndex(valueSort, 0, dataFieldCount: 1).Should().Be(0);
        PivotSortPlanner.InitialValueFieldIndex(null, 0, dataFieldCount: 0).Should().Be(-1);
    }

    [Theory]
    [InlineData(PivotSortOptionMode.LabelAscending, false)]
    [InlineData(PivotSortOptionMode.ValueAscending, true)]
    [InlineData(PivotSortOptionMode.ValueDescending, true)]
    public void ValueFieldEnabled_OnlyForValueSortsWithData(PivotSortOptionMode mode, bool expectedWhenData)
    {
        PivotSortPlanner.ValueFieldEnabled(mode, dataFieldCount: 1).Should().Be(expectedWhenData);
        PivotSortPlanner.ValueFieldEnabled(mode, dataFieldCount: 0).Should().BeFalse();
    }

    [Fact]
    public void TryValidate_ValueSortRequiresSelectableField()
    {
        PivotSortPlanner.TryValidate(PivotSortOptionMode.ValueAscending, 0, -1, out var error).Should().BeFalse();
        error.Should().Be(PivotSortPlanner.ValueSortRequiresValueFieldMessage);

        PivotSortPlanner.TryValidate(PivotSortOptionMode.ValueAscending, 2, 1, out _).Should().BeTrue();
        PivotSortPlanner.TryValidate(PivotSortOptionMode.LabelAscending, 0, -1, out _).Should().BeTrue();
    }

    [Fact]
    public void CreateResult_BuildsLabelAndValueSorts()
    {
        var label = PivotSortPlanner.CreateResult(PivotSortOptionMode.LabelDescending, sourceFieldIndex: 3, valueFieldSelectedIndex: 0);
        label.Target.Should().Be(PivotSortTarget.Label);
        label.Direction.Should().Be(PivotSortDirection.Descending);
        label.FieldIndex.Should().Be(3);

        var value = PivotSortPlanner.CreateResult(PivotSortOptionMode.ValueAscending, sourceFieldIndex: 3, valueFieldSelectedIndex: 2);
        value.Target.Should().Be(PivotSortTarget.Value);
        value.Direction.Should().Be(PivotSortDirection.Ascending);
        value.DataFieldIndex.Should().Be(2);
        value.FieldIndex.Should().Be(3);
    }

    [Fact]
    public void ReplaceFieldSort_ReplacesOnlyTheFieldsSort()
    {
        var existing = new List<PivotSortModel>
        {
            new(PivotSortTarget.Label, PivotSortDirection.Ascending, FieldIndex: 0),
            new(PivotSortTarget.Label, PivotSortDirection.Ascending, FieldIndex: 1),
        };

        var newSort = new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0, FieldIndex: 0);
        var result = PivotSortPlanner.ReplaceFieldSort(existing, newSort);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(sort => sort.FieldIndex == 1);
        result.Single(sort => sort.FieldIndex == 0).Should().Be(newSort);
    }

    [Fact]
    public void ReplaceQuickSort_OwnsLabelAndValueReplacementPolicy()
    {
        var existing = new List<PivotSortModel>
        {
            new(PivotSortTarget.Label, PivotSortDirection.Ascending, FieldIndex: 0),
            new(PivotSortTarget.Value, PivotSortDirection.Ascending, DataFieldIndex: 1, FieldIndex: 2),
            new(PivotSortTarget.Label, PivotSortDirection.Ascending, FieldIndex: 3),
        };

        var labelResult = PivotSortPlanner.ReplaceQuickSort(
            existing,
            sourceFieldIndex: 0,
            dataFieldIndex: null,
            axisFieldIndex: 2,
            direction: PivotSortDirection.Descending);
        var valueResult = PivotSortPlanner.ReplaceQuickSort(
            existing,
            sourceFieldIndex: null,
            dataFieldIndex: 1,
            axisFieldIndex: 4,
            direction: PivotSortDirection.Descending);

        labelResult.Should().HaveCount(3);
        labelResult.Should().ContainSingle(sort =>
            sort.Target == PivotSortTarget.Label &&
            sort.FieldIndex == 0 &&
            sort.Direction == PivotSortDirection.Descending);
        valueResult.Should().HaveCount(3);
        valueResult.Should().ContainSingle(sort =>
            sort.Target == PivotSortTarget.Value &&
            sort.DataFieldIndex == 1 &&
            sort.FieldIndex == 4 &&
            sort.Direction == PivotSortDirection.Descending);
    }
}

using System.Globalization;

using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotGroupFieldPlannerTests
{
    private static readonly SheetId Sheet = new(Guid.NewGuid());

    private static PivotTableModel PivotWithRowField(int sourceFieldIndex, PivotFieldModel? field = null)
    {
        var pivot = new PivotTableModel
        {
            Name = "P",
            SourceRange = new GridRange(new CellAddress(Sheet, 1, 1), new CellAddress(Sheet, 5, 4)),
        };
        pivot.RowFields.Add(field ?? new PivotFieldModel(sourceFieldIndex));
        return pivot;
    }

    [Fact]
    public void Groupings_CoverAllCoreModes()
    {
        PivotGroupFieldPlanner.Groupings.Select(option => option.Value)
            .Should().Equal(
                PivotFieldGrouping.None,
                PivotFieldGrouping.Year,
                PivotFieldGrouping.Quarter,
                PivotFieldGrouping.Month,
                PivotFieldGrouping.Day,
                PivotFieldGrouping.NumberRange);
    }

    [Fact]
    public void GroupingUsesNumberRange_OnlyForNumberRange()
    {
        PivotGroupFieldPlanner.GroupingUsesNumberRange(PivotFieldGrouping.NumberRange).Should().BeTrue();
        PivotGroupFieldPlanner.GroupingUsesNumberRange(PivotFieldGrouping.Month).Should().BeFalse();
    }

    [Fact]
    public void FindLayoutField_ReturnsTheExistingFieldGrouping()
    {
        var existing = new PivotFieldModel(2, Grouping: PivotFieldGrouping.Month);
        var pivot = PivotWithRowField(2, existing);
        PivotGroupFieldPlanner.FindLayoutField(pivot, 2).Should().Be(existing);
        PivotGroupFieldPlanner.FindLayoutField(pivot, 3).Should().BeNull();
    }

    [Fact]
    public void TryValidate_SkipsNumericChecksWhenUngrouping()
    {
        var ok = PivotGroupFieldPlanner.TryValidate(
            PivotFieldGrouping.NumberRange, ungroup: true, "x", "y", "z",
            out var start, out var end, out var interval, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        start.Should().BeNull();
        end.Should().BeNull();
        interval.Should().BeNull();
    }

    [Fact]
    public void TryValidate_RejectsNonNumericStart()
    {
        var ok = PivotGroupFieldPlanner.TryValidate(
            PivotFieldGrouping.NumberRange, ungroup: false, "abc", "10", "2",
            out _, out _, out _, out var error);
        ok.Should().BeFalse();
        error.Should().Be(PivotGroupFieldPlanner.InvalidStartMessage);
    }

    [Fact]
    public void TryValidate_RejectsNonPositiveIntervalForNumberRange()
    {
        var ok = PivotGroupFieldPlanner.TryValidate(
            PivotFieldGrouping.NumberRange, ungroup: false, "0", "100", "0",
            out _, out _, out _, out var error);
        ok.Should().BeFalse();
        error.Should().Be(PivotGroupFieldPlanner.InvalidIntervalMessage);
    }

    [Fact]
    public void TryValidate_ParsesNumberRangeBoundsAndInterval()
    {
        var ok = PivotGroupFieldPlanner.TryValidate(
            PivotFieldGrouping.NumberRange, ungroup: false, "0", "100", "10",
            out var start, out var end, out var interval, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        start.Should().Be(0);
        end.Should().Be(100);
        interval.Should().Be(10);
    }

    [Fact]
    public void TryValidate_AllowsBlankBoundsForDateGrouping()
    {
        var ok = PivotGroupFieldPlanner.TryValidate(
            PivotFieldGrouping.Month, ungroup: false, "", "", "",
            out var start, out var end, out var interval, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        start.Should().BeNull();
        end.Should().BeNull();
        interval.Should().BeNull();
    }

    [Fact]
    public void CreateField_ClearsGroupingWhenUngrouping()
    {
        var field = PivotGroupFieldPlanner.CreateField(
            3, PivotFieldGrouping.Month, ungroup: true, 1, 2, 3);
        field.SourceFieldIndex.Should().Be(3);
        field.Grouping.Should().Be(PivotFieldGrouping.None);
        field.GroupStart.Should().BeNull();
        field.GroupEnd.Should().BeNull();
        field.GroupInterval.Should().BeNull();
    }

    [Fact]
    public void CreateField_BuildsNumberRangeWithPositiveInterval()
    {
        var field = PivotGroupFieldPlanner.CreateField(
            1, PivotFieldGrouping.NumberRange, ungroup: false, 0, 100, 25);
        field.Grouping.Should().Be(PivotFieldGrouping.NumberRange);
        field.GroupStart.Should().Be(0);
        field.GroupEnd.Should().Be(100);
        field.GroupInterval.Should().Be(25);
    }

    [Fact]
    public void CreateField_DefaultsNumberRangeIntervalToAtLeastOne()
    {
        var field = PivotGroupFieldPlanner.CreateField(
            1, PivotFieldGrouping.NumberRange, ungroup: false, null, null, null);
        field.GroupInterval.Should().Be(1);
    }

    [Fact]
    public void BuildLayout_ReplacesAnExistingLayoutField()
    {
        var pivot = PivotWithRowField(2, new PivotFieldModel(2, Grouping: PivotFieldGrouping.None));
        var grouped = PivotGroupFieldPlanner.CreateField(2, PivotFieldGrouping.Year, ungroup: false, null, null, null);

        var layout = PivotGroupFieldPlanner.BuildLayout(pivot, grouped);

        layout.RowFields.Should().HaveCount(1);
        layout.RowFields[0].Grouping.Should().Be(PivotFieldGrouping.Year);
    }

    [Fact]
    public void BuildLayout_AppendsToRowsWhenFieldNotYetPlaced()
    {
        var pivot = new PivotTableModel
        {
            Name = "P",
            SourceRange = new GridRange(new CellAddress(Sheet, 1, 1), new CellAddress(Sheet, 5, 4)),
        };
        var grouped = PivotGroupFieldPlanner.CreateField(1, PivotFieldGrouping.Quarter, ungroup: false, null, null, null);

        var layout = PivotGroupFieldPlanner.BuildLayout(pivot, grouped);

        layout.RowFields.Should().ContainSingle(field => field.SourceFieldIndex == 1);
        layout.ColumnFields.Should().BeEmpty();
        layout.PageFields.Should().BeEmpty();
    }

    [Fact]
    public void FormatBound_UsesCurrentCultureAndBlankForNull()
    {
        PivotGroupFieldPlanner.FormatBound(null).Should().BeEmpty();
        PivotGroupFieldPlanner.FormatBound(12.5).Should().Be(12.5.ToString("G", CultureInfo.CurrentCulture));
    }
}

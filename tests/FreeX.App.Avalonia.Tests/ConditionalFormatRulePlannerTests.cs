using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests for conditional-format rule applicability and ordering: which rules apply to a cell, in
/// the engine's priority order, and the stop-if-true short-circuit contract.
/// </summary>
public sealed class ConditionalFormatRulePlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    private static CellAddress At(uint row, uint col) => new(Sheet, row, col);

    private static GridRange Range(uint r1, uint c1, uint r2, uint c2) =>
        new(At(r1, c1), At(r2, c2));

    private static ConditionalFormat Rule(GridRange appliesTo, int priority, bool stopIfTrue = false) =>
        new() { AppliesTo = appliesTo, Priority = priority, StopIfTrue = stopIfTrue };

    [Fact]
    public void OrderApplicableRules_ExcludesRulesOutsideRange()
    {
        var inside = Rule(Range(1, 1, 5, 5), priority: 1);
        var outside = Rule(Range(10, 10, 12, 12), priority: 1);

        var ordered = ConditionalFormatRulePlanner.OrderApplicableRules([inside, outside], At(3, 3));

        ordered.Should().ContainSingle().Which.Should().BeSameAs(inside);
    }

    [Fact]
    public void OrderApplicableRules_SortsByPriorityAscending()
    {
        var low = Rule(Range(1, 1, 5, 5), priority: 3);
        var high = Rule(Range(1, 1, 5, 5), priority: 1);
        var mid = Rule(Range(1, 1, 5, 5), priority: 2);

        var ordered = ConditionalFormatRulePlanner.OrderApplicableRules([low, high, mid], At(2, 2));

        ordered.Should().Equal(high, mid, low);
    }

    [Fact]
    public void OrderApplicableRules_BreaksTiesByInsertionOrder()
    {
        var first = Rule(Range(1, 1, 5, 5), priority: 1);
        var second = Rule(Range(1, 1, 5, 5), priority: 1);

        var ordered = ConditionalFormatRulePlanner.OrderApplicableRules([first, second], At(2, 2));

        ordered.Should().Equal(first, second);
    }

    [Fact]
    public void ShouldStopAfter_ReflectsStopIfTrueFlag()
    {
        ConditionalFormatRulePlanner.ShouldStopAfter(Rule(Range(1, 1, 1, 1), 1, stopIfTrue: true))
            .Should().BeTrue();
        ConditionalFormatRulePlanner.ShouldStopAfter(Rule(Range(1, 1, 1, 1), 1, stopIfTrue: false))
            .Should().BeFalse();
    }

    [Fact]
    public void OrderApplicableRules_NullRules_Throws()
    {
        var act = () => ConditionalFormatRulePlanner.OrderApplicableRules(null!, At(1, 1));
        act.Should().Throw<ArgumentNullException>();
    }
}

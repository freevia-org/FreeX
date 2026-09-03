using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r253: the behaviour <see cref="WorksheetAutoFilterColumnComparison"/> exists for.
///
/// <para>These pin the gap between record equality and content equality directly, on the models,
/// rather than through a command. That distinction matters: <c>AverageFilterCommand</c> -- the first
/// command to use the comparison -- happens to build its column model entirely from EMPTY collection
/// expressions, and an empty <c>[]</c> targeting an interface lowers to the cached
/// <c>Array.Empty&lt;T&gt;()</c> singleton, so its two applications really are reference-equal member
/// for member and record equality would have sufficed. Every remaining AutoFilter command builds
/// NON-empty collections -- a value list, custom filter criteria, date groups -- and for those
/// record equality is wrong. So the load-bearing case is proved here, where it is real, instead of
/// being claimed at a call site that does not exercise it.</para>
/// </summary>
public sealed class R253_AutoFilterColumnComparisonTests
{
    private static WorksheetAutoFilterColumnModel WithValues(params string[] values) =>
        new(ColumnId: 0, Values: [.. values], IncludeBlank: false, NativeFilterXmls: [], NativeAttributes: null);

    [Fact]
    public void RecordEqualityIsWrongForTwoIdenticallyBuiltValueFilters()
    {
        var left = WithValues("North", "South");
        var right = WithValues("North", "South");

        (left == right).Should().BeFalse(
            "this is the premise of the whole comparison: record equality compares the Values list "
            + "with EqualityComparer<T>.Default, i.e. by reference, and these are two distinct lists");
        left.SameAs(right).Should().BeTrue("their content is identical");
    }

    [Fact]
    public void DifferentValueListsAreNotTheSame()
    {
        WithValues("North", "South").SameAs(WithValues("North", "West")).Should().BeFalse();
        WithValues("North", "South").SameAs(WithValues("North")).Should().BeFalse();
        WithValues("North", "South").SameAs(WithValues("South", "North")).Should().BeFalse(
            "filterColumn children round-trip in document order, so a reordering is a real change");
    }

    [Fact]
    public void ScalarDifferencesAreCaughtByTheRecordEqualityHalf()
    {
        var left = WithValues("North") with { IncludeBlank = false };
        var right = WithValues("North") with { IncludeBlank = true, Values = left.Values };

        left.SameAs(right).Should().BeFalse(
            "IncludeBlank is not stripped, so record equality covers it -- and must");
    }

    [Fact]
    public void NestedModelsAreComparedByContentNotByReference()
    {
        var left = WithValues("x") with
        {
            DynamicFilter = new WorksheetAutoFilterDynamicFilterModel(
                Type: "aboveAverage",
                NativeAttributes: new Dictionary<string, string> { ["val"] = "7" }),
        };
        var same = left with
        {
            DynamicFilter = new WorksheetAutoFilterDynamicFilterModel(
                Type: "aboveAverage",
                NativeAttributes: new Dictionary<string, string> { ["val"] = "7" }),
        };
        var different = left with
        {
            DynamicFilter = new WorksheetAutoFilterDynamicFilterModel(
                Type: "belowAverage",
                NativeAttributes: new Dictionary<string, string> { ["val"] = "7" }),
        };
        var differentAttributes = left with
        {
            DynamicFilter = new WorksheetAutoFilterDynamicFilterModel(
                Type: "aboveAverage",
                NativeAttributes: new Dictionary<string, string> { ["val"] = "8" }),
        };

        left.SameAs(same).Should().BeTrue();
        left.SameAs(different).Should().BeFalse("a different dynamic-filter type is a different filter");
        left.SameAs(differentAttributes).Should().BeFalse(
            "the preserved native attributes round-trip into the saved file, so they are content too");
    }

    [Fact]
    public void CustomFilterCriteriaAreComparedByContent()
    {
        var left = WithValues() with
        {
            CustomFilters = [new WorksheetAutoFilterCustomFilterModel("greaterThan", "100")],
        };
        var same = WithValues() with
        {
            CustomFilters = [new WorksheetAutoFilterCustomFilterModel("greaterThan", "100")],
        };
        var different = WithValues() with
        {
            CustomFilters = [new WorksheetAutoFilterCustomFilterModel("lessThan", "100")],
        };

        left.SameAs(same).Should().BeTrue();
        left.SameAs(different).Should().BeFalse();
    }

    [Fact]
    public void NullsAndMissingEntriesAreDistinguished()
    {
        WorksheetAutoFilterColumnModel? absent = null;

        absent.SameAs(null).Should().BeTrue();
        absent.SameAs(WithValues("x")).Should().BeFalse();
        WithValues("x").SameAs(null).Should().BeFalse();
    }
}

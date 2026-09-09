using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 578: a pivot field's numeric-range grouping bounds (<c>rangePr/@startNum</c>,
/// <c>@endNum</c>, <c>@groupInterval</c>) are read straight out of the file. The bucket maths
/// guarded the interval with <c>if (interval &lt;= 0) interval = 1;</c> -- the REJECT form (r551),
/// which rejects NEITHER Infinity nor NaN, because every comparison with NaN is false.
/// <para>
/// The consequence is total rather than partial. With a NaN interval,
/// <c>start + Math.Floor((number - start) / interval) * interval</c> is NaN; with an INFINITE one,
/// <c>Math.Floor(x / Infinity)</c> is 0 and <c>0 * Infinity</c> is NaN as well. So every value in
/// the field lands in one bucket, and its label is the literal text "NaN-NaN".
/// </para>
/// </summary>
public sealed class R578_PivotNumberRangeGroupingTests
{
    private static string Bucket(double value, double? start, double? end, double? interval) =>
        PivotTableRefreshService.GroupKeyText(
            new NumberValue(value), PivotFieldGrouping.NumberRange, start, end, interval);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteInterval_DoesNotCollapseEveryValueIntoANaNBucket(double interval)
    {
        var low = Bucket(5, 0, null, interval);
        var high = Bucket(95, 0, null, interval);

        low.Should().NotContain("NaN");
        high.Should().NotContain("NaN");
        low.Should().NotBe(high, "distinct values must not share one bucket");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteStart_DoesNotProduceANaNBucketLabel(double start)
    {
        Bucket(5, start, null, 10).Should().NotContain("NaN");
    }

    [Theory]
    [InlineData(0, 10, "0-9")]
    [InlineData(15, 10, "10-19")]
    [InlineData(95, 10, "90-99")]
    public void FiniteBounds_StillBucketTheWayExcelLabelsThem(double value, double interval, string expected)
    {
        // Non-vacuity: the guard must not have changed the ordinary integer-interval labels, which
        // are Excel's inclusive "0-9"/"10-19" form.
        Bucket(value, 0, null, interval).Should().Be(expected);
    }

    [Fact]
    public void ZeroInterval_StillFallsBackToOne_AsItAlreadyDid()
        => Bucket(5, 0, null, 0).Should().Be("5-5");
}

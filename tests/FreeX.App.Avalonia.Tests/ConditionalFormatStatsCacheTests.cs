using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests for the per-render-pass conditional-format statistics cache, ensuring per-range aggregates
/// are built once and reused across the cells of a range (no per-cell recompute).
/// </summary>
public sealed class ConditionalFormatStatsCacheTests
{
    [Fact]
    public void GetOrAdd_BuildsOncePerRange()
    {
        var cache = new ConditionalFormatStatsCache();
        var calls = 0;

        IEnumerable<double> Factory()
        {
            calls++;
            return [1d, 2d, 3d, 4d];
        }

        var first = cache.GetOrAdd("A1:A4", Factory);
        var second = cache.GetOrAdd("A1:A4", Factory);

        calls.Should().Be(1, "the value factory must run only on a cache miss");
        second.Should().BeSameAs(first);
        cache.BuiltRangeCount.Should().Be(1);
        first.Min.Should().Be(1d);
        first.Max.Should().Be(4d);
        first.Average.Should().Be(2.5d);
    }

    [Fact]
    public void GetOrAdd_DistinctRanges_BuildSeparately()
    {
        var cache = new ConditionalFormatStatsCache();

        var a = cache.GetOrAdd("A1:A2", () => [0d, 10d]);
        var b = cache.GetOrAdd("B1:B2", () => [5d, 25d]);

        cache.BuiltRangeCount.Should().Be(2);
        a.Max.Should().Be(10d);
        b.Max.Should().Be(25d);
    }

    [Fact]
    public void GetOrAdd_NullArguments_Throw()
    {
        var cache = new ConditionalFormatStatsCache();

        var actKey = () => cache.GetOrAdd(null!, () => []);
        var actFactory = () => cache.GetOrAdd("k", null!);

        actKey.Should().Throw<ArgumentNullException>();
        actFactory.Should().Throw<ArgumentNullException>();
    }
}

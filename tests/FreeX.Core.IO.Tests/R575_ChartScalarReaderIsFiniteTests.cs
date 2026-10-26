using FluentAssertions;

using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r575: <see cref="XlsxChartScalarReader.ReadOptionalDouble"/> is the shared helper behind 68 call
/// sites across the chart readers, every one reading an attribute out of a chart part. It feeds
/// manual-layout X/Y/W/H (where a chart element is positioned), the chart's page margins
/// (left/right/top/bottom/header/footer) and trendline forward/backward periods -- all geometry,
/// all file-controlled, and none of it guarded.
///
/// <para>Surfaced by the r565 census and named in r574, which then went off to fix the tripwire
/// that should have found it and left this one open. Recording that here because "identified in a
/// previous round" is exactly how a site stays unfixed indefinitely.</para>
/// </summary>
public sealed class R575_ChartScalarReaderIsFiniteTests
{
    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    public void A_chart_scalar_that_is_not_finite_reads_as_absent(string hostile)
    {
        // The helper's contract is already "return null when the attribute cannot be read", and
        // every caller treats null as "not specified". A value that cannot be used takes that path.
        XlsxChartScalarReader.ReadOptionalDouble(hostile).Should().BeNull();
    }

    [Fact]
    public void An_ordinary_chart_scalar_is_still_read()
    {
        // Non-vacuity: a guard that returned null for everything would satisfy the assertions above
        // while silently discarding every manual layout, page margin and trendline period in the
        // workbook.
        XlsxChartScalarReader.ReadOptionalDouble("0.25").Should().Be(0.25);
        XlsxChartScalarReader.ReadOptionalDouble("-1.5").Should().Be(-1.5);
        XlsxChartScalarReader.ReadOptionalDouble("0").Should().Be(0);
    }

    [Fact]
    public void Unreadable_text_still_reads_as_absent()
    {
        // Pins the pre-existing contract the fix reuses, so the two paths cannot be separated.
        XlsxChartScalarReader.ReadOptionalDouble("not a number").Should().BeNull();
        XlsxChartScalarReader.ReadOptionalDouble(null).Should().BeNull();
    }
}

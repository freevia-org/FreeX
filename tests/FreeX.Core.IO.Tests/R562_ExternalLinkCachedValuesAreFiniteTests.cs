using System.Xml.Linq;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r562: an external link's cached sheet data (<c>xl/externalLinks/externalLink1.xml</c>) holds the
/// last values Excel saw in the OTHER workbook, and <see cref="ExternalLinkModel.ParseSheetDataSet"/>
/// turns each into a <see cref="NumberValue"/> directly. That path bypasses the formula evaluator
/// entirely, which matters because the rest of this codebase relies on the opposite: r552 recorded
/// that "cell values cannot be non-finite -- the evaluator returns #NUM! instead", and reasoned about
/// a neighbouring parse site on that basis.
///
/// <para>A cached value of "1e400" parses successfully to Infinity and was stored as a number, so a
/// crafted or broken external link could put a non-finite value into the workbook model through the
/// one door that does not go past the evaluator. The remedy is the parser's own contract: it already
/// returns null for a cached cell it cannot read, and the caller already skips nulls.</para>
/// </summary>
public sealed class R562_ExternalLinkCachedValuesAreFiniteTests
{
    private static XElement SheetDataSet(string cachedValue) =>
        XElement.Parse(
            "<sheetDataSet>"
            + "<sheetData sheetId=\"0\">"
            + "<row r=\"1\">"
            + "<cell r=\"A1\"><v>" + cachedValue + "</v></cell>"
            + "<cell r=\"B1\"><v>42</v></cell>"
            + "</row>"
            + "</sheetData>"
            + "</sheetDataSet>");

    private static ScalarValue? CachedValueAt(string cachedValue, uint row, uint col)
    {
        var sheets = ExternalLinkModel.ParseSheetDataSet(SheetDataSet(cachedValue));
        return sheets.Count == 0 || !sheets[0].Values.TryGetValue((row, col), out var value)
            ? null
            : value;
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void A_cached_value_that_is_not_finite_does_not_enter_the_model(string hostile)
    {
        var value = CachedValueAt(hostile, 1, 1);

        // Either dropped, or kept as something that is not a non-finite number. The assertion is
        // about what the MODEL may hold, not about which substitute the parser chooses.
        (value is not NumberValue number || double.IsFinite(number.Value))
            .Should().BeTrue("the cached value became " + value);
    }

    [Fact]
    public void An_unusable_cached_value_does_not_discard_its_neighbours()
    {
        // Dropping one cell must not abandon the rest of the row: the caller already skips nulls
        // per cell, and this pins that the fix keeps that granularity.
        CachedValueAt("1e400", 1, 2).Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(42);
    }

    [Fact]
    public void Ordinary_cached_values_are_still_read()
    {
        // Non-vacuity: a guard that dropped every cached number would satisfy the assertion above
        // while silently emptying every external link's cache.
        CachedValueAt("2.5", 1, 1).Should().BeOfType<NumberValue>().Which.Value.Should().Be(2.5);
        CachedValueAt("-17", 1, 1).Should().BeOfType<NumberValue>().Which.Value.Should().Be(-17);
    }
}

using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 578: <see cref="XlsxXmlAttributeReader.ReadDoubleAttribute"/> is the single door 33 xlsx
/// attribute reads take, and it admitted non-finite values. All 33 read a BOUNDED quantity — page
/// margins, pivot group start/end/interval, filter comparison values, top-10 counts,
/// <c>calcPr/@iterateDelta</c> — so none of them has a use for one.
/// <para>
/// Two of those sites are twins of r577 defects fixed on their other side: <c>@iterateDelta</c> is
/// the FILE side of the convergence threshold <c>CalculationOptionsInputParser</c> guards when it is
/// typed, and the pivot group bounds feed the numeric-range bucket maths whose own reject form
/// (<c>interval &lt;= 0</c>) is fixed alongside this.
/// </para>
/// </summary>
public sealed class R578_XmlAttributeDoubleGuardTests
{
    private static double? Read(string attributeValue)
    {
        var element = new XElement("pageMargins", new XAttribute("left", attributeValue));
        return XlsxXmlAttributeReader.ReadDoubleAttribute(element, "left");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    public void NonFiniteAttribute_ReadsAsAbsent(string attributeValue)
    {
        var value = Read(attributeValue);

        value.Should().BeNull($"\"{attributeValue}\" is not a bounded quantity; it read as {value}");
    }

    [Theory]
    [InlineData("0.75", 0.75)]
    [InlineData("0", 0)]
    [InlineData("-1.5", -1.5)]
    [InlineData("1E4", 10000)]
    public void FiniteAttribute_StillReadsThrough(string attributeValue, double expected)
    {
        // Non-vacuity: the guard must not have made every attribute read as absent. The "1E4" case
        // matters on its own — exponent notation is legitimate here and must survive.
        Read(attributeValue).Should().Be(expected);
    }

    [Fact]
    public void MissingAttribute_StillReadsAsAbsent()
        => XlsxXmlAttributeReader.ReadDoubleAttribute(new XElement("pageMargins"), "left")
            .Should().BeNull();

    // ---- The twin this fix would otherwise have created -----------------------------------------
    // XlsxPivotTableReader.ReadNativePivotFilterDoubleValue fills the SAME
    // PivotValueFilterModel.ComparisonValue that ReadPivotValueFilters fills through
    // ReadDoubleAttribute above; the two differ only in which attribute names the x14/native shape
    // uses. Guarding the shared door alone would have left this bespoke reader as the single
    // remaining way in -- a fix creating the very asymmetry it was meant to end.

    private static double? ReadNativeFilterValue(string attributeValue)
    {
        var filter = new XElement("filter", new XAttribute("value1", attributeValue));
        return XlsxPivotTableReader.ReadNativePivotFilterDoubleValue(filter, "stringValue1", "value1", "val");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void NativePivotFilterValue_NonFinite_ReadsAsAbsent(string attributeValue)
        => ReadNativeFilterValue(attributeValue).Should().BeNull(
            "a non-finite comparison value makes every ordering test false, hiding every row");

    [Fact]
    public void NativePivotFilterValue_Finite_StillReadsThrough()
    {
        // Non-vacuity: the guard must not have made every native filter value read as absent.
        ReadNativeFilterValue("12.5").Should().Be(12.5);
    }
}

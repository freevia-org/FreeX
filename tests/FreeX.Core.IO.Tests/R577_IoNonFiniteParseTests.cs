using FluentAssertions;
using System.Xml.Linq;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 577's two remaining FreeX.Core.IO findings. Both read a value out of
/// the FILE with <c>NumberStyles.Float</c>, which accepts the literal "NaN"/"Infinity" spellings and
/// returns <c>true</c> with +/-Infinity on magnitude overflow.
/// </summary>
public sealed class R577_IoNonFiniteParseTests
{
    private static Func<ScalarValue, bool> Matcher(string op, string value)
    {
        var column = new WorksheetAutoFilterColumnModel(ColumnId: 0, Values: [])
        {
            CustomFilters = [new WorksheetAutoFilterCustomFilterModel(op, value)],
        };

        XlsxWorksheetAutoFilterCustomFilterMatcher.TryCreate(column, out var matcher)
            .Should().BeTrue();
        return matcher!;
    }

    // ---- XlsxWorksheetAutoFilterCustomFilterMatcher -------------------------------------------
    // The IO-side twin of PersistedCustomFilterCriterion.Matches in FreeX.Core.Commands: two
    // implementations of one rule, so the defect existed in both and had to be fixed in both. A NaN
    // threshold makes every ordering comparison false (hiding every row) and makes notEqual true
    // for every row.

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void CustomFilter_NonFiniteThreshold_DoesNotCompareNumerically(string value)
    {
        // With the pattern treated as text, a numeric cell simply does not equal it.
        Matcher("greaterThan", value)(new NumberValue(42)).Should().BeFalse(
            $"42 must not be reported greater than \"{value}\"");
        Matcher("lessThan", value)(new NumberValue(42)).Should().BeFalse(
            $"42 must not be reported less than \"{value}\"");
    }

    [Fact]
    public void CustomFilter_FiniteThreshold_StillComparesNumerically()
    {
        // Non-vacuity: the guard must not have stopped numeric patterns comparing as numbers.
        Matcher("greaterThan", "10")(new NumberValue(42)).Should().BeTrue();
        Matcher("greaterThan", "100")(new NumberValue(42)).Should().BeFalse();
    }

    // ---- XlsxWorksheetScenarioMapper.ParseValue ------------------------------------------------
    // A scenario's input value is APPLIED to cells, so this is another door into the model
    // invariant defended at r562, r563, r569 and r576.

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    public void ScenarioValue_NonFiniteRaw_DoesNotBecomeANumber(string raw)
    {
        var value = XlsxWorksheetScenarioMapper.ParseValue(raw);

        value.Should().BeOfType<TextValue>();
        if (value is NumberValue number)
            double.IsFinite(number.Value).Should().BeTrue($"\"{raw}\" produced {number.Value}");
    }

    [Fact]
    public void ScenarioValue_FiniteRaw_StillBecomesANumber()
    {
        // Non-vacuity: the guard must not have turned every scenario value into text.
        XlsxWorksheetScenarioMapper.ParseValue("1234.5")
            .Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(1234.5);
    }

    // ---- XlsxStructuredTableStyleMetadataReader.ReadDifferentialStyleDiff ----------------------
    // A table style's dxf font size, read with no bound at all while five other files in this
    // codebase already carry the named rule for exactly that value (IsSupportedFontSize,
    // >= 1 && <= 409, Excel's own range). The <= 409 upper bound is what excludes Infinity; NaN
    // fails both comparisons.

    private static StyleDiff? ReadTableStyleFont(string sizeText)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var dxf = new XElement(ns + "dxf",
            new XElement(ns + "font",
                new XElement(ns + "sz", new XAttribute("val", sizeText))));

        return XlsxStructuredTableStyleMetadataReader.ReadDifferentialStyleDiff(
            dxf, ns, WorkbookTheme.Office, new WorkbookIndexedColorPalette());
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("NaN")]
    [InlineData("0")]
    [InlineData("500")]
    public void TableStyleFontSize_OutOfExcelsRange_IsIgnored(string sizeText)
    {
        var diff = ReadTableStyleFont(sizeText);

        if (diff?.FontSize is { } size)
        {
            double.IsFinite(size).Should().BeTrue($"\"{sizeText}\" produced the font size {size}");
            size.Should().BeInRange(1, 409);
        }
    }

    [Fact]
    public void TableStyleFontSize_InRange_StillReadsThrough()
    {
        // Non-vacuity: without this the theory above would pass on a reader that ignored every size.
        ReadTableStyleFont("14")?.FontSize.Should().Be(14);
    }
}

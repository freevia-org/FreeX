using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R44-io-external-links-3-1: a chart series value formula that references an external workbook
/// (Excel's numeric-index bracket form <c>[1]Sheet1!$B$2:$B$6</c>, or the literal book-name form
/// <c>[Budget.xlsx]Sheet1!$B$2:$B$6</c>) must never be silently reinterpreted as a same-workbook
/// local-sheet range. Doing so previously caused FreeX to rebind the chart's series to unrelated
/// local cells on save, discarding the external link entirely. TryParseFormulaRange must report
/// these formulas as unparsable so the verbatim-formula round-trip path preserves them.
/// </summary>
public sealed class XlsxChartSeriesRangeReaderTests_ExternalLink
{
    private static XElement ParseSeries(string xml) => XElement.Parse(xml);

    [Fact]
    public void TryParseFormulaRange_NumericIndexExternalReference_ReturnsFalse()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "[1]Sheet1!$B$2:$B$6",
            sheetId,
            out var range);

        parsed.Should().BeFalse(
            "a bracketed external-workbook reference must not be reinterpreted as a local-sheet range");
        range.Should().Be(default(GridRange));
    }

    [Fact]
    public void TryParseFormulaRange_NamedExternalWorkbookReference_ReturnsFalse()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "[Budget.xlsx]Sheet1!$B$2:$B$6",
            sheetId,
            out _);

        parsed.Should().BeFalse("the literal book-name bracket form is also an external reference");
    }

    [Fact]
    public void TryParseFormulaRange_NumericIndexExternalReference_WithSheetNameResolver_ReturnsFalse()
    {
        // Even when a sheet-name resolver is supplied (cross-sheet chart lookup), the bracketed
        // prefix must short-circuit before any resolver lookup is attempted.
        var sheetId = new SheetId(Guid.NewGuid());
        var resolver = new Dictionary<string, SheetId>(StringComparer.OrdinalIgnoreCase);

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "[1]Sheet1!$B$2:$B$6",
            sheetId,
            resolver,
            out _);

        parsed.Should().BeFalse();
    }

    [Fact]
    public void HasUnparsableFormula_SeriesWithExternalWorkbookValRef_ReturnsTrue()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var seriesXml = """
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:val>
                <c:numRef>
                  <c:f>[1]Sheet1!$B$2:$B$6</c:f>
                  <c:numCache>
                    <c:ptCount val="5"/>
                    <c:pt idx="0"><c:v>1</c:v></c:pt>
                  </c:numCache>
                </c:numRef>
              </c:val>
            </c:ser>
            """;

        XlsxChartSeriesRangeReader.HasUnparsableFormula(ParseSeries(seriesXml), sheetId)
            .Should().BeTrue("an external-workbook series formula must trigger the verbatim round-trip path");
    }

    [Fact]
    public void TryCollectVerbatimFormulas_SeriesWithExternalWorkbookValRef_PreservesOriginalFormulaText()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        const string externalFormula = "[1]Sheet1!$B$2:$B$6";
        var seriesXml = $"""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:val>
                <c:numRef>
                  <c:f>{externalFormula}</c:f>
                  <c:numCache>
                    <c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>42</c:v></c:pt>
                  </c:numCache>
                </c:numRef>
              </c:val>
            </c:ser>
            """;

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas(
            [ParseSeries(seriesXml)],
            sheetId);

        result.Should().NotBeNull(
            "the presence of an unparsable (external-link) formula must engage the verbatim bypass");
        result!.Should().HaveCount(1);
        result[0].SeriesIndex.Should().Be(0);
        result[0].ValFormula.Should().Be(externalFormula,
            "the raw external-link formula text (including the bracketed workbook reference) must be preserved verbatim so the writer can round-trip it instead of regenerating a local-sheet <c:f>");
    }

    [Fact]
    public void TryParseFormulaRange_OrdinaryLocalSheetRange_StillParsesSuccessfully()
    {
        // Sibling no-regression: a normal, non-bracketed same-sheet range must keep parsing fine.
        var sheetId = new SheetId(Guid.NewGuid());

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "Sheet1!$B$2:$B$6",
            sheetId,
            out var range);

        parsed.Should().BeTrue();
        range.Start.Sheet.Should().Be(sheetId);
        range.Start.Row.Should().Be(2u);
        range.Start.Col.Should().Be(range.End.Col, "both endpoints are column B");
        range.End.Row.Should().Be(6u);
    }

    [Fact]
    public void TryParseFormulaRange_QuotedCrossSheetRangeWithResolver_StillResolvesSheet()
    {
        // Sibling no-regression: the pre-existing quoted cross-sheet resolver path (not bracketed)
        // must keep working after the bracket short-circuit was added.
        var chartSheetId = new SheetId(Guid.NewGuid());
        var dataSheetId = new SheetId(Guid.NewGuid());
        var resolver = new Dictionary<string, SheetId>(StringComparer.OrdinalIgnoreCase)
        {
            ["Data Sheet"] = dataSheetId
        };

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "'Data Sheet'!$C$1:$C$3",
            chartSheetId,
            resolver,
            out var range);

        parsed.Should().BeTrue();
        range.Start.Sheet.Should().Be(dataSheetId);
    }

    [Fact]
    public void HasUnparsableFormula_SeriesWithOrdinaryLocalRange_ReturnsFalse()
    {
        // Sibling no-regression: ordinary series formulas must not be flagged as unparsable.
        var sheetId = new SheetId(Guid.NewGuid());
        var seriesXml = """
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:val>
                <c:numRef>
                  <c:f>Sheet1!$B$2:$B$6</c:f>
                </c:numRef>
              </c:val>
            </c:ser>
            """;

        XlsxChartSeriesRangeReader.HasUnparsableFormula(ParseSeries(seriesXml), sheetId)
            .Should().BeFalse();
    }
}

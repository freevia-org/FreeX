using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Regression test for round 577's finding in <c>DocxWriter.BuildScatterSeries</c>: the scatter X
/// cache is built by parsing each CATEGORY's text as a double, and
/// <see cref="double.TryParse(string?, System.Globalization.NumberStyles, System.IFormatProvider?, out double)"/>
/// has not thrown on magnitude overflow since .NET Core -- it returns <c>true</c> with +/-Infinity.
/// <c>BuildNumCache</c> then emits each value with <c>double.ToString</c>, so a category of "1e400"
/// put the literal text <c>&lt;c:v&gt;Infinity&lt;/c:v&gt;</c> into a chart part FreeW had just
/// written: not a number in the schema the chart cache declares, in a document that was valid
/// before the round trip.
/// <para>
/// The sibling READ path in <c>DocxReader.ReadNumberCache</c> was guarded in r547; this write-side
/// parse was missed because it parses category TEXT rather than a cache value, so a sweep over
/// cache parsing never reached it. The remedy is the ordinal fallback the line already applies to
/// any category that is not a number -- a non-finite category is no more a numeric X than "Q1" is.
/// </para>
/// </summary>
public class R577_ScatterCategoryNonFiniteXValueTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XDocument WriteChartPart(Chart chart)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/charts/chart1.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static string[] XCacheValues(XDocument chartXml) =>
        chartXml.Descendants(C + "ser").Single()
            .Element(C + "xVal")!
            .Descendants(C + "pt")
            .Select(pt => pt.Element(C + "v")!.Value)
            .ToArray();

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("1E+999")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void ScatterChart_NonFiniteCategoryText_IsNotWrittenIntoTheXCache(string category)
    {
        var chart = Chart.Create(ChartKind.Scatter, [category, "2"], [3.0, 4.0], seriesName: "S");

        var values = XCacheValues(WriteChartPart(chart));

        values.Should().HaveCount(2);
        values[0].Should().NotContain("Infinity");
        values[0].Should().NotContain("NaN");
        values[0].Should().NotContain("∞");
        // The line's existing fallback for a non-numeric category is the 1-based ordinal.
        values[0].Should().Be("1");
    }

    [Fact]
    public void ScatterChart_NumericCategoryText_StillBecomesTheXValue()
    {
        // Non-vacuity: the finite guard must not have collapsed every category to its ordinal.
        var values = XCacheValues(WriteChartPart(
            Chart.Create(ChartKind.Scatter, ["12.5", "40"], [3.0, 4.0], seriesName: "S")));

        values.Should().Equal("12.5", "40");
    }
}

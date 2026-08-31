using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R20-meta-3: <c>MergeChartExSeries</c> (XlsxFileAdapter.SourcePackage.cs) used to pair source vs
/// generated &lt;cx:series&gt; elements purely by list POSITION (a <c>Math.Min</c> loop). Removing a
/// NON-trailing series (e.g. the first of three) shifts every subsequent generated series into the
/// previous series' slot, so the round-19 "preserve unmodeled per-series formatting" merge welded
/// series 1's formatting (dataPt/spPr) onto series 2's data, and series 2's formatting onto series
/// 3's data. The fix pairs series by IDENTITY -- the value-range formula each series' cx:dataId
/// resolves to via cx:chartData -- before falling back to positional pairing, so a non-trailing
/// remove keeps each surviving series' own formatting attached to its own data.
///
/// These tests invoke the private static merge method directly (via reflection, matching the
/// established pattern in R19_CalcChainAndPatchMetadataTests.cs) so the exact XML shapes described in
/// the finding -- a 3-series source chart merged against a 2-series generated chart whose surviving
/// series kept referencing their original (unshifted) data columns -- can be constructed precisely.
/// </summary>
public sealed class R20_chartex_series_identity_Tests
{
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void MergeChartExSeries_NonTrailingSeriesRemoved_KeepsSurvivorsFormattingAlignedToOwnData()
    {
        // Source: a 3-series chartEx (as if opened from a real Excel-authored file) where each
        // series carries its own distinguishing per-series formatting (a dataPt/spPr marker) that
        // FreeX's model doesn't represent at all -- exactly the R19-preserved unmodeled content.
        var sourceXml = BuildChartExDocument(
            dataFormulasById: [("0", "Sheet1!$B$2:$B$4"), ("1", "Sheet1!$C$2:$C$4"), ("2", "Sheet1!$D$2:$D$4")],
            series:
            [
                BuildSourceSeries(dataId: "0", marker: "SERIES-1-MARK"),
                BuildSourceSeries(dataId: "1", marker: "SERIES-2-MARK"),
                BuildSourceSeries(dataId: "2", marker: "SERIES-3-MARK"),
            ]);
        var sourceChart = sourceXml.Root!.Element(ChartExNs + "chart")!;

        // Generated: the user removed series 1 via the chart's data-range/series editor. Series 2
        // and 3 still reference their ORIGINAL (unshifted) columns C and D -- only the dataId index
        // that names them got renumbered (0, 1) now that there are only two series. FreeX never
        // emits dataPt/spPr (that's exactly the unmodeled content R19 preserves).
        var generatedXml = BuildChartExDocument(
            dataFormulasById: [("0", "Sheet1!$C$2:$C$4"), ("1", "Sheet1!$D$2:$D$4")],
            series:
            [
                BuildGeneratedSeries(dataId: "0"),
                BuildGeneratedSeries(dataId: "1"),
            ]);

        InvokeMergeChartExSeries(sourceXml, sourceChart, generatedXml);

        var survivingSeries = sourceChart
            .Element(ChartExNs + "plotArea")!
            .Element(ChartExNs + "plotAreaRegion")!
            .Elements(ChartExNs + "series")
            .ToList();

        // The removed series' data (and its formatting) must be gone -- not silently misassigned.
        survivingSeries.Should().HaveCount(2, "series 1 was removed and has no counterpart in the generated chart");
        survivingSeries.SelectMany(series => series.Descendants(DrawingNs + "srgbClr"))
            .Select(color => color.Attribute("val")!.Value)
            .Should().NotContain("SERIES-1-MARK", "the deleted series' formatting must not leak onto a survivor");

        // The survivor now at dataId "0" must be the ORIGINAL series 2 (by data-range identity),
        // carrying series 2's own marker -- NOT series 1's marker welded on by positional pairing.
        var firstSurvivor = survivingSeries.Single(series => series.Element(ChartExNs + "dataId")!.Attribute("val")!.Value == "0");
        firstSurvivor.Descendants(DrawingNs + "srgbClr").Should().ContainSingle()
            .Which.Attribute("val")!.Value.Should().Be("SERIES-2-MARK");

        // The survivor now at dataId "1" must be the ORIGINAL series 3, carrying series 3's marker.
        var secondSurvivor = survivingSeries.Single(series => series.Element(ChartExNs + "dataId")!.Attribute("val")!.Value == "1");
        secondSurvivor.Descendants(DrawingNs + "srgbClr").Should().ContainSingle()
            .Which.Attribute("val")!.Value.Should().Be("SERIES-3-MARK");
    }

    [Fact]
    public void MergeChartExSeries_UntouchedThreeSeriesChart_PreservesEachSeriesOwnFormatting()
    {
        // Sanity companion: with NOTHING removed (source and generated both have 3 series, each still
        // pointing at its original column), every series must keep its own formatting -- confirming
        // the identity-based merge behaves identically to the old positional merge in the untouched
        // case (the scenario R19_ChartExPreserveTests.cs already covers for a single-series chart).
        var sourceXml = BuildChartExDocument(
            dataFormulasById: [("0", "Sheet1!$B$2:$B$4"), ("1", "Sheet1!$C$2:$C$4"), ("2", "Sheet1!$D$2:$D$4")],
            series:
            [
                BuildSourceSeries(dataId: "0", marker: "SERIES-1-MARK"),
                BuildSourceSeries(dataId: "1", marker: "SERIES-2-MARK"),
                BuildSourceSeries(dataId: "2", marker: "SERIES-3-MARK"),
            ]);
        var sourceChart = sourceXml.Root!.Element(ChartExNs + "chart")!;

        var generatedXml = BuildChartExDocument(
            dataFormulasById: [("0", "Sheet1!$B$2:$B$4"), ("1", "Sheet1!$C$2:$C$4"), ("2", "Sheet1!$D$2:$D$4")],
            series:
            [
                BuildGeneratedSeries(dataId: "0"),
                BuildGeneratedSeries(dataId: "1"),
                BuildGeneratedSeries(dataId: "2"),
            ]);

        InvokeMergeChartExSeries(sourceXml, sourceChart, generatedXml);

        var survivingSeries = sourceChart
            .Element(ChartExNs + "plotArea")!
            .Element(ChartExNs + "plotAreaRegion")!
            .Elements(ChartExNs + "series")
            .ToList();

        survivingSeries.Should().HaveCount(3);
        foreach (var (dataId, expectedMarker) in new[] { ("0", "SERIES-1-MARK"), ("1", "SERIES-2-MARK"), ("2", "SERIES-3-MARK") })
        {
            var series = survivingSeries.Single(s => s.Element(ChartExNs + "dataId")!.Attribute("val")!.Value == dataId);
            series.Descendants(DrawingNs + "srgbClr").Should().ContainSingle().Which.Attribute("val")!.Value.Should().Be(expectedMarker);
        }
    }

    private static XDocument BuildChartExDocument(
        (string Id, string Formula)[] dataFormulasById,
        XElement[] series)
    {
        return new XDocument(
            new XElement(ChartExNs + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "cx", ChartExNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", DrawingNs.NamespaceName),
                new XElement(ChartExNs + "chartData",
                    dataFormulasById.Select(entry =>
                        new XElement(ChartExNs + "data",
                            new XAttribute("id", entry.Id),
                            new XElement(ChartExNs + "numDim",
                                new XAttribute("type", "val"),
                                new XElement(ChartExNs + "f", entry.Formula))))),
                new XElement(ChartExNs + "chart",
                    new XElement(ChartExNs + "plotArea",
                        new XElement(ChartExNs + "plotAreaRegion", series)))));
    }

    private static XElement BuildSourceSeries(string dataId, string marker) =>
        new(ChartExNs + "series",
            new XAttribute("layoutId", "treemap"),
            new XElement(ChartExNs + "dataPt",
                new XAttribute("idx", "0"),
                new XElement(ChartExNs + "spPr",
                    new XElement(DrawingNs + "solidFill",
                        new XElement(DrawingNs + "srgbClr", new XAttribute("val", marker))))),
            new XElement(ChartExNs + "dataId", new XAttribute("val", dataId)));

    private static XElement BuildGeneratedSeries(string dataId) =>
        new(ChartExNs + "series",
            new XAttribute("layoutId", "treemap"),
            new XElement(ChartExNs + "dataId", new XAttribute("val", dataId)));

    private static void InvokeMergeChartExSeries(XDocument sourceXml, XElement sourceChart, XDocument generatedXml)
    {
        XlsxFileAdapter.MergeChartExSeries(sourceXml, sourceChart, generatedXml, ChartExNs);
    }
}

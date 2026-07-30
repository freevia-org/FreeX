using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R96-io-chart-hyperlink-name-key: R95's chart-hyperlink preservation
/// (<c>XlsxFileAdapter.GetSourceChartHyperlinksBySheet</c> / <c>XlsxWorksheetChartWriter.WriteWorksheetCharts</c>)
/// correctly reads a sheet's chart hyperlinks from the TRUE source package, but originally matched them
/// to the CURRENT save's charts purely by a positional index (the Nth original chart's pair goes to the
/// Nth chart written this save). That desyncs -- silently MISATTRIBUTING one chart's hyperlink onto a
/// different chart, not merely dropping it -- the moment a sheet's chart set changes shape between load
/// and save: a chart is deleted, a chart is inserted before another, or the charts are reordered. The fix
/// keys the source pairs by each chart graphicFrame's stable <c>cNvPr@name</c> (round-tripped onto
/// <c>ChartModel.Name</c>) instead, mirroring how <c>GetSourceDrawingObjectHyperlinksBySheet</c> already
/// keys shape/picture/text-box hyperlinks by name for the identical reason.
/// </summary>
public sealed class R96_ChartHyperlinkNameKeyedMatchTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string HyperlinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    // --- The reported bug: deleting the hyperlinked chart must not hand its hyperlink to the
    // --- surviving chart (misattribution), nor to anything else. -----------------------------------

    [Fact]
    public void DeletingHyperlinkedChart_DoesNotMisattributeHyperlinkToSurvivingChart()
    {
        using var package = BuildTwoChartPackageWithHyperlinkOnFirst("https://example.com/chart-one-link");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);

        var chartOne = sheet.Charts.Single(c => c.Name == "Chart 1");
        var chartTwo = sheet.Charts.Single(c => c.Name == "Chart 2");
        sheet.Charts.Remove(chartOne);

        ForceFullRebuildChartEdit(loaded, chartTwo);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingPath = FindSinglePart(archive, "xl/drawings/", isRels: false);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
        var graphicFrames = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame").ToList();
        graphicFrames.Should().ContainSingle("exactly one chart must remain after deleting Chart 1");
        var graphicFrame = graphicFrames.Single();
        var cNvPr = graphicFrame.Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!.Element(SpreadsheetDrawingNs + "cNvPr")!;

        cNvPr.Element(DrawingNs + "hlinkClick").Should().BeNull(
            "the surviving chart (Chart 2) never had a hyperlink of its own -- deleting its sibling " +
            "(Chart 1, which DID have a hyperlink) must not cause the now-first-in-document-order " +
            "Chart 2 to silently inherit Chart 1's hyperlink through stale positional matching");
    }

    // --- The same bug via reordering instead of deletion: each chart must keep ITS OWN hyperlink (or
    // --- lack of one) when the two charts swap places in document order. --------------------------

    [Fact]
    public void ReorderingCharts_EachChartKeepsItsOwnHyperlink()
    {
        using var package = BuildTwoChartPackageWithHyperlinkOnFirst("https://example.com/chart-one-link");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);

        var chartOne = sheet.Charts.Single(c => c.Name == "Chart 1");
        var chartTwo = sheet.Charts.Single(c => c.Name == "Chart 2");

        // Swap document order: Chart 2 (no hyperlink) now comes first, Chart 1 (has the hyperlink)
        // now comes second -- a pure reorder, no add/delete.
        sheet.Charts.Clear();
        sheet.Charts.Add(chartTwo);
        sheet.Charts.Add(chartOne);

        ForceFullRebuildChartEdit(loaded, chartTwo);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var (nameToHyperlink, _) = ReadChartHyperlinksByName(saved);

        nameToHyperlink.Should().ContainKey("Chart 1", "Chart 1 must still exist after the reorder");
        nameToHyperlink["Chart 1"].Should().Be("https://example.com/chart-one-link",
            "Chart 1 must keep its OWN hyperlink even though it is no longer first in document order");
        nameToHyperlink.Should().ContainKey("Chart 2", "Chart 2 must still exist after the reorder");
        nameToHyperlink["Chart 2"].Should().BeNull(
            "Chart 2 never had a hyperlink and must not acquire Chart 1's hyperlink just because it is " +
            "now first in document order");
    }

    // --- No-regression sibling: with NO structural change (no add/delete/reorder), a multi-chart sheet's
    // --- hyperlinks must still each land on the correct chart -- the R95 tests only ever covered a
    // --- single chart per sheet. ------------------------------------------------------------------

    [Fact]
    public void MultiChartSheet_NoStructuralChange_EachChartKeepsItsOwnHyperlinkAcrossSave()
    {
        using var package = BuildTwoChartPackageWithHyperlinkOnFirst("https://example.com/chart-one-link");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);
        var chartTwo = sheet.Charts.Single(c => c.Name == "Chart 2");

        // No add/delete/reorder here -- only an unrelated edit to force the full-rebuild save path,
        // same as R95's own ForceFullRebuildChartEdit.
        ForceFullRebuildChartEdit(loaded, chartTwo);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var (nameToHyperlink, _) = ReadChartHyperlinksByName(saved);

        nameToHyperlink.Should().ContainKey("Chart 1");
        nameToHyperlink["Chart 1"].Should().Be("https://example.com/chart-one-link",
            "Chart 1's own hyperlink must survive a save that doesn't touch the sheet's chart set shape");
        nameToHyperlink.Should().ContainKey("Chart 2");
        nameToHyperlink["Chart 2"].Should().BeNull("Chart 2 never had a hyperlink and must still have none");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a real .xlsx package (via an initial real <see cref="XlsxFileAdapter.Save"/> of a workbook
    /// with TWO charts, so all surrounding parts/relationships/content-types are authentic), then injects
    /// an <c>a:hlinkClick</c> onto ONLY the first chart's ("Chart 1") graphicFrame <c>cNvPr</c> -- exactly
    /// as Excel or a prior FreeX save would leave it. "Chart 1"/"Chart 2" are the names
    /// <see cref="XlsxWorksheetChartWriter"/> assigns freshly-authored charts (no <see cref="ChartModel.Name"/>
    /// yet), in the same order they were added to <see cref="Sheet.Charts"/>.
    /// </summary>
    private static MemoryStream BuildTwoChartPackageWithHyperlinkOnFirst(string objectHyperlinkTarget)
    {
        var workbook = new Workbook("ChartHyperlinkNameKeyedMatch");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 3; row++)
        {
            for (uint col = 1; col <= 3; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 10 + col));
        }
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Chart One",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Chart Two",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 3)),
        });

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var drawingPath = FindSinglePart(archive, "xl/drawings/", isRels: false);
            var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);

            var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
            var graphicFrames = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame").ToList();
            graphicFrames.Should().HaveCount(2, "the source package must have exactly two charts for this test");

            var firstCNvPr = graphicFrames
                .Select(gf => gf.Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!.Element(SpreadsheetDrawingNs + "cNvPr")!)
                .Single(cNvPr => cNvPr.Attribute("name")?.Value == "Chart 1");
            firstCNvPr.Add(new XElement(DrawingNs + "hlinkClick", new XAttribute(RelNs + "id", "rIdTestObjectHlink")));
            WritePackageXml(archive, drawingPath, drawingXml);

            var drawingRelsXml = archive.GetEntry(drawingRelsPath) is { } existingDrawingRels
                ? XlsxPackageTestFixtures.LoadPackageXml(existingDrawingRels)
                : new XDocument(new XElement(PackageRelNs + "Relationships"));
            drawingRelsXml.Root!.Add(new XElement(
                PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdTestObjectHlink"),
                new XAttribute("Type", HyperlinkRelationshipType),
                new XAttribute("Target", objectHyperlinkTarget),
                new XAttribute("TargetMode", "External")));
            WritePackageXml(archive, drawingRelsPath, drawingRelsXml);
        }

        package.Position = 0;
        return package;
    }

    /// <summary>
    /// Reads the saved package's drawing part and returns, for every remaining chart graphicFrame, its
    /// cNvPr@name mapped to its resolved object-hyperlink target (null if it has none).
    /// </summary>
    private static (Dictionary<string, string?> ByName, int ChartCount) ReadChartHyperlinksByName(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingPath = FindSinglePart(archive, "xl/drawings/", isRels: false);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        var relsXml = archive.GetEntry(drawingRelsPath) is { } relsEntry
            ? XlsxPackageTestFixtures.LoadPackageXml(relsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));
        var relTargets = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Where(r => r.Attribute("Id") is not null && r.Attribute("Target") is not null)
            .ToDictionary(r => r.Attribute("Id")!.Value, r => r.Attribute("Target")!.Value);

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var graphicFrames = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame").ToList();
        foreach (var graphicFrame in graphicFrames)
        {
            var cNvPr = graphicFrame.Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!.Element(SpreadsheetDrawingNs + "cNvPr")!;
            var name = cNvPr.Attribute("name")!.Value;
            var hlinkClick = cNvPr.Element(DrawingNs + "hlinkClick");
            var relId = hlinkClick?.Attribute(RelNs + "id")?.Value;
            result[name] = relId is not null && relTargets.TryGetValue(relId, out var target) ? target : null;
        }

        return (result, graphicFrames.Count);
    }

    private static string FindSinglePart(ZipArchive archive, string prefix, bool isRels) =>
        archive.Entries
            .Select(e => e.FullName)
            .Single(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                             name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                             name.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) == isRels);

    /// <summary>
    /// A real, ordinary editing command (changing the given chart's type) that has NOTHING to do with
    /// hyperlinks but forces <see cref="XlsxFileAdapter.Save"/> off both fast paths (the "model
    /// unchanged, verbatim source copy" short-circuit AND the cell-value patch path) and onto the full
    /// ClosedXML-rebuild path -- the only path that runs <c>XlsxWorksheetChartWriter.Save</c> against a
    /// freshly-generated, chart-less package. Mirrors R95_ChartHyperlinkRealPipelineTests's own helper.
    /// </summary>
    private static void ForceFullRebuildChartEdit(Workbook workbook, ChartModel chart)
    {
        var sheet = workbook.Sheets.Single(s => s.Charts.Contains(chart));
        var nextType = chart.Type == ChartType.Bar ? ChartType.Column : ChartType.Bar;
        new ChangeChartTypeCommand(sheet.Id, chart.Id, nextType)
            .Apply(new TestCommandContext(workbook))
            .Success.Should().BeTrue("the chart type change must be a valid, real edit through the command layer");
    }

    private static void WritePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}

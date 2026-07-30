using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R95-io-chart-hyperlink-real-pipeline: R41's chart-hyperlink preservation
/// (<c>XlsxWorksheetChartWriter.ReadOldChartGraphicFrameHyperlinks</c> /
/// <c>ReadOldChartTitleHyperlink</c>) reads the hyperlink from the <c>archive</c> parameter that
/// <c>XlsxWorksheetChartWriter.Save</c> is handed -- but through the REAL <see cref="XlsxFileAdapter"/>
/// save pipeline, that parameter is the in-progress, freshly-ClosedXML-generated package
/// (<c>packageStream</c> in <c>XlsxFileAdapter.ApplyPackagePostProcessing</c>), which never contains the
/// original source drawing/chart bytes -- ClosedXML always builds a brand-new, chart-less workbook and
/// FreeX's own writers populate it from scratch. R41's own tests never caught this because they call
/// <c>XlsxWorksheetChartWriter.Save</c> directly with a hand-seeded package standing in for "the archive",
/// which is exactly the shape the real pipeline does NOT have. This test drives the REAL entry point
/// (<see cref="XlsxFileAdapter.Load"/> then <see cref="XlsxFileAdapter.Save"/>) end to end, mirroring the
/// sibling fix in <c>R95_DrawingObjectHyperlinkPreservationTests</c> for shapes/text boxes/pictures.
/// </summary>
public sealed class R95_ChartHyperlinkRealPipelineTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string HyperlinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    // --- Finding 3-1 through the real pipeline: chart-object hyperlink on the graphicFrame --------

    [Fact]
    public void RealPipeline_LoadThenSave_PreservesChartObjectHyperlink()
    {
        using var package = BuildChartPackageWithHyperlinks(
            objectHyperlinkTarget: "https://example.com/object-link",
            titleHyperlinkTarget: null);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        ForceFullRebuildChartEdit(loaded);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var (target, targetMode) = ResolveGraphicFrameHyperlink(saved);
        target.Should().Be("https://example.com/object-link",
            "the chart-object hyperlink present in the original file must survive a real Load+Save " +
            "round-trip through XlsxFileAdapter, not just a hand-seeded direct call to XlsxWorksheetChartWriter.Save");
        targetMode.Should().Be("External");
    }

    // --- Finding 3-2 through the real pipeline: hyperlink on the chart's main title run -----------

    [Fact]
    public void RealPipeline_LoadThenSave_PreservesChartTitleHyperlink()
    {
        using var package = BuildChartPackageWithHyperlinks(
            objectHyperlinkTarget: null,
            titleHyperlinkTarget: "https://example.org/title-link");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        ForceFullRebuildChartEdit(loaded);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var (target, targetMode) = ResolveTitleHyperlink(saved);
        target.Should().Be("https://example.org/title-link",
            "the chart title's hyperlink present in the original file must survive a real Load+Save " +
            "round-trip through XlsxFileAdapter");
        targetMode.Should().Be("External");
    }

    // --- Both hyperlinks together, and across TWO consecutive save cycles (Load -> Save -> Load ->
    // --- Save), matching the task's "across two consecutive save cycles" requirement. --------------

    [Fact]
    public void RealPipeline_TwoConsecutiveSaveCycles_PreservesBothHyperlinks()
    {
        using var package = BuildChartPackageWithHyperlinks(
            objectHyperlinkTarget: "https://example.com/object-link",
            titleHyperlinkTarget: "https://example.org/title-link");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        ForceFullRebuildChartEdit(loaded);

        using var firstSave = new MemoryStream();
        adapter.Save(loaded, firstSave);

        firstSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(firstSave);
        ForceFullRebuildChartEdit(reloaded);

        using var secondSave = new MemoryStream();
        new XlsxFileAdapter().Save(reloaded, secondSave);

        var (objectTarget, _) = ResolveGraphicFrameHyperlink(secondSave);
        objectTarget.Should().Be("https://example.com/object-link",
            "the chart-object hyperlink must survive a SECOND save cycle too");

        var (titleTarget, _) = ResolveTitleHyperlink(secondSave);
        titleTarget.Should().Be("https://example.org/title-link",
            "the chart title hyperlink must survive a SECOND save cycle too");
    }

    // --- No-regression sibling: a chart with no hyperlinks at all must not gain spurious ones through
    // --- the real pipeline. ---------------------------------------------------------------------------

    [Fact]
    public void RealPipeline_LoadThenSave_DoesNotInventHyperlinksWhenSourceHasNone()
    {
        using var package = BuildChartPackageWithHyperlinks(objectHyperlinkTarget: null, titleHyperlinkTarget: null);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        ForceFullRebuildChartEdit(loaded);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingPath = FindSinglePart(archive, "xl/drawings/", isRels: false);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
        drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame").Single()
            .Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!
            .Element(SpreadsheetDrawingNs + "cNvPr")!
            .Element(DrawingNs + "hlinkClick").Should().BeNull("no hyperlink existed on the source chart object");

        var chartPath = FindSinglePart(archive, "xl/charts/", isRels: false);
        var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, chartPath);
        var titleRun = chartXml.Root!.Element(ChartNs + "chart")?.Element(ChartNs + "title")?
            .Element(ChartNs + "tx")?.Element(ChartNs + "rich")?
            .Element(DrawingNs + "p")?.Element(DrawingNs + "r")?.Element(DrawingNs + "rPr");
        titleRun?.Element(DrawingNs + "hlinkClick").Should().BeNull("no hyperlink existed on the source title");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a real .xlsx package (via an initial real <see cref="XlsxFileAdapter.Save"/> of a workbook
    /// with an actual chart, so all the surrounding parts/relationships/content-types are authentic),
    /// then injects an <c>a:hlinkClick</c> onto the chart's graphicFrame <c>cNvPr</c> and/or the chart
    /// title's run, exactly as Excel or a prior FreeX save would have left them. This is genuinely
    /// "the file a user opens" -- not a hand-built package standing in for an internal writer parameter.
    /// </summary>
    private static MemoryStream BuildChartPackageWithHyperlinks(string? objectHyperlinkTarget, string? titleHyperlinkTarget)
    {
        var workbook = new Workbook("ChartHyperlinkRealPipeline");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 3; row++)
        {
            for (uint col = 1; col <= 3; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 10 + col));
        }
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "My Title",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
        });

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var drawingPath = FindSinglePart(archive, "xl/drawings/", isRels: false);
            var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
            var chartPath = FindSinglePart(archive, "xl/charts/", isRels: false);
            var chartRelsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);

            if (objectHyperlinkTarget is not null)
            {
                var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
                var cNvPr = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame").Single()
                    .Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!
                    .Element(SpreadsheetDrawingNs + "cNvPr")!;
                cNvPr.Add(new XElement(DrawingNs + "hlinkClick", new XAttribute(RelNs + "id", "rIdTestObjectHlink")));
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

            if (titleHyperlinkTarget is not null)
            {
                var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, chartPath);
                var rPr = chartXml.Root!.Element(ChartNs + "chart")?.Element(ChartNs + "title")?
                    .Element(ChartNs + "tx")?.Element(ChartNs + "rich")?
                    .Element(DrawingNs + "p")?.Element(DrawingNs + "r")?.Element(DrawingNs + "rPr");
                rPr.Should().NotBeNull("the chart writer must have emitted a title run to attach a hyperlink to");
                rPr!.Add(new XElement(DrawingNs + "hlinkClick", new XAttribute(RelNs + "id", "rIdTestTitleHlink")));
                WritePackageXml(archive, chartPath, chartXml);

                var chartRelsXml = archive.GetEntry(chartRelsPath) is { } existingChartRels
                    ? XlsxPackageTestFixtures.LoadPackageXml(existingChartRels)
                    : new XDocument(new XElement(PackageRelNs + "Relationships"));
                chartRelsXml.Root!.Add(new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdTestTitleHlink"),
                    new XAttribute("Type", HyperlinkRelationshipType),
                    new XAttribute("Target", titleHyperlinkTarget),
                    new XAttribute("TargetMode", "External")));
                WritePackageXml(archive, chartRelsPath, chartRelsXml);
            }
        }

        package.Position = 0;
        return package;
    }

    private static (string Target, string? TargetMode) ResolveGraphicFrameHyperlink(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingPath = FindSinglePart(archive, "xl/drawings/", isRels: false);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
        var cNvPr = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame").Single()
            .Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!
            .Element(SpreadsheetDrawingNs + "cNvPr")!;
        var hlinkClick = cNvPr.Element(DrawingNs + "hlinkClick");
        hlinkClick.Should().NotBeNull("the rebuilt graphicFrame must carry the preserved hlinkClick");
        var relId = hlinkClick!.Attribute(RelNs + "id")!.Value;

        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingRelsPath);
        var relationship = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .First(r => r.Attribute("Id")!.Value == relId);
        return (relationship.Attribute("Target")!.Value, relationship.Attribute("TargetMode")?.Value);
    }

    private static (string Target, string? TargetMode) ResolveTitleHyperlink(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var chartPath = FindSinglePart(archive, "xl/charts/", isRels: false);
        var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, chartPath);
        var hlinkClick = chartXml.Root!
            .Element(ChartNs + "chart")!
            .Element(ChartNs + "title")!
            .Element(ChartNs + "tx")!
            .Element(ChartNs + "rich")!
            .Element(DrawingNs + "p")!
            .Element(DrawingNs + "r")!
            .Element(DrawingNs + "rPr")!
            .Element(DrawingNs + "hlinkClick");
        hlinkClick.Should().NotBeNull("the rebuilt title run must carry the preserved hlinkClick");
        var relId = hlinkClick!.Attribute(RelNs + "id")!.Value;

        var chartRelsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
        var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, chartRelsPath);
        var relationship = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .First(r => r.Attribute("Id")!.Value == relId);
        return (relationship.Attribute("Target")!.Value, relationship.Attribute("TargetMode")?.Value);
    }

    private static string FindSinglePart(ZipArchive archive, string prefix, bool isRels) =>
        archive.Entries
            .Select(e => e.FullName)
            .Single(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                             name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                             name.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) == isRels);

    /// <summary>
    /// A real, ordinary editing command (changing the chart type) that has NOTHING to do with
    /// hyperlinks but forces <see cref="XlsxFileAdapter.Save"/> off both fast paths (the
    /// "model unchanged, verbatim source copy" short-circuit AND the cell-value patch path) and onto
    /// the full ClosedXML-rebuild path -- the only path that runs <c>XlsxWorksheetChartWriter.Save</c>
    /// against a freshly-generated, chart-less package. Alternates Column/Bar so a second call (as
    /// used by the two-consecutive-save-cycles test) is a genuine change again, not a no-op that would
    /// let the fast "model unchanged" path swallow the second save too.
    /// </summary>
    private static void ForceFullRebuildChartEdit(Workbook workbook)
    {
        var sheet = workbook.GetSheetAt(0);
        var chart = sheet.Charts.Single();
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

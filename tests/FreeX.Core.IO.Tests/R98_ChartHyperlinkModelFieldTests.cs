using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R98-io-chart-hyperlink-model-field: R95/R96 correctly re-attach a chart's hyperlink at save time by
/// re-reading the TRUE source package, keyed by (current host sheet name -&gt; chart cNvPr@name) -- but
/// <see cref="ChartModel"/> itself had no hyperlink field of its own, so that lookup is keyed by the
/// chart's CURRENT host sheet, not the chart's own identity. Two confirmed consequences:
/// <list type="bullet">
/// <item>FINDING 1: <c>MoveChartCommand</c>/<c>MoveChartToNewSheetCommand</c> relocate a chart to
/// another sheet; at save the chart is looked up against its NEW host sheet's ORIGINAL dictionary,
/// which never contained it -- the hyperlink is silently dropped, or (if the destination sheet already
/// has its own chart sharing the same Excel-auto-generated name, e.g. "Chart 1" on both sheets)
/// misattributed from that other chart.</item>
/// <item>FINDING 2: <c>DuplicateSheetDrawingCloner</c> (Duplicate Sheet / paste-chart) clones
/// <see cref="ChartModel"/> without any hyperlink field to carry, so the clone's hyperlink can only ever
/// be reconstructed by the same fragile sheet-name-keyed lookup -- which drops it entirely for a
/// freshly-duplicated sheet (that sheet's name never existed in the TRUE source package at all).</item>
/// </list>
/// The fix gives <see cref="ChartModel"/> its own <see cref="ChartModel.Hyperlink"/> field (reusing
/// <see cref="DrawingObjectHyperlink"/>, mirroring R97's identical fix for
/// picture/shape/text-box), populated PER CHART at load (from that chart's own graphicFrame, never a
/// sheet-name-keyed guess) and PREFERRED at save over the old sourceChartHyperlinks mechanism. Because
/// <c>MoveChartCommand</c>/<c>MoveChartToNewSheetCommand</c> relocate the SAME <see cref="ChartModel"/>
/// instance (not a copy), and <c>DuplicateSheetDrawingCloner.CloneChart</c> now copies the field onto the
/// clone, the hyperlink simply travels with the chart object wherever it goes.
/// </summary>
public sealed class R98_ChartHyperlinkModelFieldTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string HyperlinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    // =================================================================================================
    // FINDING 1: move to another (existing) sheet.
    // =================================================================================================

    [Fact]
    public void MoveChartToAnotherSheet_PreservesOwnHyperlink()
    {
        using var package = BuildTwoSheetPackage(
            sheet1ChartName: "Chart 1", sheet1HyperlinkTarget: "https://example.com/moved-chart-link", sheet1Title: "MovedChart",
            sheet2ChartName: "Chart 7", sheet2HyperlinkTarget: null, sheet2Title: "StationaryChart");

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet1 = workbook.GetSheetAt(0);
        var sheet2 = workbook.GetSheetAt(1);
        var movedChart = sheet1.Charts.Single();
        movedChart.Hyperlink.Should().NotBeNull("the load path must resolve the chart's own hyperlink onto the model");

        new MoveChartCommand(sheet1.Id, movedChart.Id, sheet2.Id)
            .Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        ForceFullRebuildChartEdit(workbook, movedChart);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var byTitle = ReadChartHyperlinksByTitle(saved, "Sheet2");
        byTitle.Should().ContainKey("MovedChart");
        byTitle["MovedChart"].Should().Be("https://example.com/moved-chart-link",
            "the chart's own hyperlink must follow it to its new sheet even though the destination " +
            "sheet's TRUE source package never had an entry for this chart");
    }

    // =================================================================================================
    // FINDING 1 core: destination sheet already has its own SAME-NAMED chart -- must not misattribute.
    // =================================================================================================

    [Fact]
    public void MoveChartToAnotherSheet_DestinationHasSameNamedChart_DoesNotMisattributeHyperlink()
    {
        using var package = BuildTwoSheetPackage(
            sheet1ChartName: "Chart 1", sheet1HyperlinkTarget: "https://example.com/sheet1-chart1-link", sheet1Title: "MovedChart",
            sheet2ChartName: "Chart 1", sheet2HyperlinkTarget: "https://example.com/sheet2-chart1-link", sheet2Title: "NativeChart");

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet1 = workbook.GetSheetAt(0);
        var sheet2 = workbook.GetSheetAt(1);
        var movedChart = sheet1.Charts.Single();
        var nativeChart = sheet2.Charts.Single();
        movedChart.Name.Should().Be("Chart 1");
        nativeChart.Name.Should().Be("Chart 1", "both charts sharing the same Excel-auto-generated name " +
            "on different sheets is the exact collision this test must exercise");

        new MoveChartCommand(sheet1.Id, movedChart.Id, sheet2.Id)
            .Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        ForceFullRebuildChartEdit(workbook, nativeChart);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var byTitle = ReadChartHyperlinksByTitle(saved, "Sheet2");
        byTitle.Should().HaveCount(2, "both the native chart and the moved chart must survive on Sheet2");
        byTitle["MovedChart"].Should().Be("https://example.com/sheet1-chart1-link",
            "the MOVED chart must keep its OWN hyperlink, not the native chart's, despite sharing the same name");
        byTitle["NativeChart"].Should().Be("https://example.com/sheet2-chart1-link",
            "the NATIVE chart must keep its OWN hyperlink, not the moved chart's, despite sharing the same name");
    }

    // =================================================================================================
    // FINDING 1 variant: move to a brand-new sheet (MoveChartToNewSheetCommand).
    // =================================================================================================

    [Fact]
    public void MoveChartToNewSheet_PreservesOwnHyperlink()
    {
        using var package = BuildSingleChartPackage(
            chartName: "Chart 1", hyperlinkTarget: "https://example.com/new-sheet-link", title: "OnlyChart");

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet1 = workbook.GetSheetAt(0);
        var chart = sheet1.Charts.Single();

        var moveCmd = new MoveChartToNewSheetCommand(sheet1.Id, chart.Id, "ChartSheet");
        moveCmd.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        ForceFullRebuildChartEdit(workbook, chart);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var byTitle = ReadChartHyperlinksByTitle(saved, "ChartSheet");
        byTitle.Should().ContainKey("OnlyChart");
        byTitle["OnlyChart"].Should().Be("https://example.com/new-sheet-link",
            "the chart's own hyperlink must survive a Move Chart > New Sheet, where the destination " +
            "sheet name never existed in the TRUE source package at all");
    }

    // =================================================================================================
    // FINDING 2 core: Duplicate Sheet -- the copy's sheet name never existed in the TRUE source package.
    // =================================================================================================

    [Fact]
    public void DuplicateSheet_ChartHyperlink_PreservedOnBothOriginalAndCopy()
    {
        using var package = BuildSingleChartPackage(
            chartName: "Chart 1", hyperlinkTarget: "https://example.com/dup-sheet-link", title: "DupChart");

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var source = workbook.GetSheetAt(0);
        source.Charts.Single().Hyperlink.Should().NotBeNull();

        var dupCmd = new DuplicateSheetCommand(source.Id, "Sheet1 (2)");
        dupCmd.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var originalByTitle = ReadChartHyperlinksByTitle(saved, source.Name);
        originalByTitle["DupChart"].Should().Be("https://example.com/dup-sheet-link",
            "the ORIGINAL sheet's chart hyperlink must still survive the save");

        var copyByTitle = ReadChartHyperlinksByTitle(saved, "Sheet1 (2)");
        copyByTitle.Should().ContainKey("DupChart");
        copyByTitle["DupChart"].Should().Be("https://example.com/dup-sheet-link",
            "the DUPLICATED sheet's own copy of the chart must carry the hyperlink forward even though " +
            "'Sheet1 (2)' never existed in the TRUE source package (so the old sheet-name-keyed lookup " +
            "would find nothing for it)");
    }

    // =================================================================================================
    // FINDING 2 variant: paste/duplicate a chart on the SAME sheet (Ctrl+C/Ctrl+V via
    // DuplicateDrawingObjectCommand, which reuses DuplicateSheetDrawingCloner.CloneChart).
    // =================================================================================================

    [Fact]
    public void DuplicateChartOnSameSheet_ClonePreservesHyperlink()
    {
        using var package = BuildSingleChartPackage(
            chartName: "Chart 1", hyperlinkTarget: "https://example.com/paste-chart-link", title: "PastedChart");

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet = workbook.GetSheetAt(0);
        var original = sheet.Charts.Single();

        var pasteCmd = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.Chart, original.Id);
        pasteCmd.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.Charts.Should().HaveCount(2);
        var clone = sheet.Charts.Single(c => c.Id != original.Id);
        clone.Hyperlink.Should().Be(original.Hyperlink, "the clone must carry the SAME hyperlink as its source");
        // Give the clone a distinct title so the saved package's two same-named ("Chart 1") charts can
        // be told apart below -- mirrors real usage (a user would retitle a pasted chart).
        clone.Title = "PastedChartClone";
        ForceFullRebuildChartEdit(workbook, original);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var byTitle = ReadChartHyperlinksByTitle(saved, sheet.Name);
        byTitle.Should().HaveCount(2);
        byTitle["PastedChart"].Should().Be("https://example.com/paste-chart-link");
        byTitle["PastedChartClone"].Should().Be("https://example.com/paste-chart-link",
            "the pasted clone (sharing the ORIGINAL's auto-generated cNvPr name, 'Chart 1') must keep " +
            "its OWN correctly-carried hyperlink, not fall through to a stale/misattributed lookup");
    }

    // =================================================================================================
    // No-regression sibling: a chart with NO hyperlink must not gain one from a same-named sibling
    // after a move.
    // =================================================================================================

    [Fact]
    public void MoveChartWithNoHyperlink_DoesNotInventOneFromSameNamedSibling()
    {
        using var package = BuildTwoSheetPackage(
            sheet1ChartName: "Chart 1", sheet1HyperlinkTarget: null, sheet1Title: "MovedChart",
            sheet2ChartName: "Chart 1", sheet2HyperlinkTarget: "https://example.com/native-link", sheet2Title: "NativeChart");

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet1 = workbook.GetSheetAt(0);
        var sheet2 = workbook.GetSheetAt(1);
        var movedChart = sheet1.Charts.Single();
        movedChart.Hyperlink.Should().BeNull();

        new MoveChartCommand(sheet1.Id, movedChart.Id, sheet2.Id)
            .Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        ForceFullRebuildChartEdit(workbook, movedChart);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var byTitle = ReadChartHyperlinksByTitle(saved, "Sheet2");
        byTitle["MovedChart"].Should().BeNull(
            "the moved chart never had a hyperlink and must not acquire the same-named native chart's " +
            "hyperlink just by landing on its sheet");
        byTitle["NativeChart"].Should().Be("https://example.com/native-link",
            "the native chart must keep its own hyperlink unaffected by the incoming chart");
    }

    // =================================================================================================
    // Two consecutive saves: the misattribution-prone scenario must stay correct across a SECOND save.
    // =================================================================================================

    [Fact]
    public void MoveChartToAnotherSheet_TwoConsecutiveSaves_StaysCorrect()
    {
        using var package = BuildTwoSheetPackage(
            sheet1ChartName: "Chart 1", sheet1HyperlinkTarget: "https://example.com/sheet1-link", sheet1Title: "MovedChart",
            sheet2ChartName: "Chart 1", sheet2HyperlinkTarget: "https://example.com/sheet2-link", sheet2Title: "NativeChart");

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet1 = workbook.GetSheetAt(0);
        var sheet2 = workbook.GetSheetAt(1);
        var movedChart = sheet1.Charts.Single();
        var nativeChart = sheet2.Charts.Single();

        new MoveChartCommand(sheet1.Id, movedChart.Id, sheet2.Id)
            .Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        ForceFullRebuildChartEdit(workbook, nativeChart);

        using var firstSave = new MemoryStream();
        adapter.Save(workbook, firstSave);

        firstSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(firstSave);
        var reloadedSheet2 = reloaded.Sheets.Single(s => s.Name == "Sheet2");
        ForceFullRebuildChartEdit(reloaded, reloadedSheet2.Charts.First());

        using var secondSave = new MemoryStream();
        new XlsxFileAdapter().Save(reloaded, secondSave);

        var byTitle = ReadChartHyperlinksByTitle(secondSave, "Sheet2");
        byTitle.Should().HaveCount(2);
        byTitle["MovedChart"].Should().Be("https://example.com/sheet1-link", "must survive a SECOND save cycle too");
        byTitle["NativeChart"].Should().Be("https://example.com/sheet2-link", "must survive a SECOND save cycle too");
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a real, single-sheet .xlsx package (via a real <see cref="XlsxFileAdapter.Save"/>) with
    /// one chart, then (optionally) injects an <c>a:hlinkClick</c> onto the chart's graphicFrame
    /// <c>cNvPr</c>, exactly as Excel or a prior FreeX save would leave it.
    /// </summary>
    private static MemoryStream BuildSingleChartPackage(string chartName, string? hyperlinkTarget, string title)
    {
        var workbook = new Workbook("ChartHyperlinkModelField");
        var sheet = workbook.AddSheet("Sheet1");
        SeedGrid(sheet);
        sheet.Charts.Add(new ChartModel
        {
            Name = chartName,
            Type = ChartType.Column,
            Title = title,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
        });

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        if (hyperlinkTarget is not null)
        {
            using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
            InjectChartObjectHyperlink(archive, "Sheet1", chartName, hyperlinkTarget);
        }

        package.Position = 0;
        return package;
    }

    /// <summary>
    /// Builds a real, TWO-sheet .xlsx package, each sheet with its own single chart (which may share
    /// the SAME <paramref name="sheet1ChartName"/>/<paramref name="sheet2ChartName"/> -- the exact
    /// collision the misattribution findings are about), then injects each chart's own hyperlink (if
    /// any) into that sheet's OWN drawing part.
    /// </summary>
    private static MemoryStream BuildTwoSheetPackage(
        string sheet1ChartName, string? sheet1HyperlinkTarget, string sheet1Title,
        string sheet2ChartName, string? sheet2HyperlinkTarget, string sheet2Title)
    {
        var workbook = new Workbook("ChartHyperlinkModelFieldTwoSheet");
        var sheet1 = workbook.AddSheet("Sheet1");
        SeedGrid(sheet1);
        sheet1.Charts.Add(new ChartModel
        {
            Name = sheet1ChartName,
            Type = ChartType.Column,
            Title = sheet1Title,
            DataRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 2)),
        });

        var sheet2 = workbook.AddSheet("Sheet2");
        SeedGrid(sheet2);
        sheet2.Charts.Add(new ChartModel
        {
            Name = sheet2ChartName,
            Type = ChartType.Column,
            Title = sheet2Title,
            DataRange = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 3, 2)),
        });

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            if (sheet1HyperlinkTarget is not null)
                InjectChartObjectHyperlink(archive, "Sheet1", sheet1ChartName, sheet1HyperlinkTarget);
            if (sheet2HyperlinkTarget is not null)
                InjectChartObjectHyperlink(archive, "Sheet2", sheet2ChartName, sheet2HyperlinkTarget);
        }

        package.Position = 0;
        return package;
    }

    private static void SeedGrid(Sheet sheet)
    {
        for (uint row = 1; row <= 3; row++)
        {
            for (uint col = 1; col <= 2; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 10 + col));
        }
    }

    /// <summary>
    /// Resolves <paramref name="sheetName"/>'s OWN drawing part (via xl/workbook.xml -> the sheet's
    /// worksheet part -> that worksheet's own &lt;drawing r:id=".."/&gt;), finds the chart graphicFrame
    /// named <paramref name="chartName"/> within it, and adds an <c>a:hlinkClick</c> (+ relationship)
    /// to it -- exactly as Excel or a prior FreeX save would have left it. Mirrors R95/R96's identical
    /// single-drawing-part injection, generalized to a specific sheet's own drawing part so a
    /// two-sheet package can carry two INDEPENDENT chart hyperlinks.
    /// </summary>
    private static void InjectChartObjectHyperlink(ZipArchive archive, string sheetName, string chartName, string target)
    {
        var drawingPath = ResolveSheetDrawingPath(archive, sheetName);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
        var cNvPr = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame")
            .Select(gf => gf.Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!.Element(SpreadsheetDrawingNs + "cNvPr")!)
            .Single(pr => pr.Attribute("name")?.Value == chartName);
        var relId = "rIdTestObjectHlink";
        cNvPr.Add(new XElement(DrawingNs + "hlinkClick", new XAttribute(RelNs + "id", relId)));
        WritePackageXml(archive, drawingPath, drawingXml);

        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        var drawingRelsXml = archive.GetEntry(drawingRelsPath) is { } existingDrawingRels
            ? XlsxPackageTestFixtures.LoadPackageXml(existingDrawingRels)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));
        drawingRelsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", relId),
            new XAttribute("Type", HyperlinkRelationshipType),
            new XAttribute("Target", target),
            new XAttribute("TargetMode", "External")));
        WritePackageXml(archive, drawingRelsPath, drawingRelsXml);
    }

    private static string ResolveSheetDrawingPath(ZipArchive archive, string sheetName)
    {
        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
        var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        var sheetElement = workbookXml.Root!.Element(WorkbookNs + "sheets")!.Elements(WorkbookNs + "sheet")
            .Single(s => s.Attribute("name")?.Value == sheetName);
        var sheetRelId = sheetElement.Attribute(RelNs + "id")!.Value;
        var worksheetTarget = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Single(r => r.Attribute("Id")!.Value == sheetRelId).Attribute("Target")!.Value;
        var worksheetPath = XlsxPackagePath.NormalizeWorkbookTarget(worksheetTarget);

        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetPath);
        var drawingRelId = worksheetXml.Root!.Element(WorkbookNs + "drawing")!.Attribute(RelNs + "id")!.Value;
        var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetRelsPath);
        var drawingTarget = worksheetRelsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Single(r => r.Attribute("Id")!.Value == drawingRelId).Attribute("Target")!.Value;
        return XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, drawingTarget);
    }

    /// <summary>
    /// Reads <paramref name="sheetName"/>'s saved drawing part and, for every chart graphicFrame found,
    /// resolves its own chart part (via the graphicFrame's own c:chart/cx:chart relationship, never an
    /// assumed identity) to read that chart's Title text, then maps Title -&gt; resolved object-hyperlink
    /// target (null if none). Distinguishing by TITLE (not name) lets a test assert on two charts that
    /// deliberately share the same cNvPr@name (the misattribution collision under test).
    /// </summary>
    private static Dictionary<string, string?> ReadChartHyperlinksByTitle(MemoryStream saved, string sheetName)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingPath = ResolveSheetDrawingPath(archive, sheetName);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        var drawingRelsXml = archive.GetEntry(drawingRelsPath) is { } drawingRelsEntry
            ? XlsxPackageTestFixtures.LoadPackageXml(drawingRelsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));
        var drawingRelTargets = drawingRelsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Where(r => r.Attribute("Id") is not null && r.Attribute("Target") is not null)
            .ToDictionary(r => r.Attribute("Id")!.Value, r => r.Attribute("Target")!.Value);

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var graphicFrame in drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame"))
        {
            var cNvPr = graphicFrame.Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!.Element(SpreadsheetDrawingNs + "cNvPr")!;

            var graphicData = graphicFrame.Descendants(DrawingNs + "graphicData").First();
            var chartRelId = graphicData.Elements().First().Attribute(RelNs + "id")!.Value;
            var chartTarget = drawingRelTargets[chartRelId];
            var chartPath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, chartTarget);
            var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, chartPath);
            var title = chartXml.Root!.Element(ChartNs + "chart")!.Element(ChartNs + "title")?
                .Element(ChartNs + "tx")?.Element(ChartNs + "rich")?
                .Element(DrawingNs + "p")?.Element(DrawingNs + "r")?.Element(DrawingNs + "t")?.Value;
            title.Should().NotBeNullOrEmpty("every test chart is authored with a distinguishing Title");

            var hlinkClick = cNvPr.Element(DrawingNs + "hlinkClick");
            var relId = hlinkClick?.Attribute(RelNs + "id")?.Value;
            string? target = null;
            if (relId is not null)
            {
                var relationship = drawingRelsXml.Root!.Elements(PackageRelNs + "Relationship")
                    .Single(r => r.Attribute("Id")!.Value == relId);
                target = relationship.Attribute("Target")!.Value;
            }

            result[title!] = target;
        }

        return result;
    }

    /// <summary>
    /// A real, ordinary editing command (changing the given chart's type) that has NOTHING to do with
    /// hyperlinks but forces <see cref="XlsxFileAdapter.Save"/> off both fast paths (the "model
    /// unchanged, verbatim source copy" short-circuit AND the cell-value/drawing-part patch-safe
    /// byte-copy path) and onto the full ClosedXML-rebuild path -- the only path that runs
    /// <c>XlsxWorksheetChartWriter.Save</c> against a freshly-generated, chart-less package. Mirrors
    /// R95/R96's identical helper. Most of this file's scenarios (move/duplicate) already change the
    /// sheet's chart COUNT, which independently forces the same full-rebuild path (the drawing-part
    /// patch-safety guard requires the source drawing's chart-element count to match
    /// <c>sheet.Charts.Count</c> exactly) -- this helper is used regardless, for tests/paths where that
    /// isn't otherwise guaranteed (e.g. after a save+reload, or the same-sheet paste test where the
    /// sheet's chart count also changes but an explicit edit keeps the forcing mechanism uniform and
    /// obvious across every test in this file).
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

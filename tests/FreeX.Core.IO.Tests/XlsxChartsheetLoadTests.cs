using System.IO;
using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public class XlsxChartsheetLoadTests
{
    private static string ChartsheetCorpusPath() =>
        TestWorkspaceFiles.FindWorkspaceFile(
            "test-corpus", "public", "tealeg-xlsx", "testchartsheet.xlsx");

    // R76-io-chartsheet-4-2: renaming a loaded chartsheet used to fail to rename it on save --
    // the reclaim loop matched the source <sheet> (old name) against the ClosedXML-generated
    // targets (new name) by name only, missed, and fell into the "add a brand-new <sheet>" branch
    // under the OLD name while leaving the stray placeholder worksheet under the NEW name behind,
    // producing 3 sheets on reload instead of 2.
    [Fact]
    public void Save_AfterRenamingChartsheet_RenamesInPlaceWithoutStrayPlaceholder()
    {
        var adapter = new XlsxFileAdapter();
        using var source = File.OpenRead(ChartsheetCorpusPath());
        var workbook = adapter.Load(source);
        var originalSheetCount = workbook.Sheets.Count;
        var chart = workbook.GetSheet("Chart1")!;
        chart.Name = "RenamedChart";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        reloaded.Sheets.Should().HaveCount(originalSheetCount,
            "renaming a chartsheet must not leave a stray empty placeholder worksheet behind");
        reloaded.Sheets.Select(s => s.Name).Should().NotContain("Chart1",
            "the old chartsheet name must not survive as a second, resurrected sheet");
        var renamed = reloaded.GetSheet("RenamedChart");
        renamed.Should().NotBeNull();
        renamed!.IsChartsheet.Should().BeTrue("the renamed sheet must still be recognized as a chartsheet");
    }

    // Sibling no-regression: an untouched chartsheet elsewhere in the same save must still
    // round-trip exactly as before (already covered by Save_AfterEditingAnotherSheet_
    // PreservesTheChartsheetReference below); this adds the same guarantee from the rename
    // scenario's own perspective -- the OTHER (normal) sheet must be unaffected by the rename.
    [Fact]
    public void Save_AfterRenamingChartsheet_OtherWorksheetRoundTripsUnchanged()
    {
        var adapter = new XlsxFileAdapter();
        using var source = File.OpenRead(ChartsheetCorpusPath());
        var workbook = adapter.Load(source);
        workbook.GetSheet("Chart1")!.Name = "RenamedChart";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        var sheet1 = reloaded.GetSheet("Sheet1");
        sheet1.Should().NotBeNull();
        sheet1!.IsChartsheet.Should().BeFalse();
    }

    // R76-io-chartsheet-4-3: deleting a loaded chartsheet used to fail to delete it on save --
    // the reclaim loop is driven only by the source archive and the ClosedXML output, never the
    // live workbook.Sheets model, so the "add a brand-new <sheet>" fallback branch re-added the
    // chartsheet from the source archive even though the user had removed it from the model.
    [Fact]
    public void Save_AfterDeletingChartsheet_ChartsheetIsNotResurrected()
    {
        var adapter = new XlsxFileAdapter();
        using var source = File.OpenRead(ChartsheetCorpusPath());
        var workbook = adapter.Load(source);
        var chart = workbook.GetSheet("Chart1")!;
        workbook.RemoveSheet(chart.Id);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        reloaded.Sheets.Select(s => s.Name).Should().NotContain("Chart1",
            "a chartsheet removed from the model must not be resurrected on save");
        reloaded.Sheets.Should().ContainSingle().Which.Name.Should().Be("Sheet1");
    }

    // R78-meta-2: the rename-reattachment queue used to be populated from ANY renamed target
    // sheet with no relationship-type/chartsheet check, so deleting a chartsheet while
    // independently renaming an unrelated normal worksheet in the same save let the renamed
    // worksheet's <sheet> entry get dequeued as if it were the deleted chartsheet's placeholder --
    // rewiring the renamed worksheet onto the chartsheet part and losing its real worksheet
    // content.
    [Fact]
    public void Save_AfterDeletingChartsheetAndRenamingAnotherWorksheet_DoesNotMisattachChartToRenamedWorksheet()
    {
        var adapter = new XlsxFileAdapter();
        using var source = File.OpenRead(ChartsheetCorpusPath());
        var workbook = adapter.Load(source);
        var chart = workbook.GetSheet("Chart1")!;
        workbook.RemoveSheet(chart.Id);
        var sheet1 = workbook.GetSheet("Sheet1")!;
        sheet1.Name = "Renamed1";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        reloaded.Sheets.Select(s => s.Name).Should().NotContain("Chart1",
            "the deleted chartsheet must not be resurrected under the renamed worksheet's identity");
        var renamed = reloaded.GetSheet("Renamed1");
        renamed.Should().NotBeNull();
        renamed!.IsChartsheet.Should().BeFalse(
            "the renamed sheet is an ordinary worksheet, not a reattached chartsheet placeholder");
        renamed.GetCell(new CellAddress(renamed.Id, 2, 1))?.Value.Should().Be(new NumberValue(1),
            "the renamed worksheet's own real content must survive, not be replaced by the deleted chart");
    }

    // No-regression sibling: the genuine chartsheet-rename reattachment path (an actual chartsheet
    // renamed, not deleted) must still work correctly even when an unrelated normal worksheet is
    // ALSO renamed in the same save -- the new chartsheet-only filter on the queue must not
    // exclude a real chartsheet rename just because another rename is also present.
    [Fact]
    public void Save_AfterRenamingBothChartsheetAndAnotherWorksheet_BothRenamesApplyCorrectly()
    {
        var adapter = new XlsxFileAdapter();
        using var source = File.OpenRead(ChartsheetCorpusPath());
        var workbook = adapter.Load(source);
        workbook.GetSheet("Chart1")!.Name = "RenamedChart";
        workbook.GetSheet("Sheet1")!.Name = "Renamed1";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        reloaded.Sheets.Should().HaveCount(2,
            "renaming two sheets must not leave a stray placeholder or lose a sheet");
        var renamedChart = reloaded.GetSheet("RenamedChart");
        renamedChart.Should().NotBeNull();
        renamedChart!.IsChartsheet.Should().BeTrue("the renamed chartsheet must still be recognized as a chartsheet");
        var renamedWorksheet = reloaded.GetSheet("Renamed1");
        renamedWorksheet.Should().NotBeNull();
        renamedWorksheet!.IsChartsheet.Should().BeFalse();
        renamedWorksheet.GetCell(new CellAddress(renamedWorksheet.Id, 2, 1))?.Value.Should().Be(new NumberValue(1),
            "the renamed worksheet's own content must survive alongside the renamed chartsheet");
    }

    // Sibling no-regression: deleting a normal worksheet (never touched by this reclaim loop at
    // all, since it only processes non-worksheet relationship types) must leave an untouched
    // chartsheet elsewhere in the workbook completely unaffected. Inspect the saved package
    // directly rather than reloading it -- a workbook left with only a chartsheet and no real
    // worksheet is a pre-existing, unrelated ClosedXML/Excel restriction (a workbook needs at
    // least one worksheet), not something this fix is responsible for.
    [Fact]
    public void Save_AfterDeletingNormalWorksheet_ChartsheetStillRoundTrips()
    {
        var adapter = new XlsxFileAdapter();
        using var source = File.OpenRead(ChartsheetCorpusPath());
        var workbook = adapter.Load(source);
        var sheet1 = workbook.GetSheet("Sheet1")!;
        workbook.RemoveSheet(sheet1.Id);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        archive.GetEntry("xl/chartsheets/sheet1.xml").Should().NotBeNull(
            "the chartsheet package part must survive a save even after the sheet worksheet is deleted");
        ChartsheetReferenceTargets(archive)
            .Should().ContainSingle()
            .Which.Should().EndWith("chartsheets/sheet1.xml",
                "the workbook <sheet> entry must still point at the chartsheet part");
    }

    [Fact]
    public void Load_Testchartsheet_LoadsBothSheetsIncludingTheChartsheet()
    {
        using var stream = File.OpenRead(ChartsheetCorpusPath());
        var workbook = new XlsxFileAdapter().Load(stream);

        workbook.Sheets.Select(s => s.Name).Should().BeEquivalentTo(["Chart1", "Sheet1"]);
    }

    [Fact]
    public void Load_Testchartsheet_ChartsheetCarriesItsChartModel()
    {
        using var stream = File.OpenRead(ChartsheetCorpusPath());
        var workbook = new XlsxFileAdapter().Load(stream);

        var chartSheet = workbook.GetSheet("Chart1");
        chartSheet.Should().NotBeNull();
        chartSheet!.IsChartsheet.Should().BeTrue();
        chartSheet.ChartsheetChart.Should().NotBeNull();
        chartSheet.ChartsheetChart!.Type.Should().Be(ChartType.Line);
    }

    [Fact]
    public void Load_Testchartsheet_NormalWorksheetIsNotAChartsheet()
    {
        using var stream = File.OpenRead(ChartsheetCorpusPath());
        var workbook = new XlsxFileAdapter().Load(stream);

        workbook.GetSheet("Sheet1")!.IsChartsheet.Should().BeFalse();
    }

    [Fact]
    public void Inspect_Testchartsheet_DoesNotFlagUnsupportedSheetTypes()
    {
        using var stream = File.OpenRead(ChartsheetCorpusPath());
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind)
            .Should().NotContain(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes);
    }

    [Fact]
    public void Save_AfterEditingAnotherSheet_PreservesTheChartsheetReference()
    {
        var adapter = new XlsxFileAdapter();
        using var source = File.OpenRead(ChartsheetCorpusPath());
        var workbook = adapter.Load(source);

        // Edit a normal worksheet so the save path regenerates workbook.xml.
        var worksheet = workbook.GetSheet("Sheet1")!;
        worksheet.SetCell(new CellAddress(worksheet.Id, 10, 1), new TextValue("edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        archive.GetEntry("xl/chartsheets/sheet1.xml").Should().NotBeNull(
            "the chartsheet package part must survive a save");
        ChartsheetReferenceTargets(archive)
            .Should().ContainSingle()
            .Which.Should().EndWith("chartsheets/sheet1.xml",
                "the workbook <sheet> entry must still point at the chartsheet part");
    }

    private static IEnumerable<string> ChartsheetReferenceTargets(ZipArchive archive)
    {
        var workbookXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/workbook.xml")!);
        var relsXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        System.Xml.Linq.XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        System.Xml.Linq.XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        System.Xml.Linq.XNamespace pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var byId = relsXml.Root!
            .Elements(pkgRelNs + "Relationship")
            .Where(r => r.Attribute("Type")?.Value
                .EndsWith("/chartsheet", System.StringComparison.OrdinalIgnoreCase) == true)
            .ToDictionary(r => r.Attribute("Id")!.Value, r => r.Attribute("Target")!.Value);

        return workbookXml.Root!
            .Element(mainNs + "sheets")!
            .Elements(mainNs + "sheet")
            .Select(s => s.Attribute(relNs + "id")?.Value)
            .Where(id => id is not null && byId.ContainsKey(id))
            .Select(id => byId[id!]);
    }
}

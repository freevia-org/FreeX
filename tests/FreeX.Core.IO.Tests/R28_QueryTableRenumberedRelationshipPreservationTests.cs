using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R28-io-connections-querytable-deep-1: when a retained sheet's worksheet part is renumbered on a
/// full-rebuild save (e.g. an earlier sheet is deleted so every later sheet shifts down one physical
/// worksheetN.xml slot), the sheet's OLD relationships part used to be excluded wholesale with no
/// transplant step, silently dropping its worksheet -> queryTable relationship (the legacy External
/// Data Range / "Query Table" binding) even though the xl/queryTables/*.xml part itself survived as
/// an orphan. See XlsxFileAdapter.SourcePackage.cs (PreserveRenumberedWorksheetQueryTableRelationships).
/// </summary>
public sealed class R28_QueryTableRenumberedRelationshipPreservationTests
{
    private const string QueryTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    [Fact]
    public void RenumberedSheet_KeepsQueryTableRelationship_WhenAnEarlierSheetIsDeleted()
    {
        var workbook = new Workbook("QueryTableRenumber");
        var data = workbook.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("data"));
        var report = workbook.AddSheet("Report");
        report.SetCell(new CellAddress(report.Id, 1, 1), new TextValue("report"));
        var queryResult = workbook.AddSheet("QueryResult");
        queryResult.SetCell(new CellAddress(queryResult.Id, 1, 1), new TextValue("query"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);

        string queryResultSourcePath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            queryResultSourcePath = GetWorksheetPathForSheetName(archive, "QueryResult");
        queryResultSourcePath.Should().Be("xl/worksheets/sheet3.xml");

        AddQueryTableRelationship(source, queryResultSourcePath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.RemoveSheet(loaded.GetSheet("Data")!.Id);
        var loadedReport = loaded.GetSheet("Report")!;
        loadedReport.SetCell(new CellAddress(loadedReport.Id, 2, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.FullSave,
            "deleting a sheet is a structural edit that cannot go through the cell-value patch shortcut");

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var queryResultTargetPath = GetWorksheetPathForSheetName(savedArchive, "QueryResult");
        queryResultTargetPath.Should().Be(
            "xl/worksheets/sheet2.xml",
            "Report and QueryResult both shift down one worksheet slot once Data is deleted");

        savedArchive.GetEntry("xl/queryTables/queryTable1.xml").Should().NotBeNull(
            "the queryTable part itself already survives via the generic unknown-part passthrough");

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(queryResultTargetPath);
        var relsEntry = savedArchive.GetEntry(relsPath);
        relsEntry.Should().NotBeNull("the renumbered sheet must still have its own relationships part");
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry!);
        relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == QueryTableRelationshipType &&
                relationship.Attribute("Target")?.Value == "../queryTables/queryTable1.xml")
            .Should()
            .ContainSingle(
                "the worksheet -> queryTable relationship must be transplanted to the sheet's new " +
                "rels path on renumbering, not silently dropped");
    }

    // Sibling already-working case: the retained sheet holding the queryTable relationship keeps its
    // ORIGINAL worksheet path (no renumbering) across a full-rebuild save. This exercises the
    // pre-existing same-path relationship merge (XlsxPackageMetadataMerger's
    // IsQueryTablePackageGraphRelationship) and guards that the new renumber-transplant logic added
    // above does not regress or double-add the relationship when no renumbering ever happens.
    [Fact]
    public void UnchangedSheetPosition_KeepsQueryTableRelationship_OnFullRebuildSave()
    {
        var workbook = new Workbook("QueryTableNoRenumber");
        var report = workbook.AddSheet("Report");
        report.SetCell(new CellAddress(report.Id, 1, 1), new TextValue("report"));
        var queryResult = workbook.AddSheet("QueryResult");
        queryResult.SetCell(new CellAddress(queryResult.Id, 1, 1), new TextValue("query"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);

        string queryResultSourcePath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            queryResultSourcePath = GetWorksheetPathForSheetName(archive, "QueryResult");
        queryResultSourcePath.Should().Be("xl/worksheets/sheet2.xml");

        AddQueryTableRelationship(source, queryResultSourcePath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        // Force a full-rebuild save (bypassing the cell-value patch shortcut) while keeping every
        // sheet's position/path unchanged, per the established pattern in
        // XlsxBroaderRetentionChecksTests.LegacyDrawingHfExclusion.cs. The add+remove-sheet edit
        // alone leaves the model fingerprint identical to the source package (triggering the
        // unrelated "model unchanged" SourceCopy shortcut), so also make a genuine cell edit.
        var tempSheet = loaded.AddSheet("__TempForFullSave__");
        loaded.RemoveSheet(tempSheet.Id);
        var loadedReport = loaded.GetSheet("Report")!;
        loadedReport.SetCell(new CellAddress(loadedReport.Id, 2, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        GetWorksheetPathForSheetName(savedArchive, "QueryResult").Should().Be(queryResultSourcePath);

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(queryResultSourcePath);
        var relsEntry = savedArchive.GetEntry(relsPath);
        relsEntry.Should().NotBeNull();
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry!);
        relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == QueryTableRelationshipType &&
                relationship.Attribute("Target")?.Value == "../queryTables/queryTable1.xml")
            .Should()
            .ContainSingle("the queryTable relationship must survive a full-rebuild save even without renumbering");
    }

    private static string GetWorksheetPathForSheetName(ZipArchive archive, string sheetName)
    {
        var workbookXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/workbook.xml")!);
        var workbookRels = XlsxRelationshipReader.LoadTargets(archive, "xl/_rels/workbook.xml.rels", "xl/workbook.xml", PackageRelNs);
        return XlsxWorkbookSheetPathReader
            .GetWorkbookSheetPaths(workbookXml, workbookRels, WorksheetNs, RelNs)
            .Single(pair => pair.SheetName == sheetName)
            .WorksheetPath;
    }

    /// <summary>
    /// Patches a saved package to add a minimal legacy queryTable binding on <paramref name="worksheetPath"/>:
    /// xl/queryTables/queryTable1.xml plus the worksheet's own relationship pointing at it. Mirrors the
    /// package shape built by XlsxNonChartSchemaValidationTests.ConnectionsQueryTables.cs.
    /// </summary>
    private static void AddQueryTableRelationship(MemoryStream packageStream, string worksheetPath)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.Root!.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", "/xl/queryTables/queryTable1.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.queryTable+xml")));
        XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);

        var queryTableEntry = archive.CreateEntry("xl/queryTables/queryTable1.xml", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(queryTableEntry.Open()))
        {
            new XDocument(new XElement(
                WorksheetNs + "queryTable",
                new XAttribute("name", "FreeXQueryTable"),
                new XAttribute("connectionId", "1"))).Save(writer);
        }

        var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsEntry = archive.GetEntry(worksheetRelsPath);
        var worksheetRelsXml = worksheetRelsEntry is not null
            ? XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));
        worksheetRelsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXQueryTable"),
            new XAttribute("Type", QueryTableRelationshipType),
            new XAttribute("Target", "../queryTables/queryTable1.xml")));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);

        packageStream.Position = 0;
    }
}

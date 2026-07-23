using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R77-io-duplicate-sheet-querytable-1: FreeX never models a legacy/classic "Get External Data"
/// query table (xl/queryTables/*.xml + the worksheet's own relationship of type .../queryTable) at
/// all -- it survives a save purely via the generic source-package passthrough machinery, matched by
/// SHEET NAME between the loaded source package and the freshly generated package (see
/// XlsxFileAdapter.SourcePackage.cs, PreserveRenumberedWorksheetQueryTableRelationships). Duplicating
/// a sheet (Home &gt; Sheet &gt; Duplicate Sheet / "Create a copy") produces a brand-new sheet name
/// that never existed in the source package, so that name-keyed matching has nothing to attach the
/// queryTable relationship to -- the copy silently loses its query-table binding even though real
/// Excel duplicates the queryTable part (and its worksheet relationship) for the copied sheet.
/// </summary>
public sealed class R77_DuplicateSheetQueryTableTests
{
    private const string QueryTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void DuplicateSheet_ClonesQueryTablePartAndRelationship_ForTheCopiedSheet()
    {
        var workbook = new Workbook("QueryTableDuplicate");
        var report = workbook.AddSheet("Report");
        report.SetCell(new CellAddress(report.Id, 1, 1), new TextValue("report"));
        var queryResult = workbook.AddSheet("QueryResult");
        queryResult.SetCell(new CellAddress(queryResult.Id, 1, 1), new TextValue("query"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);

        string queryResultSourcePath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            queryResultSourcePath = GetWorksheetPathForSheetName(archive, "QueryResult");

        AddQueryTableRelationship(source, queryResultSourcePath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedQueryResult = loaded.GetSheet("QueryResult")!;
        var ctx = new TestCommandContext(loaded);

        new DuplicateSheetCommand(loadedQueryResult.Id).Apply(ctx).Success.Should().BeTrue();
        loaded.Sheets.Select(s => s.Name).Should().Contain("QueryResult (2)");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var copyWorksheetPath = GetWorksheetPathForSheetName(savedArchive, "QueryResult (2)");

        var copyRelsPath = XlsxPackagePath.GetRelationshipPartPath(copyWorksheetPath);
        var copyRelsEntry = savedArchive.GetEntry(copyRelsPath);
        copyRelsEntry.Should().NotBeNull("the duplicated sheet must carry its own relationships part");
        var copyRelsXml = XlsxPackageXmlEditor.LoadXml(copyRelsEntry!);
        var copyQueryTableRelationships = copyRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship => relationship.Attribute("Type")?.Value == QueryTableRelationshipType)
            .ToList();
        copyQueryTableRelationships.Should().ContainSingle(
            "Duplicate Sheet must clone the source sheet's worksheet -> queryTable relationship onto the copy");

        var copyQueryTableTarget = XlsxPackagePath.ResolveRelationshipTarget(
            copyWorksheetPath,
            copyQueryTableRelationships[0].Attribute("Target")!.Value);
        savedArchive.GetEntry(copyQueryTableTarget).Should().NotBeNull(
            "the cloned relationship must point at a real queryTable part in the saved package");

        // The clone must be its OWN part, not a second relationship aimed at the original sheet's
        // queryTable1.xml -- otherwise renaming/removing either sheet's query table later would
        // corrupt the other, and Excel itself always writes a distinct queryTableN.xml per sheet.
        var originalWorksheetPath = GetWorksheetPathForSheetName(savedArchive, "QueryResult");
        var originalRelsXml = XlsxPackageXmlEditor.LoadXml(
            savedArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(originalWorksheetPath))!);
        var originalQueryTableTarget = XlsxPackagePath.ResolveRelationshipTarget(
            originalWorksheetPath,
            originalRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Single(relationship => relationship.Attribute("Type")?.Value == QueryTableRelationshipType)
                .Attribute("Target")!.Value);
        copyQueryTableTarget.Should().NotBe(
            originalQueryTableTarget,
            "the duplicated sheet must get its own queryTable part, matching real Excel's Duplicate Sheet behavior");

        // The original (source) sheet must be completely unaffected.
        originalRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Count(relationship => relationship.Attribute("Type")?.Value == QueryTableRelationshipType)
            .Should().Be(1, "the source sheet's own queryTable relationship must be untouched, not duplicated");
    }

    // Sibling no-regression case: duplicating a sheet with no queryTable at all must still save
    // cleanly with no spurious queryTable relationship or part appearing anywhere.
    [Fact]
    public void DuplicateSheet_PlainSheetWithNoQueryTable_SavesCleanly_WithNoQueryTableIntroduced()
    {
        var workbook = new Workbook("PlainDuplicate");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var ctx = new TestCommandContext(loaded);

        new DuplicateSheetCommand(loadedSheet.Id).Apply(ctx).Success.Should().BeTrue();
        loaded.Sheets.Select(s => s.Name).Should().Contain("Sheet1 (2)");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        savedArchive.Entries.Should().NotContain(
            entry => entry.FullName.StartsWith("xl/queryTables/", StringComparison.OrdinalIgnoreCase),
            "duplicating a plain sheet with no query table must never fabricate one");

        var copyWorksheetPath = GetWorksheetPathForSheetName(savedArchive, "Sheet1 (2)");
        var copyRelsPath = XlsxPackagePath.GetRelationshipPartPath(copyWorksheetPath);
        var copyRelsEntry = savedArchive.GetEntry(copyRelsPath);
        if (copyRelsEntry is not null)
        {
            var copyRelsXml = XlsxPackageXmlEditor.LoadXml(copyRelsEntry);
            copyRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Should().NotContain(relationship => relationship.Attribute("Type") != null && relationship.Attribute("Type")!.Value == QueryTableRelationshipType);
        }
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
    /// package shape built by R28_QueryTableRenumberedRelationshipPreservationTests.
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

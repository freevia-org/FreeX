using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R102-io-rename-worksheet-exclusion-1: XlsxFileAdapter.SourcePackage.cs's
/// GetExcludedWorksheetPackagePartPaths (see PreserveSourcePackageParts) matches source sheets against
/// context.TargetSheets by SHEET NAME. context.SourceSheets is keyed by the sheet's name as it was in
/// the LOADED source package; context.TargetSheets is keyed by the sheet's name in the FRESHLY
/// GENERATED package (i.e. after any in-session rename has already been applied to the model and
/// re-serialized). Renaming a sheet therefore makes the lookup of the OLD name against the NEW-name-
/// keyed dictionary fail unconditionally -- indistinguishable from the sheet having been deleted -- so
/// every package part that survives ONLY via this source-preservation passthrough (e.g. a legacy
/// "Get External Data" queryTable, which FreeX has no in-model representation of at all -- see
/// R28_QueryTableRenumberedRelationshipPreservationTests / R77_DuplicateSheetQueryTableTests) is wrongly
/// treated as belonging to a removed sheet and silently dropped on save, even though the sheet's own
/// worksheetN.xml path is completely unchanged by a plain rename.
/// </summary>
public sealed class R102_RenameSheetPreservedPartsTests
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

    // Baseline: renaming the ONLY sheet that carries a preserved queryTable, with no other sheets in
    // the book. Isolates GetExcludedWorksheetPackagePartPaths from any renumbering path (the worksheet
    // part's physical path is unchanged by a pure rename).
    [Fact]
    public void RenameSheet_KeepsQueryTablePartAndRelationship_SingleSheetBook()
    {
        var workbook = new Workbook("RenameQueryTableSingle");
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

        new RenameSheetCommand(loadedQueryResult.Id, "QueryResultRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.FullSave,
            "renaming a sheet is a structural edit that must not go through the cell-value patch shortcut");

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedWorksheetPath = GetWorksheetPathForSheetName(savedArchive, "QueryResultRenamed");
        renamedWorksheetPath.Should().Be(
            queryResultSourcePath,
            "a pure rename must not renumber the worksheet part");

        savedArchive.GetEntry("xl/queryTables/queryTable1.xml").Should().NotBeNull(
            "the queryTable part itself must survive a plain rename via the generic unknown-part passthrough");

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(renamedWorksheetPath);
        var relsEntry = savedArchive.GetEntry(relsPath);
        relsEntry.Should().NotBeNull("the renamed sheet must still carry its own relationships part");
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry!);
        relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == QueryTableRelationshipType &&
                relationship.Attribute("Target")?.Value == "../queryTables/queryTable1.xml")
            .Should()
            .ContainSingle(
                "the worksheet -> queryTable relationship must survive a plain rename, not be dropped as if " +
                "the sheet had been deleted");
    }

    // Rename one of SEVERAL sheets -- the renamed sheet's own preserved part must survive, and the
    // other (untouched) sheets' own preserved parts must be completely unaffected.
    [Fact]
    public void RenameSheet_KeepsQueryTablePartAndRelationship_AmongMultipleSheets()
    {
        var workbook = new Workbook("RenameQueryTableMulti");
        var report = workbook.AddSheet("Report");
        report.SetCell(new CellAddress(report.Id, 1, 1), new TextValue("report"));
        var queryResult = workbook.AddSheet("QueryResult");
        queryResult.SetCell(new CellAddress(queryResult.Id, 1, 1), new TextValue("query"));
        var trailer = workbook.AddSheet("Trailer");
        trailer.SetCell(new CellAddress(trailer.Id, 1, 1), new TextValue("trailer"));

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

        // Rename an UNRELATED sheet too, to prove the fix keys off the renamed sheet's own identity
        // and doesn't require every sheet name to stay put.
        new RenameSheetCommand(loaded.GetSheet("Report")!.Id, "ReportRenamed").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(loadedQueryResult.Id, "QueryResultRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedWorksheetPath = GetWorksheetPathForSheetName(savedArchive, "QueryResultRenamed");
        renamedWorksheetPath.Should().Be(queryResultSourcePath);

        savedArchive.GetEntry("xl/queryTables/queryTable1.xml").Should().NotBeNull(
            "the queryTable part must survive when its own sheet is renamed alongside another sheet");

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(renamedWorksheetPath);
        var relsEntry = savedArchive.GetEntry(relsPath);
        relsEntry.Should().NotBeNull();
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry!);
        relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == QueryTableRelationshipType &&
                relationship.Attribute("Target")?.Value == "../queryTables/queryTable1.xml")
            .Should()
            .ContainSingle();
    }

    // Rename then rename BACK to the original name -- the round trip must not leave the part excluded
    // partway through, nor duplicate/corrupt anything.
    [Fact]
    public void RenameSheet_ThenRenameBack_KeepsQueryTablePartAndRelationship()
    {
        var workbook = new Workbook("RenameQueryTableRoundTrip");
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

        new RenameSheetCommand(loadedQueryResult.Id, "Temp").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(loadedQueryResult.Id, "QueryResult").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var finalWorksheetPath = GetWorksheetPathForSheetName(savedArchive, "QueryResult");
        finalWorksheetPath.Should().Be(queryResultSourcePath);

        savedArchive.GetEntry("xl/queryTables/queryTable1.xml").Should().NotBeNull(
            "renaming away and back to the original name must not drop the queryTable part");

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(finalWorksheetPath);
        var relsXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(relsPath)!);
        relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Count(relationship => relationship.Attribute("Type")?.Value == QueryTableRelationshipType)
            .Should().Be(1, "the round trip must not duplicate the relationship either");
    }

    // No-regression control: renaming a sheet that carries NO preserved parts at all must still save
    // cleanly (nothing to lose, and no spurious exclusion of anything else).
    [Fact]
    public void RenameSheet_PlainSheetWithNoPreservedParts_SavesCleanly()
    {
        var workbook = new Workbook("RenamePlain");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loadedSheet.Id, "Sheet1Renamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheet("Sheet1Renamed");
        reloadedSheet.Should().NotBeNull("the renamed sheet itself must survive the save/reload round trip");
        var reloadedCell = reloadedSheet!.GetCell(new CellAddress(reloadedSheet.Id, 1, 1));
        reloadedCell.Should().NotBeNull();
        reloadedCell!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("hello");
    }

    // Baseline no-rename control, for comparison with the rename cases above: the same queryTable
    // fixture must already survive an unrelated full-rebuild save when no sheet is renamed at all.
    [Fact]
    public void NoRename_KeepsQueryTablePartAndRelationship_OnFullRebuildSave()
    {
        var workbook = new Workbook("NoRenameQueryTable");
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
        var ctx = new TestCommandContext(loaded);

        // Force a full-rebuild save without touching any sheet name (pattern from
        // R28_QueryTableRenumberedRelationshipPreservationTests.UnchangedSheetPosition...).
        var tempSheet = loaded.AddSheet("__TempForFullSave__");
        loaded.RemoveSheet(tempSheet.Id);
        loaded.GetSheet("QueryResult")!.SetCell(new CellAddress(loaded.GetSheet("QueryResult")!.Id, 2, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        savedArchive.GetEntry("xl/queryTables/queryTable1.xml").Should().NotBeNull();
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
    /// package shape built by R28_QueryTableRenumberedRelationshipPreservationTests /
    /// R77_DuplicateSheetQueryTableTests.
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

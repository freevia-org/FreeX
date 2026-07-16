using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R47-io-pivot-cache-shared-items-3-4: NativeJsonAdapter dropped PivotCacheFieldModel.SharedItemKinds
/// entirely (ToPivotCacheField hardcoded null; FromPivotCacheField never read the property at all), so a
/// boolean shared item ("1"/"0", originally &lt;b v="1"/&gt;) lost its original element kind after a
/// native .fxl round-trip. On a later XLSX export, XlsxPivotTableWriter.Cache.cs's
/// InferPivotCacheSharedItemXml fallback (used whenever kind == '\0') tries double.TryParse before
/// bool.TryParse, so "1"/"0" is misclassified as a number (&lt;n v="1"/&gt;) instead of a boolean
/// (&lt;b v="1"/&gt;). Fixed by adding a SharedItemKinds property to the private PivotCacheFieldDto and
/// wiring it through both ToPivotCacheField and FromPivotCacheField.
/// </summary>
public sealed class R47_NativeJsonPivotCacheSharedItemKindTests
{
    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Workbook BuildWorkbookWithBooleanPivotCacheSharedItems()
    {
        var workbook = new Workbook("PivotCacheSharedItemKindTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Active"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new BoolValue(false));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:A3"
        });
        // Mirrors what XlsxPivotCacheReader produces for a genuine <b v="1"/><b v="0"/> sharedItems block:
        // string values "1"/"0" with their original element kind preserved as 'b'.
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel(
            "Active",
            SharedItems: ["1", "0"],
            SharedItemKinds: ['b', 'b']));

        // A PivotTableModel referencing the cache is required so the XLSX writer's featurePlan.HasPivotTables
        // gate is satisfied and XlsxPivotTableWriter.Save (which serializes the cache part) actually runs.
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 1))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    [Fact]
    public void NativeJsonRoundTrip_PreservesSharedItemKinds_SoXlsxExportKeepsBooleanNotNumber()
    {
        var workbook = BuildWorkbookWithBooleanPivotCacheSharedItems();

        var jsonStream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, jsonStream);
        jsonStream.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(jsonStream);

        var reloadedField = reloaded.PivotCaches.Should().ContainSingle().Subject.Fields
            .Should().Contain(field => field.Name == "Active").Subject;
        reloadedField.SharedItemKinds.Should().NotBeNull(
            "the shared items' original element kind must survive a native .fxl round-trip");
        reloadedField.SharedItemKinds.Should().Equal('b', 'b');

        // Downstream check: re-exporting to XLSX must keep the field boolean (<b v=.../>), not silently
        // reclassify it as numeric (<n v=.../>) because the kind tag was lost.
        var xlsxStream = new MemoryStream();
        new XlsxFileAdapter().Save(reloaded, xlsxStream);
        xlsxStream.Position = 0;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var cacheXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sharedItems = cacheXml.Root!
            .Element(ns + "cacheFields")!
            .Elements(ns + "cacheField")
            .Should().ContainSingle().Subject
            .Element(ns + "sharedItems")!;

        sharedItems.Elements(ns + "b").Should().HaveCount(2, "boolean shared items must stay boolean, not become <n>");
        sharedItems.Elements(ns + "n").Should().BeEmpty("a preserved boolean kind must not be reclassified as numeric");
    }

    [Fact]
    public void NativeJsonRoundTrip_FieldWithNoSharedItemKinds_StaysNullNoRegression()
    {
        // Sibling no-regression case: a field that never carried SharedItemKinds (e.g. FreeX-created,
        // items added programmatically without any original element kind) must keep round-tripping with
        // SharedItemKinds == null rather than picking up some synthetic value.
        var workbook = new Workbook("PivotCacheNoSharedItemKindTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:A2"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category", SharedItems: ["A"]));

        var jsonStream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, jsonStream);
        jsonStream.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(jsonStream);

        var reloadedField = reloaded.PivotCaches.Should().ContainSingle().Subject.Fields
            .Should().Contain(field => field.Name == "Category").Subject;
        reloadedField.SharedItemKinds.Should().BeNull();
        reloadedField.SharedItems.Should().Equal("A");
    }
}

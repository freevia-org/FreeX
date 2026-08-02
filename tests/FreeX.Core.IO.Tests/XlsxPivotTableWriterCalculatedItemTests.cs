using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R26-io-pivot-deep-3: CT_CalculatedItem requires a pivotArea child (minOccurs="1") identifying
/// the target field. XlsxPivotTableWriter previously emitted a childless calculatedItem element,
/// which is structurally invalid OOXML that real Excel repairs/drops on open.
///
/// R116-io-pivot-calcitem-part: calculatedItems is a child of CT_PivotCacheDefinition (ECMA-376
/// 18.10.1.3, confirmed via reflection: DocumentFormat.OpenXml.Spreadsheet.PivotCacheDefinition has a
/// CalculatedItems property, PivotTableDefinition does not), so it now lives in the shared
/// pivotCacheDefinitionN.xml part rather than pivotTableN.xml -- see
/// R116_PivotCalculatedItemCachePartLocationTests for the location fix itself.
/// </summary>
public class XlsxPivotTableWriterCalculatedItemTests
{
    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) BuildWorkbookWithPivot()
    {
        var workbook = new Workbook("PivotCalculatedItemTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B3"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Region"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 9, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 1));
        sheet.PivotTables.Add(pivot);

        return (workbook, sheet, pivot);
    }

    [Fact]
    public void Save_CalculatedItem_EmitsRequiredPivotAreaChildTargetingItsField()
    {
        var (workbook, _, pivot) = BuildWorkbookWithPivot();
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "East + West", "East+West"));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        // R116-io-pivot-calcitem-part: calculatedItems belongs on the shared pivotCacheDefinition part,
        // not pivotTableN.xml.
        var cacheXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!);

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var calculatedItem = cacheXml.Root!
            .Element(ns + "calculatedItems")!
            .Elements(ns + "calculatedItem")
            .Should().ContainSingle().Subject;

        calculatedItem.Attribute("field")!.Value.Should().Be("0");
        calculatedItem.Attribute("formula")!.Value.Should().Be("East+West");

        // CT_CalculatedItem: pivotArea is a required child (minOccurs="1").
        var pivotArea = calculatedItem.Element(ns + "pivotArea");
        pivotArea.Should().NotBeNull("Excel requires pivotArea to place the calculated item on its target field");

        var reference = pivotArea!
            .Element(ns + "references")!
            .Elements(ns + "reference")
            .Should().ContainSingle().Subject;
        reference.Attribute("field")!.Value.Should().Be("0");

        // The pivotTableDefinition part must NOT carry the (schema-invalid) element at all.
        var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);
        pivotXml.Root!.Element(ns + "calculatedItems").Should().BeNull(
            "CT_pivotTableDefinition has no calculatedItems child -- real Excel repairs/drops it there");
    }

    [Fact]
    public void Save_NoCalculatedItems_OmitsCalculatedItemsElement()
    {
        // Sibling already-working case: pivot tables without any calculated items must not gain a
        // spurious (and empty) calculatedItems element as a side effect of this fix.
        var (workbook, _, _) = BuildWorkbookWithPivot();

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);
        var cacheXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!);

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        pivotXml.Root!.Element(ns + "calculatedItems").Should().BeNull();
        cacheXml.Root!.Element(ns + "calculatedItems").Should().BeNull();
    }
}

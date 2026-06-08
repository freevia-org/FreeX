using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void PivotTable_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreatePivotTableSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithPivotTable_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreatePivotTableSourceWorkbook());
        var sourcePivotCaches = ReadWorkbookChildElement(source, "pivotCaches");
        var sourceWorkbookRelationships = ReadPackageRootElement(source, "xl/_rels/workbook.xml.rels");
        var sourceWorksheetRelationships = ReadPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels");
        var sourcePivotTableRelationships = ReadPackageRootElement(source, "xl/pivotTables/_rels/pivotTable1.xml.rels");
        var sourcePivotCacheDefinition = ReadPackageRootElement(source, "xl/pivotCache/pivotCacheDefinition1.xml");
        var sourcePivotCacheDefinitionRelationships = ReadPackageRootElement(source, "xl/pivotCache/_rels/pivotCacheDefinition1.xml.rels");
        var sourcePivotCacheRecords = ReadPackageRootElement(source, "xl/pivotCache/pivotCacheRecords1.xml");
        var sourcePivotTable = ReadPackageRootElement(source, "xl/pivotTables/pivotTable1.xml");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 10, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorkbookChildElement(saved, "pivotCaches")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePivotCaches.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/_rels/workbook.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/worksheets/_rels/sheet1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/pivotTables/_rels/pivotTable1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePivotTableRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/pivotCache/pivotCacheDefinition1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePivotCacheDefinition.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/pivotCache/_rels/pivotCacheDefinition1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePivotCacheDefinitionRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/pivotCache/pivotCacheRecords1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePivotCacheRecords.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/pivotTables/pivotTable1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePivotTable.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedPivotCache = reloaded.PivotCaches.Should().ContainSingle().Subject;
        reloadedPivotCache.CacheId.Should().Be(1);
        reloadedPivotCache.SourceType.Should().Be(PivotCacheSourceType.WorksheetRange);
        reloadedPivotCache.SourceSheetName.Should().Be("PivotData");
        reloadedPivotCache.SourceReference.Should().Be("A1:B3");
        reloadedPivotCache.PackagePart.Should().Be("xl/pivotCache/pivotCacheDefinition1.xml");
        reloadedPivotCache.Fields.Select(field => field.Name).Should().Equal("Category", "Amount");

        var reloadedPivotTable = reloaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        reloadedPivotTable.Name.Should().Be("PivotTable1");
        reloadedPivotTable.CacheId.Should().Be(1);
        reloadedPivotTable.TargetRange.Start.ToA1().Should().Be("A5");
        reloadedPivotTable.TargetRange.End.ToA1().Should().Be("B8");
        reloadedPivotTable.PackagePart.Should().Be("xl/pivotTables/pivotTable1.xml");
        reloadedPivotTable.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        reloadedPivotTable.DataFields.Should().ContainSingle().Which.Name.Should().Be("Sum of Amount");
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookPivotCachesForSchemaValidity()
    {
        using var source = Save(CreatePivotTableSourceWorkbook());
        var sourceRelationshipId = SetWorkbookPivotCachesInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.PivotCaches.Should().ContainSingle(cache => cache.CacheId == 1);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 10, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        SchemaErrors(saved).Should().BeEmpty();

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var pivotCaches = ReadWorkbookChildElement(saved, "pivotCaches");
        pivotCaches.Attribute("customPivotCachesFlag").Should().BeNull();
        pivotCaches.Element(workbookNs + "nativePivotCachesChild").Should().BeNull();

        var pivotCache = pivotCaches
            .Elements(workbookNs + "pivotCache")
            .Should()
            .ContainSingle()
            .Subject;
        pivotCache.Attribute("cacheId")!.Value.Should().Be("1");
        pivotCache.Attribute(relNs + "id")!.Value.Should().Be(sourceRelationshipId);
        pivotCache.Attribute("customPivotCacheFlag").Should().BeNull();
        pivotCache.Elements().Should().BeEmpty();
        ReadPackageRootElement(saved, "xl/pivotCache/pivotCacheDefinition1.xml")
            .Name
            .Should()
            .Be(workbookNs + "pivotCacheDefinition");
        ReadPackageRootElement(saved, "xl/pivotTables/pivotTable1.xml")
            .Name
            .Should()
            .Be(workbookNs + "pivotTableDefinition");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetCell(10, 4)!.Value.Should().Be(new NumberValue(42));
        var reloadedCache = reloaded.PivotCaches.Should().ContainSingle().Subject;
        reloadedCache.CacheId.Should().Be(1);
        reloadedCache.SourceType.Should().Be(PivotCacheSourceType.WorksheetRange);
        reloadedCache.SourceSheetName.Should().Be("PivotData");
        reloadedCache.SourceReference.Should().Be("A1:B3");
        reloadedCache.Fields.Select(field => field.Name).Should().Equal("Category", "Amount");
        var reloadedPivot = reloadedSheet.PivotTables.Should().ContainSingle().Subject;
        reloadedPivot.Name.Should().Be("PivotTable1");
        reloadedPivot.CacheId.Should().Be(1);
        reloadedPivot.RowFields.Should().ContainSingle();
        reloadedPivot.DataFields.Should().ContainSingle();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesLegacyPivotTableDefinitionRootAttributesForSchemaValidity()
    {
        using var source = Save(CreatePivotTableSourceWorkbook());
        SetPivotTableDefinitionLegacyRootAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 10, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();

        var pivotTable = ReadPackageRootElement(saved, "xl/pivotTables/pivotTable1.xml");
        pivotTable.Attribute("showGrandTotals").Should().BeNull();
        pivotTable.Attribute("showRowGrandTotals").Should().BeNull();
        pivotTable.Attribute("showColumnGrandTotals").Should().BeNull();
        pivotTable.Attribute("repeatItemLabels").Should().BeNull();
        pivotTable.Attribute("blankLineAfterItems").Should().BeNull();
        pivotTable.Attribute("rowGrandTotals")!.Value.Should().Be("0");
        pivotTable.Attribute("colGrandTotals")!.Value.Should().Be("1");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetCell(10, 4)!.Value.Should().Be(new NumberValue(42));
        reloaded.PivotCaches.Should().ContainSingle().Which.CacheId.Should().Be(1);
        var reloadedPivot = reloadedSheet.PivotTables.Should().ContainSingle().Subject;
        reloadedPivot.Name.Should().Be("PivotTable1");
        reloadedPivot.CacheId.Should().Be(1);
        reloadedPivot.ShowRowGrandTotals.Should().BeFalse();
        reloadedPivot.ShowColumnGrandTotals.Should().BeTrue();
        reloadedPivot.RowFields.Should().ContainSingle();
        reloadedPivot.DataFields.Should().ContainSingle();
    }

    private static Workbook CreatePivotTableSourceWorkbook()
    {
        var workbook = new Workbook("PivotTablePatchSave");
        var sheet = workbook.AddSheet("PivotData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 3, 2),
            TargetRange = Range(sheet, 5, 1, 8, 2),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    private static string SetWorkbookPivotCachesInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var pivotCaches = workbookXml.Root!.Element(workbookNs + "pivotCaches")!;
        pivotCaches.SetAttributeValue("customPivotCachesFlag", "removed");
        pivotCaches.Add(new XElement(workbookNs + "nativePivotCachesChild"));

        var pivotCache = pivotCaches.Element(workbookNs + "pivotCache")!;
        var relationshipId = pivotCache.Attribute(relNs + "id")!.Value;
        pivotCache.SetAttributeValue("cacheId", " 1 ");
        pivotCache.SetAttributeValue(relNs + "id", $" {relationshipId} ");
        pivotCache.SetAttributeValue("customPivotCacheFlag", "removed");
        pivotCache.Add(new XElement(workbookNs + "nativePivotCacheChild"));
        pivotCaches.Add(new XElement(
            workbookNs + "pivotCache",
            new XAttribute("cacheId", "bad"),
            new XAttribute(relNs + "id", "rIdInvalidPivotCache")));
        pivotCaches.Add(new XElement(
            workbookNs + "pivotCache",
            new XAttribute("cacheId", "1"),
            new XAttribute(relNs + "id", relationshipId)));
        pivotCaches.Add(new XElement(
            workbookNs + "pivotCache",
            new XAttribute("cacheId", "2"),
            new XAttribute(relNs + "id", relationshipId)));

        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        return relationshipId;
    }

    private static void SetPivotTableDefinitionLegacyRootAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);

        var pivotTableXml = LoadPackageXml(archive, "xl/pivotTables/pivotTable1.xml");
        var root = pivotTableXml.Root!;
        root.Attribute("rowGrandTotals")?.Remove();
        root.Attribute("colGrandTotals")?.Remove();
        root.SetAttributeValue("showGrandTotals", "1");
        root.SetAttributeValue("showRowGrandTotals", "0");
        root.SetAttributeValue("showColumnGrandTotals", "1");
        root.SetAttributeValue("repeatItemLabels", "0");
        root.SetAttributeValue("blankLineAfterItems", "1");

        ReplacePackageXml(archive, "xl/pivotTables/pivotTable1.xml", pivotTableXml);
    }
}

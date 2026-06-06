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

}

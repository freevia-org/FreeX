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
    public void StructuredTable_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("StructuredTable");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 1, 1, 3, 2),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Name"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Value"));
        sheet.StructuredTables.Add(table);

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithStructuredTable_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateStructuredTableSourceWorkbook());
        var sourceTablePart = ReadPackageRootElement(source, "xl/tables/table1.xml");
        var sourceTableParts = ReadWorksheetChildElement(source, "tableParts");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadPackageRootElement(saved, "xl/tables/table1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceTablePart.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "tableParts")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceTableParts.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void AutoFilter_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateAutoFilterSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithAutoFilter_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateAutoFilterSourceWorkbook());
        var sourceAutoFilter = ReadWorksheetChildElement(source, "autoFilter");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "autoFilter")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceAutoFilter.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void WorksheetSortStateAndDataConsolidation_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetSortStateAndDataConsolidationSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetSortStateAndDataConsolidation_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetSortStateAndDataConsolidationSourceWorkbook());
        var sourceSortState = ReadWorksheetChildElement(source, "sortState");
        var sourceDataConsolidate = ReadWorksheetChildElement(source, "dataConsolidate");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "sortState")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSortState.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "dataConsolidate")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceDataConsolidate.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void WorksheetSingleXmlCells_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetSingleXmlCellsSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetSingleXmlCells_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetSingleXmlCellsSourceWorkbook());
        var sourceSingleXmlCells = ReadWorksheetSingleCellTableRootElement(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadPackageRootElement(saved, "xl/worksheets/sheet1.xml")
            .Element(XName.Get("singleXmlCells", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))
            .Should()
            .BeNull();
        ReadWorksheetSingleCellTableRootElement(saved)
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSingleXmlCells.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void WorksheetCustomProperties_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetCustomPropertiesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetCustomProperties_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetCustomPropertiesSourceWorkbook());
        var sourceCustomProperties = ReadWorksheetChildElement(source, "customProperties");
        var sourceCustomPropertyPart = ReadWorksheetCustomPropertyPartBytes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "customProperties")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceCustomProperties.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetCustomPropertyPartBytes(saved).Should().Equal(sourceCustomPropertyPart);
    }

    [Fact]
    public void LoadedWorkbookFullSave_WhenWorksheetCustomPropertiesChange_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetCustomPropertiesSourceWorkbook());
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.CustomProperties[0] = sheet.CustomProperties[0] with { Id = 8 };
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_unsupported_model_delta");
        SchemaErrors(saved).Should().BeEmpty();
    }


    [Fact]
    public void WorksheetDiagnostics_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetDiagnosticsSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetDiagnostics_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetDiagnosticsSourceWorkbook());
        var sourceCellWatches = ReadWorksheetChildElement(source, "cellWatches");
        var sourceIgnoredErrors = ReadWorksheetChildElement(source, "ignoredErrors");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "cellWatches")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceCellWatches.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "ignoredErrors")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceIgnoredErrors.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void NamedRanges_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("NamedRanges");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        workbook.DefineNamedRange("MyRange", Range(sheet, 2, 1, 5, 1));
        workbook.DefineNamedRange("SingleCell", Range(sheet, 1, 1, 1, 1));

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithNamedRanges_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateNamedRangeSourceWorkbook());
        var sourceDefinedNames = ReadWorkbookChildElement(source, "definedNames");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorkbookChildElement(saved, "definedNames")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceDefinedNames.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void MergedCells_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("MergedCells");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Merged Header"));
        SeedNumericGrid(sheet);
        sheet.AddMergedRegion(Range(sheet, 1, 1, 1, 3));
        sheet.AddMergedRegion(Range(sheet, 2, 4, 4, 4));

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithMergedCells_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateMergedCellSourceWorkbook());
        var sourceMergeCells = ReadWorksheetChildElement(source, "mergeCells");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "mergeCells")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceMergeCells.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void Comments_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("Comments");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "First comment";
        sheet.Comments[new CellAddress(sheet.Id, 2, 2)] = "Second comment";

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithComments_ProducesSchemaValidWorkbook()
    {
        using var source = CreateLegacyCommentSourcePackage();
        var sourceComments = ReadPackageRootElement(source, "xl/comments1.xml");
        var sourceVmlDrawing = ReadPackageRootElement(source, "xl/drawings/vmlDrawing1.vml");
        var sourceWorksheetRelationships = ReadPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels");
        var sourceLegacyDrawing = ReadWorksheetChildElement(source, "legacyDrawing");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadPackageRootElement(saved, "xl/comments1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceComments.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/drawings/vmlDrawing1.vml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceVmlDrawing.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/worksheets/_rels/sheet1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetRelationships.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "legacyDrawing")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceLegacyDrawing.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void SheetProtection_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateSheetProtectionSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithSheetProtection_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateSheetProtectionSourceWorkbook());
        var sourceSheetProtection = ReadWorksheetChildElement(source, "sheetProtection");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "sheetProtection")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSheetProtection.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void FreezePanes_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("FreezePanes");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithFreezePanes_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateFreezePaneSourceWorkbook());
        var sourceSheetViews = ReadWorksheetChildElement(source, "sheetViews");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "sheetViews")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSheetViews.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void SplitPanes_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("SplitPanes");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.SplitRow = 3;
        sheet.SplitColumn = 2;
        sheet.ViewTopRow = 1;
        sheet.ViewLeftCol = 1;

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithSplitPanes_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateSplitPaneSourceWorkbook());
        var sourceSheetViews = ReadWorksheetChildElement(source, "sheetViews");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "sheetViews")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSheetViews.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void PhoneticProperties_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreatePhoneticPropertiesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithPhoneticProperties_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreatePhoneticPropertiesSourceWorkbook());
        var sourcePhoneticProperties = ReadWorksheetChildElement(source, "phoneticPr");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "phoneticPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePhoneticProperties.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void WorksheetOutlineAndFormat_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetOutlineAndFormatSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetOutlineAndFormat_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetOutlineAndFormatSourceWorkbook());
        var sourceSheetProperties = ReadWorksheetChildElement(source, "sheetPr");
        var sourceSheetFormat = ReadWorksheetChildElement(source, "sheetFormatPr");
        var sourceColumns = ReadWorksheetChildElement(source, "cols");
        var sourceRow3 = ReadWorksheetRowElement(source, 3);
        var sourceRow4 = ReadWorksheetRowElement(source, 4);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "sheetPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSheetProperties.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "sheetFormatPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSheetFormat.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "cols")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceColumns.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetRowElement(saved, 3)
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceRow3.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetRowElement(saved, 4)
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceRow4.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void PageLayout_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreatePageLayoutSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithPageLayout_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreatePageLayoutSourceWorkbook());
        var sourceSheetProperties = ReadWorksheetChildElement(source, "sheetPr");
        var sourcePrintOptions = ReadWorksheetChildElement(source, "printOptions");
        var sourcePageMargins = ReadWorksheetChildElement(source, "pageMargins");
        var sourcePageSetup = ReadWorksheetChildElement(source, "pageSetup");
        var sourceHeaderFooter = ReadWorksheetChildElement(source, "headerFooter");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "sheetPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSheetProperties.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "printOptions")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePrintOptions.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "pageMargins")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePageMargins.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "pageSetup")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePageSetup.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "headerFooter")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceHeaderFooter.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void ManualPageBreaks_UseExcelCompatibleSpanBounds()
    {
        var workbook = new Workbook("ManualPageBreaks");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(4);

        var worksheetXml = WorksheetXml(workbook);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rowBreak = worksheetXml.Root!
            .Element(worksheetNs + "rowBreaks")!
            .Element(worksheetNs + "brk")!;
        rowBreak.Attribute("max")!.Value.Should().Be("16383");
        rowBreak.Attribute("man")!.Value.Should().Be("1");

        var columnBreak = worksheetXml.Root!
            .Element(worksheetNs + "colBreaks")!
            .Element(worksheetNs + "brk")!;
        columnBreak.Attribute("max")!.Value.Should().Be("1048575");
        columnBreak.Attribute("man")!.Value.Should().Be("1");
        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithManualPageBreaks_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateManualPageBreakSourceWorkbook());
        var sourceRowBreaks = ReadWorksheetChildElement(source, "rowBreaks");
        var sourceColumnBreaks = ReadWorksheetChildElement(source, "colBreaks");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "rowBreaks")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceRowBreaks.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "colBreaks")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceColumnBreaks.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void CombinedNonChartFeatures_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("Combined");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.FrozenRows = 1;
        sheet.AddMergedRegion(Range(sheet, 1, 1, 1, 2));
        sheet.Comments[new CellAddress(sheet.Id, 3, 3)] = "Note";
        workbook.DefineNamedRange("Combined_Range", Range(sheet, 2, 1, 5, 2));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Type = DvType.Decimal,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    private static Workbook CreateStructuredTableSourceWorkbook()
    {
        var workbook = new Workbook("StructuredTablePatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 1, 1, 3, 2),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Name"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Value"));
        sheet.StructuredTables.Add(table);

        return workbook;
    }

    private static Workbook CreateAutoFilterSourceWorkbook()
    {
        var workbook = new Workbook("AutoFilterPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new BlankValue());
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(16));

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B5", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            ["North"],
            IncludeBlank: true));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            1,
            [],
            IncludeBlank: false,
            CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThan", "10")],
            CustomFiltersAnd: false,
            NativeCustomFiltersAttributes: null,
            NativeFilterXmls: []));
        return workbook;
    }

    private static Workbook CreateWorksheetSortStateAndDataConsolidationSourceWorkbook()
    {
        var workbook = new Workbook("SortConsolidationPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(9));

        sheet.SortState = new WorksheetSortStateModel
        {
            Reference = "A1:B5",
            CaseSensitive = true,
            SortMethod = "stroke",
            Conditions =
            [
                new WorksheetSortConditionModel
                {
                    Reference = "A2:A5",
                    Descending = true
                }
            ]
        };
        sheet.DataConsolidation = new WorksheetDataConsolidationModel
        {
            Function = "sum",
            LeftLabels = true,
            TopLabels = true,
            Link = true,
            References =
            [
                new WorksheetDataConsolidationReferenceModel
                {
                    Reference = "A1:B5",
                    Sheet = "Data"
                }
            ]
        };
        return workbook;
    }

    private static Workbook CreateWorksheetSingleXmlCellsSourceWorkbook()
    {
        var workbook = new Workbook("SingleXmlCellsPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Mapped text"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(12));
        sheet.SingleXmlCells = new WorksheetSingleXmlCellsModel
        {
            Cells =
            [
                new WorksheetSingleXmlCellModel
                {
                    Id = 1,
                    Reference = "A1",
                    XmlCellPropertyId = 1
                },
                new WorksheetSingleXmlCellModel
                {
                    Id = 2,
                    Reference = "B2",
                    XmlCellPropertyId = 2
                }
            ]
        };
        return workbook;
    }

    private static Workbook CreateWorksheetCustomPropertiesSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetCustomPropertiesPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Property"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(12));
        sheet.CustomProperties.Add(new WorksheetCustomProperty("FreeXModeledProperty", 7));
        return workbook;
    }

    private static Workbook CreateWorksheetDiagnosticsSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetDiagnosticsPatchSave");
        var sheet = workbook.AddSheet("Data");
        var ignoredAddress = new CellAddress(sheet.Id, 1, 1);
        var watchedAddress = new CellAddress(sheet.Id, 2, 2);

        sheet.SetCell(ignoredAddress, new TextValue("00123"));
        sheet.GetCell(ignoredAddress.Row, ignoredAddress.Col)!.IgnoreFormulaError = true;
        sheet.SetFormula(watchedAddress, "A1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(12));
        workbook.WatchedCells.Add(watchedAddress);
        return workbook;
    }

    private static Workbook CreateSheetProtectionSourceWorkbook()
    {
        var workbook = new Workbook("SheetProtectionPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Locked"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "secret";
        return workbook;
    }

    private static Workbook CreateFreezePaneSourceWorkbook()
    {
        var workbook = new Workbook("FreezePanePatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;
        return workbook;
    }

    private static Workbook CreateSplitPaneSourceWorkbook()
    {
        var workbook = new Workbook("SplitPanePatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.SplitRow = 3;
        sheet.SplitColumn = 2;
        sheet.ViewTopRow = 1;
        sheet.ViewLeftCol = 1;
        return workbook;
    }

    private static Workbook CreatePhoneticPropertiesSourceWorkbook()
    {
        var workbook = new Workbook("PhoneticPropertiesPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.PhoneticProperties = new WorksheetPhoneticProperties("1", "fullwidthKatakana", "center");
        return workbook;
    }

    private static Workbook CreateWorksheetOutlineAndFormatSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetOutlineAndFormatPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.DefaultColumnWidth = 10.5;
        sheet.DefaultRowHeight = 24.0;
        sheet.ColumnWidths[2] = 14.25;
        sheet.ColumnWidths[3] = 16.5;
        sheet.RowHeights[3] = 28.0;
        sheet.RowOutlineLevels[3] = 1;
        sheet.RowOutlineLevels[4] = 2;
        sheet.ColOutlineLevels[2] = 1;
        sheet.ColOutlineLevels[3] = 2;
        sheet.OutlineSummaryBelow = false;
        sheet.OutlineSummaryRight = false;
        sheet.ShowOutlineSymbols = false;
        sheet.ApplyOutlineStyles = true;
        sheet.SheetFormatMetadata = CreateWorksheetOutlineSheetFormatMetadata();
        return workbook;
    }

    private static NativeXmlPreserveBag CreateWorksheetOutlineSheetFormatMetadata()
    {
        var bag = new NativeXmlPreserveBag();
        bag.Set(
            "sheetFormatPr",
            """<e baseColWidth="12" zeroHeight="0" thickTop="1" thickBottom="0" outlineLevelRow="2" outlineLevelCol="2" />""");
        return bag;
    }

    private static Workbook CreatePageLayoutSourceWorkbook()
    {
        var workbook = new Workbook("PageLayoutPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Legal;
        sheet.PageMargins = new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1);
        sheet.HeaderMargin = 0.25;
        sheet.FooterMargin = 0.35;
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.CenterHorizontallyOnPage = true;
        sheet.CenterVerticallyOnPage = true;
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.FirstPageNumber = 3;
        sheet.UsePrinterDefaults = false;
        sheet.PrintCopies = 2;
        sheet.PrintBlackAndWhite = true;
        sheet.PrintDraftQuality = true;
        sheet.PrintQualityDpi = 600;
        sheet.PrintQualityVerticalDpi = 300;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Dash;
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 2);
        sheet.FitToPage = true;
        sheet.AutoPageBreaks = false;
        sheet.PageHeader = new WorksheetHeaderFooter("Left header", "Center header", "Right header");
        sheet.PageFooter = new WorksheetHeaderFooter("Left footer", "Page &[Page] of &[Pages]", "Right footer");
        sheet.FirstPageHeader = new WorksheetHeaderFooter("First header left", "First header center", "First header right");
        sheet.FirstPageFooter = new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right");
        sheet.EvenPageHeader = new WorksheetHeaderFooter("Even header left", "Even header center", "Even header right");
        sheet.EvenPageFooter = new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.DifferentOddEvenHeaderFooter = true;
        sheet.HeaderFooterScaleWithDocument = false;
        sheet.HeaderFooterAlignWithMargins = false;
        return workbook;
    }

    private static Workbook CreateManualPageBreakSourceWorkbook()
    {
        var workbook = new Workbook("ManualPageBreakPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(4);
        return workbook;
    }

    private static Workbook CreateMergedCellSourceWorkbook()
    {
        var workbook = new Workbook("MergedCellPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Merged Header"));
        SeedNumericGrid(sheet);
        sheet.AddMergedRegion(Range(sheet, 1, 1, 1, 3));
        sheet.AddMergedRegion(Range(sheet, 2, 4, 4, 4));
        return workbook;
    }

    private static Workbook CreateNamedRangeSourceWorkbook()
    {
        var workbook = new Workbook("NamedRangePatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        workbook.DefineNamedRange("MyRange", Range(sheet, 2, 1, 5, 1));
        workbook.DefineNamedRange("SingleCell", Range(sheet, 1, 1, 1, 1));
        return workbook;
    }

    private static XElement ReadWorkbookChildElement(Stream stream, string localName)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(ReadPackageRootElement(stream, "xl/workbook.xml").Element(workbookNs + localName)!);
    }

    private static MemoryStream CreateLegacyCommentSourcePackage() =>
        XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C2"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>source</t></is></c></row>
                    <row r="2"><c r="C2" t="inlineStr"><is><t>review</t></is></c></row>
                  </sheetData>
                  <legacyDrawing r:id="rId2"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
                </Relationships>
                """),
            (
                "xl/comments1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <authors>
                    <author>Excel Reviewer</author>
                  </authors>
                  <commentList>
                    <comment ref="C2" authorId="0">
                      <text><r><t>Original note</t></r></text>
                    </comment>
                  </commentList>
                </comments>
                """),
            (
                "xl/drawings/vmlDrawing1.vml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <xml xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="_x0000_s1025" type="#_x0000_t202" style="position:absolute;margin-left:80pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden" fillcolor="#ffffe1" o:insetmode="auto">
                    <v:fill color2="#ffffe1"/>
                    <v:shadow color="black" obscured="t"/>
                    <v:path o:connecttype="none"/>
                    <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                    <x:ClientData ObjectType="Note">
                      <x:MoveWithCells/>
                      <x:SizeWithCells/>
                      <x:Anchor>2, 15, 1, 2, 4, 15, 5, 3</x:Anchor>
                      <x:AutoFill>False</x:AutoFill>
                      <x:Row>1</x:Row>
                      <x:Column>2</x:Column>
                    </x:ClientData>
                  </v:shape>
                </xml>
                """));

    private static XElement ReadPackageRootElement(Stream stream, string entryName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        return new XElement(LoadPackageXml(archive.GetEntry(entryName)!).Root!);
    }

    private static XElement ReadWorksheetSingleCellTableRootElement(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string relationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableSingleCells";
        const string worksheetPath = "xl/worksheets/sheet1.xml";

        var relsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        var relationship = relsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Single(element => element.Attribute("Type")?.Value == relationshipType);
        var partPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, relationship.Attribute("Target")!.Value);
        return new XElement(LoadPackageXml(archive.GetEntry(partPath)!).Root!);
    }

    private static byte[] ReadWorksheetCustomPropertyPartBytes(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string relationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customProperty";
        const string worksheetPath = "xl/worksheets/sheet1.xml";

        var relsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        var relationship = relsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Single(element => element.Attribute("Type")?.Value == relationshipType);
        var partPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, relationship.Attribute("Target")!.Value);
        using var partStream = archive.GetEntry(partPath)!.Open();
        using var bytes = new MemoryStream();
        partStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static XElement ReadWorksheetRowElement(Stream stream, uint row)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        return new XElement(worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Elements(worksheetNs + "row")
            .Single(element => element.Attribute("r")?.Value == $"{row}"));
    }

}

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
    public void StructuredTableAutoFilter_SanitizesInvalidAttributesForSchemaValidity()
    {
        using var saved = Save(CreateInvalidStructuredTableAutoFilterSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var autoFilter = ReadPackageRootElement(saved, "xl/tables/table1.xml")
            .Element(XName.Get("autoFilter", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))!;
        var workbookNs = autoFilter.Name.Namespace;
        var columns = autoFilter.Elements(workbookNs + "filterColumn").ToArray();
        columns.Should().HaveCount(3);
        autoFilter.Attribute("customAttr").Should().BeNull();
        autoFilter.Element(workbookNs + "nativeAutoFilterChild").Should().BeNull();

        columns[0].Attribute("hiddenButton").Should().BeNull();
        columns[0].Attribute("showButton").Should().BeNull();
        columns[0].Attribute("customFilterColumnFlag").Should().BeNull();
        var filters = columns[0].Element(workbookNs + "filters")!;
        filters.Attribute("blank").Should().BeNull();
        filters.Attribute("filtersFlag").Should().BeNull();
        var filter = filters.Element(workbookNs + "filter")!;
        filter.Attribute("filterFlag").Should().BeNull();
        filter.Elements().Should().BeEmpty();

        var customFilters = columns[1].Element(workbookNs + "customFilters")!;
        customFilters.Attribute("and").Should().BeNull();
        customFilters.Attribute("customFiltersFlag").Should().BeNull();
        var customFilter = customFilters.Element(workbookNs + "customFilter")!;
        customFilter.Attribute("operator").Should().BeNull();
        customFilter.Attribute("customFilterFlag").Should().BeNull();
        customFilter.Elements().Should().BeEmpty();

        var top10 = columns[2].Element(workbookNs + "top10")!;
        top10.Attribute("top").Should().BeNull();
        top10.Attribute("percent").Should().BeNull();
        top10.Attribute("val")!.Value.Should().Be("10");
        top10.Attribute("filterVal").Should().BeNull();
        top10.Attribute("customTop10Flag").Should().BeNull();
    }

    [Fact]
    public void StructuredTableSortState_SanitizesInvalidAttributesForSchemaValidity()
    {
        using var saved = Save(CreateInvalidStructuredTableSortStateSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var sortState = ReadPackageRootElement(saved, "xl/tables/table1.xml")
            .Element(XName.Get("sortState", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))!;
        var workbookNs = sortState.Name.Namespace;
        var condition = sortState.Element(workbookNs + "sortCondition")!;
        sortState.Attribute("columnSort").Should().BeNull();
        sortState.Attribute("caseSensitive").Should().BeNull();
        sortState.Attribute("sortMethod").Should().BeNull();
        condition.Attribute("descending").Should().BeNull();
        condition.Attribute("sortBy").Should().BeNull();
        condition.Attribute("dxfId").Should().BeNull();
        condition.Attribute("iconId").Should().BeNull();
    }

    [Fact]
    public void StructuredTableMetadata_SanitizesInvalidAttributesForSchemaValidity()
    {
        using var saved = Save(CreateInvalidStructuredTableMetadataSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        AssertStructuredTableMetadataSanitized(saved);
    }

    [Fact]
    public void StructuredTableExtensionLists_SanitizesInvalidNativeMetadataForSchemaValidity()
    {
        using var saved = Save(CreateInvalidStructuredTableExtensionListSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        AssertStructuredTableExtensionListsSanitized(saved);
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
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
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
    public void LoadedWorkbookPatchSave_SanitizesInvalidStructuredTableAutoFilterForSchemaValidity()
    {
        using var source = Save(CreateStructuredTableSourceWorkbook());
        SetStructuredTableAutoFilterInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var autoFilter = ReadPackageRootElement(saved, "xl/tables/table1.xml")
            .Element(XName.Get("autoFilter", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))!;
        var workbookNs = autoFilter.Name.Namespace;
        autoFilter.Attribute("customAttr").Should().BeNull();
        autoFilter.Element(workbookNs + "nativeAutoFilterChild").Should().BeNull();
        autoFilter.Element(workbookNs + "extLst").Should().NotBeNull();
        var firstColumn = autoFilter.Elements(workbookNs + "filterColumn").First();
        firstColumn.Attribute("hiddenButton").Should().BeNull();
        firstColumn.Attribute("showButton").Should().BeNull();
        firstColumn.Attribute("customFilterColumnFlag").Should().BeNull();
        var filters = firstColumn.Element(workbookNs + "filters")!;
        filters.Attribute("blank").Should().BeNull();
        filters.Attribute("filtersFlag").Should().BeNull();
        filters.Element(workbookNs + "filter")!.Attribute("filterFlag").Should().BeNull();
        filters.Element(workbookNs + "filter")!.Elements().Should().BeEmpty();

        var customFilters = autoFilter
            .Elements(workbookNs + "filterColumn")
            .Skip(1)
            .First()
            .Element(workbookNs + "customFilters")!;
        customFilters.Attribute("and").Should().BeNull();
        customFilters.Attribute("customFiltersFlag").Should().BeNull();
        var customFilter = customFilters.Element(workbookNs + "customFilter")!;
        customFilter.Attribute("operator").Should().BeNull();
        customFilter.Attribute("customFilterFlag").Should().BeNull();
        customFilter.Elements().Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidStructuredTableSortStateForSchemaValidity()
    {
        using var source = Save(CreateStructuredTableSourceWorkbook());
        SetStructuredTableSortStateInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var sortState = ReadPackageRootElement(saved, "xl/tables/table1.xml")
            .Element(XName.Get("sortState", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))!;
        var workbookNs = sortState.Name.Namespace;
        var condition = sortState.Element(workbookNs + "sortCondition")!;
        sortState.Attribute("columnSort").Should().BeNull();
        sortState.Attribute("caseSensitive").Should().BeNull();
        sortState.Attribute("sortMethod").Should().BeNull();
        condition.Attribute("descending").Should().BeNull();
        condition.Attribute("sortBy").Should().BeNull();
        condition.Attribute("dxfId").Should().BeNull();
        condition.Attribute("iconId").Should().BeNull();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidStructuredTableMetadataForSchemaValidity()
    {
        using var source = Save(CreateStructuredTableSourceWorkbook());
        SetStructuredTableMetadataInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertStructuredTableMetadataSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidStructuredTableExtensionListsForSchemaValidity()
    {
        using var source = Save(CreateStructuredTableSourceWorkbook());
        SetStructuredTableExtensionListsInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertStructuredTableExtensionListsSanitized(saved);
    }


    [Fact]
    public void AutoFilter_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateAutoFilterSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void AutoFilter_SanitizesInvalidAttributesForSchemaValidity()
    {
        using var saved = Save(CreateInvalidAutoFilterSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var autoFilter = ReadWorksheetChildElement(saved, "autoFilter");
        var worksheetNs = autoFilter.Name.Namespace;
        var columns = autoFilter.Elements(worksheetNs + "filterColumn").ToArray();
        columns.Should().HaveCount(6);

        columns[0].Attribute("hiddenButton").Should().BeNull();
        columns[0].Attribute("showButton").Should().BeNull();
        columns[0].Attribute("customFilterColumnFlag").Should().BeNull();
        var filters = columns[0].Element(worksheetNs + "filters")!;
        filters.Attribute("blank").Should().BeNull();
        filters.Attribute("calendarType").Should().BeNull();
        filters.Attribute("filtersFlag").Should().BeNull();
        filters.Element(worksheetNs + "filter")!.Attribute("filterFlag").Should().BeNull();
        filters.Element(worksheetNs + "filter")!.Elements().Should().BeEmpty();
        filters.Elements(worksheetNs + "dateGroupItem").Should().BeEmpty();

        var customFilters = columns[1].Element(worksheetNs + "customFilters")!;
        customFilters.Attribute("and").Should().BeNull();
        customFilters.Attribute("customFiltersFlag").Should().BeNull();
        var customFilter = customFilters.Element(worksheetNs + "customFilter")!;
        customFilter.Attribute("operator").Should().BeNull();
        customFilter.Attribute("customFilterFlag").Should().BeNull();
        customFilter.Elements().Should().BeEmpty();

        var top10 = columns[2].Element(worksheetNs + "top10")!;
        top10.Attribute("top").Should().BeNull();
        top10.Attribute("percent").Should().BeNull();
        top10.Attribute("val")!.Value.Should().Be("10");
        top10.Attribute("filterVal").Should().BeNull();
        top10.Attribute("customTop10Flag").Should().BeNull();

        var dynamicFilter = columns[3].Element(worksheetNs + "dynamicFilter")!;
        dynamicFilter.Attribute("type")!.Value.Should().Be("aboveAverage");
        dynamicFilter.Attribute("val").Should().BeNull();
        dynamicFilter.Attribute("maxVal").Should().BeNull();
        dynamicFilter.Attribute("customDynamicFilterFlag").Should().BeNull();

        columns[4].Element(worksheetNs + "colorFilter").Should().BeNull();
        columns[5].Element(worksheetNs + "iconFilter").Should().BeNull();
    }

    [Fact]
    public void AutoFilterAndSortStateExtensionLists_SanitizesInvalidNativeMetadataForSchemaValidity()
    {
        using var saved = Save(CreateInvalidWorksheetFilterSortExtensionListSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetFilterSortExtensionListsSanitized(saved);
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
    public void LoadedWorkbookPatchSave_SanitizesInvalidAutoFilterAttributesForSchemaValidity()
    {
        using var source = Save(CreateAutoFilterSourceWorkbook());
        SetWorksheetAutoFilterInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var autoFilter = ReadWorksheetChildElement(saved, "autoFilter");
        var worksheetNs = autoFilter.Name.Namespace;
        var firstColumn = autoFilter.Elements(worksheetNs + "filterColumn").First();
        firstColumn.Attribute("hiddenButton").Should().BeNull();
        firstColumn.Attribute("showButton").Should().BeNull();
        firstColumn.Attribute("customFilterColumnFlag").Should().BeNull();
        var filters = firstColumn.Element(worksheetNs + "filters")!;
        filters.Attribute("blank").Should().BeNull();
        filters.Attribute("calendarType").Should().BeNull();
        filters.Attribute("filtersFlag").Should().BeNull();
        filters.Element(worksheetNs + "filter")!.Attribute("filterFlag").Should().BeNull();
        filters.Element(worksheetNs + "filter")!.Elements().Should().BeEmpty();
        filters.Elements(worksheetNs + "dateGroupItem").Should().BeEmpty();

        var customFilters = autoFilter
            .Elements(worksheetNs + "filterColumn")
            .Skip(1)
            .First()
            .Element(worksheetNs + "customFilters")!;
        customFilters.Attribute("and").Should().BeNull();
        customFilters.Attribute("customFiltersFlag").Should().BeNull();
        var customFilter = customFilters.Element(worksheetNs + "customFilter")!;
        customFilter.Attribute("operator").Should().BeNull();
        customFilter.Attribute("customFilterFlag").Should().BeNull();
        customFilter.Elements().Should().BeEmpty();
    }


    [Fact]
    public void WorksheetSortStateAndDataConsolidation_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetSortStateAndDataConsolidationSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void WorksheetSortState_SanitizesInvalidAttributesForSchemaValidity()
    {
        var workbook = CreateWorksheetSortStateAndDataConsolidationSourceWorkbook();
        var sortState = workbook.GetSheetAt(0).SortState!;
        sortState.SortMethod = "invalid";
        sortState.Conditions[0].SortBy = "invalid";
        sortState.Conditions[0].DxfId = "not-a-number";
        sortState.Conditions[0].IconId = "not-a-number";

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var savedSortState = ReadWorksheetChildElement(saved, "sortState");
        var savedCondition = savedSortState.Element(savedSortState.Name.Namespace + "sortCondition")!;
        savedSortState.Attribute("sortMethod").Should().BeNull();
        savedCondition.Attribute("sortBy").Should().BeNull();
        savedCondition.Attribute("dxfId").Should().BeNull();
        savedCondition.Attribute("iconId").Should().BeNull();
    }

    [Fact]
    public void WorksheetDataConsolidation_SanitizesInvalidAttributesForSchemaValidity()
    {
        var workbook = CreateWorksheetSortStateAndDataConsolidationSourceWorkbook();
        var dataConsolidation = workbook.GetSheetAt(0).DataConsolidation!;
        dataConsolidation.Function = "invalid";
        dataConsolidation.NativeAttributes["startLabels"] = "maybe";
        dataConsolidation.NativeAttributes["customDataConsolidationFlag"] = "removed";
        dataConsolidation.References[0].NativeAttributes["customDataRefFlag"] = "removed";

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var dataConsolidate = ReadWorksheetChildElement(saved, "dataConsolidate");
        dataConsolidate.Attribute("function").Should().BeNull();
        dataConsolidate.Attribute("startLabels").Should().BeNull();
        dataConsolidate.Attribute("customDataConsolidationFlag").Should().BeNull();
        dataConsolidate.Descendants(dataConsolidate.Name.Namespace + "dataRef").Should().ContainSingle()
            .Which.Attribute("customDataRefFlag").Should().BeNull();
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
    public void LoadedWorkbookPatchSave_SanitizesInvalidSortStateAttributesForSchemaValidity()
    {
        using var source = Save(CreateWorksheetSortStateAndDataConsolidationSourceWorkbook());
        SetWorksheetSortStateInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var sortState = ReadWorksheetChildElement(saved, "sortState");
        var condition = sortState.Element(sortState.Name.Namespace + "sortCondition")!;
        sortState.Attribute("sortMethod").Should().BeNull();
        condition.Attribute("sortBy").Should().BeNull();
        condition.Attribute("dxfId").Should().BeNull();
        condition.Attribute("iconId").Should().BeNull();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidAutoFilterAndSortStateExtensionListsForSchemaValidity()
    {
        using var source = Save(CreateWorksheetFilterSortExtensionListBaseWorkbook());
        SetWorksheetFilterSortExtensionListsInvalidNativeMetadata(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetFilterSortExtensionListsSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidDataConsolidationAttributesForSchemaValidity()
    {
        using var source = Save(CreateWorksheetSortStateAndDataConsolidationSourceWorkbook());
        SetWorksheetDataConsolidationInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var dataConsolidate = ReadWorksheetChildElement(saved, "dataConsolidate");
        var dataRefs = dataConsolidate.Element(dataConsolidate.Name.Namespace + "dataRefs")!;
        dataConsolidate.Attribute("function").Should().BeNull();
        dataConsolidate.Attribute("leftLabels").Should().BeNull();
        dataConsolidate.Attribute("startLabels").Should().BeNull();
        dataConsolidate.Attribute("topLabels").Should().BeNull();
        dataConsolidate.Attribute("link").Should().BeNull();
        dataConsolidate.Attribute("customDataConsolidationFlag").Should().BeNull();
        dataConsolidate.Element(dataConsolidate.Name.Namespace + "nativeDataConsolidateChild").Should().BeNull();
        dataRefs.Attribute("count")!.Value.Should().Be("1");
        dataRefs.Attribute("customDataRefsFlag").Should().BeNull();
        dataRefs.Element(dataConsolidate.Name.Namespace + "nativeDataRefsChild").Should().BeNull();
        var dataRef = dataRefs.Element(dataConsolidate.Name.Namespace + "dataRef")!;
        dataRef.Attribute("customDataRefFlag").Should().BeNull();
        dataRef.Element(dataConsolidate.Name.Namespace + "nativeDataRefChild").Should().BeNull();
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
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetCustomPropertiesForSchemaValidity()
    {
        using var source = Save(CreateWorksheetCustomPropertiesSourceWorkbook());
        SetWorksheetCustomPropertiesInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.CustomProperties[0] = sheet.CustomProperties[0] with { Id = 8 };
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetCustomPropertiesSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetCustomPropertiesForSchemaValidity()
    {
        using var source = Save(CreateWorksheetCustomPropertiesSourceWorkbook());
        SetWorksheetCustomPropertiesInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetCustomPropertiesSanitized(saved);
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
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetCellWatchesForSchemaValidity()
    {
        using var source = Save(CreateWorksheetDiagnosticsSourceWorkbook());
        SetWorksheetCellWatchesInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetCellWatchesSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetCellWatchesForSchemaValidity()
    {
        using var source = Save(CreateWorksheetDiagnosticsSourceWorkbook());
        SetWorksheetCellWatchesInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetCellWatchesSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetIgnoredErrorsForSchemaValidity()
    {
        using var source = Save(CreateWorksheetDiagnosticsSourceWorkbook());
        SetWorksheetIgnoredErrorsInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(2, 2)!.IgnoreFormulaError = true;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetIgnoredErrorsSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetIgnoredErrorsForSchemaValidity()
    {
        using var source = Save(CreateWorksheetDiagnosticsSourceWorkbook());
        SetWorksheetIgnoredErrorsInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetIgnoredErrorsSanitized(saved);
    }


    [Fact]
    public void WorksheetScenarios_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetScenariosSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetScenarios_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetScenariosSourceWorkbook());
        var sourceScenarios = ReadWorksheetChildElement(source, "scenarios");
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
        ReadWorksheetChildElement(saved, "scenarios")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceScenarios.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void ProtectedRanges_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateProtectedRangesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithProtectedRanges_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateProtectedRangesSourceWorkbook());
        var sourceProtectedRanges = ReadWorksheetChildElement(source, "protectedRanges");
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
        ReadWorksheetChildElement(saved, "protectedRanges")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceProtectedRanges.ToString(SaveOptions.DisableFormatting));
    }


    [Fact]
    public void WorksheetCalculationProperties_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetCalculationPropertiesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetCalculationProperties_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetCalculationPropertiesSourceWorkbook());
        var sourceCalculationProperties = ReadWorksheetChildElement(source, "sheetCalcPr");
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
        ReadWorksheetChildElement(saved, "sheetCalcPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceCalculationProperties.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetCalculationPropertiesForSchemaValidity()
    {
        using var source = Save(CreateWorksheetCalculationPropertiesSourceWorkbook());
        SetWorksheetCalculationPropertiesInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetCalculationPropertiesSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetCalculationPropertiesForSchemaValidity()
    {
        using var source = Save(CreateWorksheetCalculationPropertiesSourceWorkbook());
        SetWorksheetCalculationPropertiesInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(77));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetCalculationPropertiesSanitized(saved);
    }


    [Fact]
    public void CustomSheetViews_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateCustomSheetViewsSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithCustomSheetViews_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateCustomSheetViewsSourceWorkbook());
        var sourceWorkbookViews = ReadWorkbookChildElement(source, "customWorkbookViews");
        var sourceSheetViews = ReadWorksheetChildElement(source, "customSheetViews");
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
        ReadWorkbookChildElement(saved, "customWorkbookViews")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookViews.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "customSheetViews")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSheetViews.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidCustomWorkbookViewsForSchemaValidity()
    {
        using var source = Save(CreateCustomSheetViewsSourceWorkbook());
        SetCustomWorkbookViewsInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var customWorkbookViews = ReadWorkbookChildElement(saved, "customWorkbookViews");
        customWorkbookViews.Attribute("customCustomWorkbookViewsFlag").Should().BeNull();
        customWorkbookViews.Element(customWorkbookViews.Name.Namespace + "nativeCustomWorkbookViewsChild").Should().BeNull();
        var customWorkbookView = customWorkbookViews.Elements(customWorkbookViews.Name.Namespace + "customWorkbookView").Single();
        AssertInvalidCustomWorkbookViewAttributesRemoved(customWorkbookView);
    }


    [Fact]
    public void WorksheetAdditionalViews_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetAdditionalViewsSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void WorksheetAdditionalViews_SanitizesInvalidSheetViewAttributesForSchemaValidity()
    {
        var workbook = CreateWorksheetAdditionalViewsSourceWorkbook();
        var additionalView = workbook.GetSheetAt(0).AdditionalViews!.Views[0];
        additionalView.NativeXml = """
            <sheetView xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" workbookViewId="1" view="invalid" showGridLines="maybe" zoomScale="not-a-number" topLeftCell="BAD" customSheetViewAttr="removed">
              <pane xSplit="not-a-number" topLeftCell="BAD" activePane="badPane" state="badState" customPaneAttr="removed" />
              <selection pane="badPane" activeCell="BAD" activeCellId="not-a-number" sqref="BAD" customSelectionAttr="removed" />
            </sheetView>
            """;

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var sheetViews = ReadWorksheetChildElement(saved, "sheetViews");
        var sheetView = sheetViews.Elements(sheetViews.Name.Namespace + "sheetView")
            .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value, "1", StringComparison.Ordinal));
        var pane = sheetView.Element(sheetView.Name.Namespace + "pane")!;
        var selection = sheetView.Element(sheetView.Name.Namespace + "selection")!;
        sheetView.Attribute("view").Should().BeNull();
        sheetView.Attribute("showGridLines").Should().BeNull();
        sheetView.Attribute("zoomScale").Should().BeNull();
        sheetView.Attribute("topLeftCell").Should().BeNull();
        sheetView.Attribute("customSheetViewAttr").Should().BeNull();
        pane.Attribute("xSplit").Should().BeNull();
        pane.Attribute("topLeftCell").Should().BeNull();
        pane.Attribute("activePane").Should().BeNull();
        pane.Attribute("state").Should().BeNull();
        pane.Attribute("customPaneAttr").Should().BeNull();
        selection.Attribute("pane").Should().BeNull();
        selection.Attribute("activeCell").Should().BeNull();
        selection.Attribute("activeCellId").Should().BeNull();
        selection.Attribute("sqref").Should().BeNull();
        selection.Attribute("customSelectionAttr").Should().BeNull();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetAdditionalViews_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetAdditionalViewsSourceWorkbook());
        var sourceWorkbookViews = ReadWorkbookChildElement(source, "bookViews");
        var sourceSheetViews = ReadWorksheetChildElement(source, "sheetViews");
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
        ReadWorkbookChildElement(saved, "bookViews")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookViews.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "sheetViews")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSheetViews.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetSheetViewsForSchemaValidity()
    {
        using var source = Save(CreateWorksheetAdditionalViewsSourceWorkbook());
        SetWorksheetSheetViewsInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.AdditionalViews!.Views.Add(new WorksheetAdditionalViewModel { WorkbookViewId = "2" });

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetSheetViewsSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetSheetViewsForSchemaValidity()
    {
        using var source = Save(CreateWorksheetAdditionalViewsSourceWorkbook());
        SetWorksheetSheetViewsInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetSheetViewsSanitized(saved);
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
    public void LoadedWorkbookFullSave_SanitizesInvalidMergedCellsForSchemaValidity()
    {
        using var source = Save(CreateMergedCellSourceWorkbook());
        SetMergedCellInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("full-save edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertMergedCellsSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidMergedCellsForSchemaValidity()
    {
        using var source = Save(CreateMergedCellSourceWorkbook());
        SetMergedCellInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("merged edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertMergedCellsSanitized(saved);
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
    public void LoadedWorkbookPatchSave_SanitizesRichCommentFontFamiliesForSchemaValidity()
    {
        using var source = CreateLegacyCommentSourcePackage();
        AddCssFontFamilyRichComment(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        ReadLegacyCommentRunFont(saved, "C2").Should().Be("Google Sans");
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
    public void LoadedWorkbookPatchSave_SanitizesInvalidSheetViewAttributesForSchemaValidity()
    {
        using var source = Save(CreateFreezePaneSourceWorkbook());
        SetWorksheetSheetViewInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var sheetViews = ReadWorksheetChildElement(saved, "sheetViews");
        var sheetView = sheetViews.Elements(sheetViews.Name.Namespace + "sheetView")
            .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value, "0", StringComparison.Ordinal));
        var pane = sheetView.Element(sheetView.Name.Namespace + "pane")!;
        var selection = sheetView.Element(sheetView.Name.Namespace + "selection")!;
        sheetView.Attribute("view").Should().BeNull();
        sheetView.Attribute("showGridLines").Should().BeNull();
        sheetView.Attribute("zoomScale").Should().BeNull();
        sheetView.Attribute("topLeftCell").Should().BeNull();
        sheetView.Attribute("customSheetViewAttr").Should().BeNull();
        pane.Attribute("xSplit").Should().BeNull();
        pane.Attribute("topLeftCell").Should().BeNull();
        pane.Attribute("activePane").Should().BeNull();
        pane.Attribute("state").Should().BeNull();
        pane.Attribute("customPaneAttr").Should().BeNull();
        selection.Attribute("pane").Should().BeNull();
        selection.Attribute("activeCell").Should().BeNull();
        selection.Attribute("activeCellId").Should().BeNull();
        selection.Attribute("sqref").Should().BeNull();
        selection.Attribute("customSelectionAttr").Should().BeNull();
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
    public void PhoneticProperties_SanitizesInvalidAttributesForSchemaValidity()
    {
        var workbook = new Workbook("PhoneticPropertiesSanitize");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.PhoneticProperties = new WorksheetPhoneticProperties("not-a-number", "invalidType", "center");

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var phoneticPr = ReadWorksheetChildElement(saved, "phoneticPr");
        phoneticPr.Attribute("fontId")!.Value.Should().Be("0");
        phoneticPr.Attribute("type").Should().BeNull();
        phoneticPr.Attribute("alignment")!.Value.Should().Be("center");
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
    public void LoadedWorkbookFullSave_SanitizesInvalidNativePhoneticPropertiesForSchemaValidity()
    {
        using var source = Save(CreatePhoneticPropertiesSourceWorkbook());
        SetWorksheetPhoneticPropertiesInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetPhoneticPropertiesSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidPhoneticPropertiesForSchemaValidity()
    {
        using var source = Save(CreatePhoneticPropertiesSourceWorkbook());
        SetWorksheetPhoneticProperties(source, "not-a-number", "invalidType", "center");
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var phoneticPr = ReadWorksheetChildElement(saved, "phoneticPr");
        phoneticPr.Attribute("fontId")!.Value.Should().Be("0");
        phoneticPr.Attribute("type").Should().BeNull();
        phoneticPr.Attribute("alignment")!.Value.Should().Be("center");
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidNativePhoneticPropertiesForSchemaValidity()
    {
        using var source = Save(CreatePhoneticPropertiesSourceWorkbook());
        SetWorksheetPhoneticPropertiesInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(77));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetPhoneticPropertiesSanitized(saved);
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
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetSheetFormatForSchemaValidity()
    {
        using var source = Save(CreateWorksheetOutlineAndFormatSourceWorkbook());
        SetWorksheetSheetFormatInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetSheetFormatSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetSheetFormatForSchemaValidity()
    {
        using var source = Save(CreateWorksheetOutlineAndFormatSourceWorkbook());
        SetWorksheetSheetFormatInvalidNativeMetadata(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetSheetFormatSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetGridXmlForSchemaValidity()
    {
        using var source = Save(CreateWorksheetGridXmlSourceWorkbook());
        SetWorksheetGridXmlInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("full-save edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetGridXmlSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetGridXmlForSchemaValidity()
    {
        using var source = Save(CreateWorksheetGridXmlSourceWorkbook());
        SetWorksheetGridXmlInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetGridXmlSanitized(saved);
    }

    [Fact]
    public void WorksheetDimension_SanitizesInvalidNativeMetadataForSchemaValidity()
    {
        using var saved = Save(CreateInvalidWorksheetDimensionSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetDimensionSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetDimensionForSchemaValidity()
    {
        using var source = Save(CreateWorksheetDimensionSourceWorkbook());
        SetWorksheetDimensionInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("full-save edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetDimensionSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetDimensionForSchemaValidity()
    {
        using var source = Save(CreateWorksheetDimensionSourceWorkbook());
        SetWorksheetDimensionInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(77));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetDimensionSanitized(saved);
    }


    [Fact]
    public void PageLayout_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreatePageLayoutSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void PageLayout_SanitizesInvalidNativeMetadataForSchemaValidity()
    {
        using var saved = Save(CreateInvalidPageLayoutSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        AssertPageLayoutSanitized(saved);
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
    public void LoadedWorkbookPatchSave_SanitizesInvalidPageLayoutForSchemaValidity()
    {
        using var source = Save(CreatePageLayoutSourceWorkbook());
        SetPageLayoutInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertPageLayoutSanitized(saved);
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
    public void ManualPageBreaks_SanitizesInvalidAttributesForSchemaValidity()
    {
        using var saved = Save(CreateInvalidManualPageBreakSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        AssertManualPageBreaksSanitized(saved);
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
    public void LoadedWorkbookPatchSave_SanitizesInvalidManualPageBreaksForSchemaValidity()
    {
        using var source = Save(CreateManualPageBreakSourceWorkbook());
        SetManualPageBreakInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertManualPageBreaksSanitized(saved);
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

    private const string StructuredTableRootExtensionUri = "{FREEX-TABLE-EXT}";
    private const string StructuredTableColumnsExtensionUri = "{FREEX-TABLE-COLUMNS-EXT}";
    private const string StructuredTableColumnExtensionUri = "{FREEX-TABLE-COLUMN-EXT}";
    private const string StructuredTableAutoFilterExtensionUri = "{FREEX-TABLE-AUTOFILTER-EXTLIST}";
    private const string StructuredTableFilterColumnExtensionUri = "{FREEX-TABLE-FILTER-COLUMN-EXT}";
    private const string StructuredTableSortStateExtensionUri = "{FREEX-TABLE-SORTSTATE-EXT}";
    private const string WorksheetAutoFilterExtensionUri = "{FREEX-WORKSHEET-AUTOFILTER-EXT}";
    private const string WorksheetFilterColumnExtensionUri = "{FREEX-WORKSHEET-FILTER-COLUMN-EXT}";
    private const string WorksheetSortStateExtensionUri = "{FREEX-WORKSHEET-SORTSTATE-EXT}";
    private const string WorksheetSortConditionExtensionUri = "{FREEX-WORKSHEET-SORT-CONDITION-EXT}";

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
            NativeAutoFilterAttributes = new Dictionary<string, string> { ["customAttr"] = "removed" },
            NativeAutoFilterChildXmls =
            [
                "<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEX-TABLE-AUTOFILTER-EXT}\" /></extLst>",
                "<nativeAutoFilterChild xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"
            ]
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Name"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Value"));
        sheet.StructuredTables.Add(table);

        return workbook;
    }

    private static Workbook CreateInvalidStructuredTableExtensionListSourceWorkbook()
    {
        var workbook = new Workbook("StructuredTableExtensionListInvalidSchema");
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
            NativeSortStateXml = CreateInvalidStructuredTableSortStateXml(),
            NativeChildXmls =
            [
                CreateInvalidExtensionListXml(StructuredTableRootExtensionUri, "FreeXTableExtension", "customTableExtLstFlag", "customTableExtFlag", "nativeTableExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-TABLE-EXTLST}")
            ],
            NativeAutoFilterChildXmls =
            [
                CreateInvalidExtensionListXml(StructuredTableAutoFilterExtensionUri, "FreeXTableAutoFilterExtension", "customAutoFilterExtLstFlag", "customAutoFilterExtFlag", "nativeAutoFilterExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-TABLE-AUTOFILTER-EXTLST}")
            ]
        };
        table.Columns.Add(new StructuredTableColumnModel(
            1,
            "Name",
            NativeChildXmls:
            [
                CreateInvalidExtensionListXml(StructuredTableColumnExtensionUri, "FreeXTableColumnExtension", "customColumnExtLstFlag", "customColumnExtFlag", "nativeColumnExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-TABLE-COLUMN-EXTLST}")
            ]));
        table.Columns.Add(new StructuredTableColumnModel(2, "Value"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(
            0,
            ["A"],
            IncludeBlank: false,
            NativeFilterXmls:
            [
                CreateInvalidExtensionListXml(StructuredTableFilterColumnExtensionUri, "FreeXTableFilterColumnExtension", "customFilterColumnExtLstFlag", "customFilterColumnExtFlag", "nativeFilterColumnExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-TABLE-FILTER-COLUMN-EXTLST}")
            ]));
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static Workbook CreateInvalidStructuredTableAutoFilterSourceWorkbook()
    {
        var workbook = new Workbook("StructuredTableAutoFilterInvalidSchema");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Rank"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 1, 1, 3, 3),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
            NativeAutoFilterAttributes = new Dictionary<string, string> { ["customAttr"] = "removed" },
            NativeAutoFilterChildXmls =
            [
                "<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEX-TABLE-AUTOFILTER-EXT}\" /></extLst>",
                "<nativeAutoFilterChild xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"
            ]
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Name"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Value"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Rank"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(
            0,
            ["A"],
            IncludeBlank: false,
            NativeFilterXmls: [],
            NativeAttributes: new Dictionary<string, string>
            {
                ["hiddenButton"] = "maybe",
                ["showButton"] = "maybe",
                ["customFilterColumnFlag"] = "removed"
            }));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(
            1,
            [],
            IncludeBlank: false,
            CustomFilters: [new StructuredTableCustomFilterModel("invalid", "1", new Dictionary<string, string> { ["customFilterFlag"] = "removed" })],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: "maybe",
            NativeCustomFiltersAttributes: new Dictionary<string, string> { ["customFiltersFlag"] = "removed" },
            NativeFilterXmls: []));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(
            2,
            [],
            IncludeBlank: false,
            NativeFilterXmls:
            [
                """
                <top10 xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" top="maybe" percent="maybe" val="not-a-number" filterVal="not-a-number" customTop10Flag="removed" />
                """
            ]));
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static Workbook CreateInvalidStructuredTableSortStateSourceWorkbook()
    {
        var workbook = new Workbook("StructuredTableSortStateInvalidSchema");
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
            NativeSortStateXml = """
                <sortState xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" ref="A1:B3" columnSort="maybe" caseSensitive="maybe" sortMethod="invalid">
                  <sortCondition ref="A2:A3" descending="maybe" sortBy="invalid" dxfId="not-a-number" iconId="not-a-number" />
                </sortState>
                """,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Name"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Value"));
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static Workbook CreateInvalidStructuredTableMetadataSourceWorkbook()
    {
        var workbook = new Workbook("StructuredTableMetadataInvalidSchema");
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
            NativeAttributes = new Dictionary<string, string>
            {
                ["tableType"] = "invalid",
                ["headerRowDxfId"] = "not-a-number",
                ["connectionId"] = "not-a-number"
            }
        };
        table.Columns.Add(new StructuredTableColumnModel(
            1,
            "Name",
            TotalsRowFunction: "invalid"));
        table.Columns.Add(new StructuredTableColumnModel(
            2,
            "Value",
            NativeAttributes: new Dictionary<string, string>
            {
                ["queryTableFieldId"] = "not-a-number",
                ["dataDxfId"] = "not-a-number"
            }));
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static void AssertStructuredTableMetadataSanitized(MemoryStream stream)
    {
        var table = ReadPackageRootElement(stream, "xl/tables/table1.xml");
        var workbookNs = table.Name.Namespace;
        table.Attribute("tableType").Should().BeNull();
        table.Attribute("headerRowDxfId").Should().BeNull();
        table.Attribute("connectionId").Should().BeNull();

        var tableColumns = table.Element(workbookNs + "tableColumns")!;
        tableColumns.Attribute("count")!.Value.Should().Be("2");
        var columns = tableColumns.Elements(workbookNs + "tableColumn").ToArray();
        columns.Should().HaveCount(2);
        columns[0].Attribute("id")!.Value.Should().Be("1");
        columns[0].Attribute("totalsRowFunction").Should().BeNull();
        (columns[0].Element(workbookNs + "calculatedColumnFormula")?.Attribute("array")).Should().BeNull();
        columns[1].Attribute("queryTableFieldId").Should().BeNull();
        columns[1].Attribute("dataDxfId").Should().BeNull();

        var styleInfo = table.Element(workbookNs + "tableStyleInfo")!;
        styleInfo.Attribute("showFirstColumn")?.Value.Should().NotBe("maybe");
        styleInfo.Attribute("showRowStripes")?.Value.Should().NotBe("maybe");
    }

    private static void AssertStructuredTableExtensionListsSanitized(MemoryStream stream)
    {
        var table = ReadPackageRootElement(stream, "xl/tables/table1.xml");
        var workbookNs = table.Name.Namespace;

        AssertExtensionListSanitized(
            table,
            workbookNs,
            StructuredTableRootExtensionUri,
            "FreeXTableExtension",
            "customTableExtLstFlag",
            "customTableExtFlag",
            "nativeTableExtLstChild");

        var tableChildren = table.Elements().Select(element => element.Name.LocalName).ToList();
        AssertChildPrecedes(tableChildren, "autoFilter", "sortState");
        AssertChildPrecedes(tableChildren, "sortState", "tableColumns");
        AssertChildPrecedes(tableChildren, "tableColumns", "tableStyleInfo");
        AssertChildPrecedes(tableChildren, "tableStyleInfo", "extLst");

        var autoFilter = table.Element(workbookNs + "autoFilter");
        autoFilter.Should().NotBeNull();
        AssertExtensionListSanitized(
            autoFilter!,
            workbookNs,
            StructuredTableAutoFilterExtensionUri,
            "FreeXTableAutoFilterExtension",
            "customAutoFilterExtLstFlag",
            "customAutoFilterExtFlag",
            "nativeAutoFilterExtLstChild");

        var filterColumn = autoFilter!.Element(workbookNs + "filterColumn");
        filterColumn.Should().NotBeNull();
        filterColumn!.Elements(workbookNs + "extLst").Should().BeEmpty();

        var sortState = table.Element(workbookNs + "sortState");
        sortState.Should().NotBeNull();
        AssertExtensionListSanitized(
            sortState!,
            workbookNs,
            StructuredTableSortStateExtensionUri,
            "FreeXTableSortStateExtension",
            "customSortStateExtLstFlag",
            "customSortStateExtFlag",
            "nativeSortStateExtLstChild");
        sortState!.Elements().Select(element => element.Name.LocalName).Should().ContainInOrder("sortCondition", "extLst");

        var tableColumns = table.Element(workbookNs + "tableColumns");
        tableColumns.Should().NotBeNull();
        tableColumns!.Elements(workbookNs + "extLst").Should().BeEmpty();

        var firstColumn = tableColumns!.Elements(workbookNs + "tableColumn").First();
        AssertExtensionListSanitized(
            firstColumn,
            workbookNs,
            StructuredTableColumnExtensionUri,
            "FreeXTableColumnExtension",
            "customColumnExtLstFlag",
            "customColumnExtFlag",
            "nativeColumnExtLstChild");
    }

    private static void AssertExtensionListSanitized(
        XElement parent,
        XNamespace workbookNs,
        string expectedUri,
        string expectedPayloadName,
        string listAttributeName,
        string extensionAttributeName,
        string unexpectedChildName)
    {
        var extensionList = parent.Elements(workbookNs + "extLst").Should().ContainSingle().Subject;
        extensionList.Attribute(listAttributeName).Should().BeNull();
        extensionList.Element(workbookNs + unexpectedChildName).Should().BeNull();

        var extension = extensionList.Elements(workbookNs + "ext").Should().ContainSingle().Subject;
        extension.Attribute("uri")!.Value.Should().Be(expectedUri);
        extension.Attribute(extensionAttributeName).Should().BeNull();
        extension.ToString(SaveOptions.DisableFormatting).Should().Contain(expectedPayloadName);
    }

    private static string CreateInvalidExtensionListXml(
        string uri,
        string payloadName,
        string listAttributeName,
        string extensionAttributeName,
        string unexpectedChildName)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return CreateInvalidExtensionList(
                workbookNs,
                uri,
                payloadName,
                listAttributeName,
                extensionAttributeName,
                unexpectedChildName)
            .ToString(SaveOptions.DisableFormatting);
    }

    private static string CreateDuplicateExtensionListXml(string uri)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(
                workbookNs + "extLst",
                new XElement(workbookNs + "ext", new XAttribute("uri", uri)))
            .ToString(SaveOptions.DisableFormatting);
    }

    private static string CreateInvalidStructuredTableSortStateXml()
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return CreateInvalidStructuredTableSortStateElement(workbookNs).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement CreateInvalidStructuredTableSortStateElement(XNamespace workbookNs) =>
        new(
            workbookNs + "sortState",
            new XAttribute("ref", "A1:B3"),
            CreateInvalidExtensionList(
                workbookNs,
                StructuredTableSortStateExtensionUri,
                "FreeXTableSortStateExtension",
                "customSortStateExtLstFlag",
                "customSortStateExtFlag",
                "nativeSortStateExtLstChild"),
            new XElement(
                workbookNs + "sortCondition",
                new XAttribute("ref", "A2:A3")),
            new XElement(
                workbookNs + "extLst",
                new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-TABLE-SORTSTATE-EXTLST}"))));

    private static XElement CreateInvalidExtensionList(
        XNamespace workbookNs,
        string uri,
        string payloadName,
        string listAttributeName,
        string extensionAttributeName,
        string unexpectedChildName)
    {
        XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";
        return new XElement(
            workbookNs + "extLst",
            new XAttribute(listAttributeName, "removed"),
            new XElement(
                workbookNs + "ext",
                new XAttribute("uri", $" {uri} "),
                new XAttribute(extensionAttributeName, "removed"),
                new XElement(
                    x15Ns + "futureMetadata",
                    new XAttribute(XNamespace.Xmlns + "x15", x15Ns),
                    new XAttribute("name", payloadName))),
            new XElement(workbookNs + unexpectedChildName),
            new XElement(workbookNs + "ext", new XAttribute("uri", " ")),
            new XElement(workbookNs + "ext", new XAttribute("uri", uri)));
    }

    private static void AssertChildPrecedes(List<string> childNames, string firstName, string secondName)
    {
        var firstIndex = childNames.IndexOf(firstName);
        var secondIndex = childNames.IndexOf(secondName);
        if (firstIndex >= 0 && secondIndex >= 0)
            firstIndex.Should().BeLessThan(secondIndex);
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

    private static Workbook CreateWorksheetFilterSortExtensionListBaseWorkbook()
    {
        var workbook = new Workbook("WorksheetFilterSortExtensionList");
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
        sheet.SortState = new WorksheetSortStateModel
        {
            Reference = "A1:B5",
            Conditions =
            [
                new WorksheetSortConditionModel
                {
                    Reference = "A2:A5"
                }
            ]
        };
        return workbook;
    }

    private static Workbook CreateInvalidWorksheetFilterSortExtensionListSourceWorkbook()
    {
        var workbook = CreateWorksheetFilterSortExtensionListBaseWorkbook();
        var sheet = workbook.GetSheetAt(0);
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B5", null)
        {
            NativeChildXmls =
            [
                CreateInvalidExtensionListXml(WorksheetAutoFilterExtensionUri, "FreeXWorksheetAutoFilterExtension", "customAutoFilterExtLstFlag", "customAutoFilterExtFlag", "nativeAutoFilterExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-WORKSHEET-AUTOFILTER-EXTLST}")
            ]
        };
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            ["North"],
            IncludeBlank: true,
            NativeFilterXmls:
            [
                CreateInvalidExtensionListXml(WorksheetFilterColumnExtensionUri, "FreeXWorksheetFilterColumnExtension", "customFilterColumnExtLstFlag", "customFilterColumnExtFlag", "nativeFilterColumnExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-WORKSHEET-FILTER-COLUMN-EXTLST}")
            ]));
        sheet.SortState = new WorksheetSortStateModel
        {
            NativeXml = CreateInvalidWorksheetSortStateXml()
        };
        return workbook;
    }

    private static void AssertWorksheetFilterSortExtensionListsSanitized(MemoryStream stream)
    {
        var autoFilter = ReadWorksheetChildElement(stream, "autoFilter");
        var worksheetNs = autoFilter.Name.Namespace;
        AssertExtensionListSanitized(
            autoFilter,
            worksheetNs,
            WorksheetAutoFilterExtensionUri,
            "FreeXWorksheetAutoFilterExtension",
            "customAutoFilterExtLstFlag",
            "customAutoFilterExtFlag",
            "nativeAutoFilterExtLstChild");

        var filterColumn = autoFilter.Elements(worksheetNs + "filterColumn").First();
        filterColumn.Elements(worksheetNs + "extLst").Should().BeEmpty();

        var sortState = ReadWorksheetChildElement(stream, "sortState");
        AssertExtensionListSanitized(
            sortState,
            worksheetNs,
            WorksheetSortStateExtensionUri,
            "FreeXWorksheetSortStateExtension",
            "customSortStateExtLstFlag",
            "customSortStateExtFlag",
            "nativeSortStateExtLstChild");
        sortState.Elements().Select(element => element.Name.LocalName).Should().ContainInOrder("sortCondition", "extLst");

        var sortCondition = sortState.Element(worksheetNs + "sortCondition");
        sortCondition.Should().NotBeNull();
        sortCondition!.Elements().Should().BeEmpty();
    }

    private static string CreateInvalidWorksheetSortStateXml()
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return CreateInvalidWorksheetSortStateElement(workbookNs).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement CreateInvalidWorksheetSortStateElement(XNamespace workbookNs) =>
        new(
            workbookNs + "sortState",
            new XAttribute("ref", "A1:B5"),
            CreateInvalidExtensionList(
                workbookNs,
                WorksheetSortStateExtensionUri,
                "FreeXWorksheetSortStateExtension",
                "customSortStateExtLstFlag",
                "customSortStateExtFlag",
                "nativeSortStateExtLstChild"),
            new XElement(
                workbookNs + "sortCondition",
                new XAttribute("ref", "A2:A5"),
                CreateInvalidExtensionList(
                    workbookNs,
                    WorksheetSortConditionExtensionUri,
                    "FreeXWorksheetSortConditionExtension",
                    "customSortConditionExtLstFlag",
                    "customSortConditionExtFlag",
                    "nativeSortConditionExtLstChild"),
                new XElement(
                    workbookNs + "extLst",
                    new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-WORKSHEET-SORT-CONDITION-EXTLST}")))),
            new XElement(
                workbookNs + "extLst",
                new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-WORKSHEET-SORTSTATE-EXTLST}"))));

    private static Workbook CreateInvalidAutoFilterSourceWorkbook()
    {
        var workbook = new Workbook("AutoFilterInvalidSchema");
        var sheet = workbook.AddSheet("Data");
        string[] headers = ["Region", "Custom", "Top", "Dynamic", "Color", "Icon"];
        for (uint col = 1; col <= 6; col++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new TextValue(headers[(int)col - 1]));
            sheet.SetCell(new CellAddress(sheet.Id, 2, col), new NumberValue(col * 10));
        }

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:F2", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            ["North"],
            IncludeBlank: false,
            CustomFilters: [],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: null,
            DynamicFilter: null,
            ColorFilter: null,
            IconFilter: null,
            DateGroups:
            [
                new WorksheetAutoFilterDateGroupItemModel(
                    YearRaw: "not-a-number",
                    DateTimeGrouping: "invalid")
            ],
            NativeFiltersAttributes: new Dictionary<string, string>
            {
                ["blank"] = "maybe",
                ["calendarType"] = "invalid",
                ["filtersFlag"] = "removed"
            },
            NativeFilterXmls: [],
            NativeAttributes: new Dictionary<string, string>
            {
                ["hiddenButton"] = "maybe",
                ["showButton"] = "maybe",
                ["customFilterColumnFlag"] = "removed"
            }));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            1,
            [],
            IncludeBlank: false,
            CustomFilters: [new WorksheetAutoFilterCustomFilterModel("invalid", "A", new Dictionary<string, string> { ["customFilterFlag"] = "removed" })],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: "maybe",
            NativeCustomFiltersAttributes: new Dictionary<string, string> { ["customFiltersFlag"] = "removed" },
            Top10: null,
            DynamicFilter: null,
            ColorFilter: null,
            IconFilter: null,
            NativeFilterXmls: []));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            2,
            [],
            IncludeBlank: false,
            CustomFilters: [],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: new WorksheetAutoFilterTop10Model(
                TopRaw: "maybe",
                PercentRaw: "maybe",
                ValueRaw: "not-a-number",
                FilterValueRaw: "not-a-number",
                NativeAttributes: new Dictionary<string, string> { ["customTop10Flag"] = "removed" }),
            DynamicFilter: null,
            NativeFilterXmls: []));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            3,
            [],
            IncludeBlank: false,
            CustomFilters: [],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: null,
            DynamicFilter: new WorksheetAutoFilterDynamicFilterModel(
                Type: "invalid",
                ValueRaw: "not-a-number",
                MaxValueRaw: "not-a-number",
                NativeAttributes: new Dictionary<string, string> { ["customDynamicFilterFlag"] = "removed" }),
            NativeFilterXmls: []));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            4,
            [],
            IncludeBlank: false,
            CustomFilters: [],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: null,
            DynamicFilter: null,
            ColorFilter: new WorksheetAutoFilterColorFilterModel(
                DifferentialFormatIdRaw: "not-a-number",
                CellColorRaw: "maybe"),
            NativeFilterXmls: []));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            5,
            [],
            IncludeBlank: false,
            CustomFilters: [],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: null,
            DynamicFilter: null,
            ColorFilter: null,
            IconFilter: new WorksheetAutoFilterIconFilterModel(
                IconSet: "invalid",
                IconIdRaw: "not-a-number"),
            DateGroups: [],
            NativeFiltersAttributes: null,
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

    private static void SetStructuredTableAutoFilterInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var tableXml = LoadPackageXml(archive, "xl/tables/table1.xml");
        var autoFilter = tableXml.Root!.Element(workbookNs + "autoFilter")!;
        autoFilter.SetAttributeValue("customAttr", "removed");
        autoFilter.Add(
            new XElement(workbookNs + "extLst", new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-TABLE-AUTOFILTER-EXT}"))),
            new XElement(workbookNs + "nativeAutoFilterChild"),
            new XElement(
                workbookNs + "filterColumn",
                new XAttribute("colId", "0"),
                new XAttribute("hiddenButton", "maybe"),
                new XAttribute("showButton", "maybe"),
                new XAttribute("customFilterColumnFlag", "removed"),
                new XElement(
                    workbookNs + "filters",
                    new XAttribute("blank", "maybe"),
                    new XAttribute("filtersFlag", "removed"),
                    new XElement(
                        workbookNs + "filter",
                        new XAttribute("val", "A"),
                        new XAttribute("filterFlag", "removed"),
                        new XElement(workbookNs + "nativeFilterChild")))),
            new XElement(
                workbookNs + "filterColumn",
                new XAttribute("colId", "1"),
                new XElement(
                    workbookNs + "customFilters",
                    new XAttribute("and", "maybe"),
                    new XAttribute("customFiltersFlag", "removed"),
                    new XElement(
                        workbookNs + "customFilter",
                        new XAttribute("operator", "invalid"),
                        new XAttribute("val", "1"),
                        new XAttribute("customFilterFlag", "removed"),
                        new XElement(workbookNs + "nativeCustomFilterChild")))));
        ReplacePackageXml(archive, "xl/tables/table1.xml", tableXml);
    }

    private static void SetStructuredTableSortStateInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var tableXml = LoadPackageXml(archive, "xl/tables/table1.xml");
        var sortState = new XElement(
            workbookNs + "sortState",
            new XAttribute("ref", "A1:B3"),
            new XAttribute("columnSort", "maybe"),
            new XAttribute("caseSensitive", "maybe"),
            new XAttribute("sortMethod", "invalid"),
            new XElement(
                workbookNs + "sortCondition",
                new XAttribute("ref", "A2:A3"),
                new XAttribute("descending", "maybe"),
                new XAttribute("sortBy", "invalid"),
                new XAttribute("dxfId", "not-a-number"),
                new XAttribute("iconId", "not-a-number")));
        if (tableXml.Root!.Element(workbookNs + "autoFilter") is { } autoFilter)
            autoFilter.AddAfterSelf(sortState);
        else
            tableXml.Root.AddFirst(sortState);
        ReplacePackageXml(archive, "xl/tables/table1.xml", tableXml);
    }

    private static void SetStructuredTableMetadataInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var tableXml = LoadPackageXml(archive, "xl/tables/table1.xml");
        var root = tableXml.Root!;
        root.SetAttributeValue("tableType", "invalid");
        root.SetAttributeValue("headerRowDxfId", "not-a-number");

        var tableColumns = root.Element(workbookNs + "tableColumns")!;
        tableColumns.SetAttributeValue("count", "not-a-number");
        var columns = tableColumns.Elements(workbookNs + "tableColumn").ToArray();
        columns[0].SetAttributeValue("id", "not-a-number");
        columns[0].SetAttributeValue("totalsRowFunction", "invalid");
        columns[0].AddFirst(new XElement(
            workbookNs + "calculatedColumnFormula",
            new XAttribute("array", "maybe"),
            "[Value]*2"));
        columns[1].SetAttributeValue("queryTableFieldId", "not-a-number");
        columns[1].SetAttributeValue("dataDxfId", "not-a-number");

        var styleInfo = root.Element(workbookNs + "tableStyleInfo")!;
        styleInfo.SetAttributeValue("showFirstColumn", "maybe");
        styleInfo.SetAttributeValue("showRowStripes", "maybe");
        ReplacePackageXml(archive, "xl/tables/table1.xml", tableXml);
    }

    private static void SetStructuredTableExtensionListsInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var tableXml = LoadPackageXml(archive, "xl/tables/table1.xml");
        var root = tableXml.Root!;
        root.Elements(workbookNs + "extLst").Remove();
        root.Add(
            CreateInvalidExtensionList(workbookNs, StructuredTableRootExtensionUri, "FreeXTableExtension", "customTableExtLstFlag", "customTableExtFlag", "nativeTableExtLstChild"),
            new XElement(workbookNs + "extLst", new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-TABLE-EXTLST}"))));

        var autoFilter = root.Element(workbookNs + "autoFilter")!;
        autoFilter.Elements(workbookNs + "extLst").Remove();
        autoFilter.Add(
            CreateInvalidExtensionList(workbookNs, StructuredTableAutoFilterExtensionUri, "FreeXTableAutoFilterExtension", "customAutoFilterExtLstFlag", "customAutoFilterExtFlag", "nativeAutoFilterExtLstChild"),
            new XElement(
                workbookNs + "filterColumn",
                new XAttribute("colId", "0"),
                CreateInvalidExtensionList(workbookNs, StructuredTableFilterColumnExtensionUri, "FreeXTableFilterColumnExtension", "customFilterColumnExtLstFlag", "customFilterColumnExtFlag", "nativeFilterColumnExtLstChild"),
                new XElement(workbookNs + "extLst", new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-TABLE-FILTER-COLUMN-EXTLST}")))),
            new XElement(workbookNs + "extLst", new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-TABLE-AUTOFILTER-EXTLST}"))));

        autoFilter.AddAfterSelf(CreateInvalidStructuredTableSortStateElement(workbookNs));

        var tableColumns = root.Element(workbookNs + "tableColumns")!;
        tableColumns.Add(
            CreateInvalidExtensionList(workbookNs, StructuredTableColumnsExtensionUri, "FreeXTableColumnsExtension", "customTableColumnsExtLstFlag", "customTableColumnsExtFlag", "nativeTableColumnsExtLstChild"),
            new XElement(workbookNs + "extLst", new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-TABLE-COLUMNS-EXTLST}"))));

        var firstColumn = tableColumns.Elements(workbookNs + "tableColumn").First();
        firstColumn.Add(
            CreateInvalidExtensionList(workbookNs, StructuredTableColumnExtensionUri, "FreeXTableColumnExtension", "customColumnExtLstFlag", "customColumnExtFlag", "nativeColumnExtLstChild"),
            new XElement(workbookNs + "extLst", new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-TABLE-COLUMN-EXTLST}"))));

        ReplacePackageXml(archive, "xl/tables/table1.xml", tableXml);
    }

    private static void SetWorksheetAutoFilterInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var autoFilter = worksheetXml.Root!.Element(workbookNs + "autoFilter")!;
        var firstColumn = autoFilter.Elements(workbookNs + "filterColumn").First();
        firstColumn.SetAttributeValue("hiddenButton", "maybe");
        firstColumn.SetAttributeValue("showButton", "maybe");
        firstColumn.SetAttributeValue("customFilterColumnFlag", "removed");
        var filters = firstColumn.Element(workbookNs + "filters")!;
        filters.SetAttributeValue("blank", "maybe");
        filters.SetAttributeValue("calendarType", "invalid");
        filters.SetAttributeValue("filtersFlag", "removed");
        var filter = filters.Element(workbookNs + "filter")!;
        filter.SetAttributeValue("filterFlag", "removed");
        filter.Add(new XElement(workbookNs + "nativeFilterChild"));
        filters.Add(new XElement(
            workbookNs + "dateGroupItem",
            new XAttribute("year", "not-a-number"),
            new XAttribute("dateTimeGrouping", "invalid")));

        var customFilters = autoFilter
            .Elements(workbookNs + "filterColumn")
            .Skip(1)
            .First()
            .Element(workbookNs + "customFilters")!;
        customFilters.SetAttributeValue("and", "maybe");
        customFilters.SetAttributeValue("customFiltersFlag", "removed");
        var customFilter = customFilters.Element(workbookNs + "customFilter")!;
        customFilter.SetAttributeValue("operator", "invalid");
        customFilter.SetAttributeValue("customFilterFlag", "removed");
        customFilter.Add(new XElement(workbookNs + "nativeCustomFilterChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void SetWorksheetSortStateInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var sortState = worksheetXml.Root!.Element(workbookNs + "sortState")!;
        sortState.SetAttributeValue("sortMethod", "invalid");
        var condition = sortState.Element(workbookNs + "sortCondition")!;
        condition.SetAttributeValue("sortBy", "invalid");
        condition.SetAttributeValue("dxfId", "not-a-number");
        condition.SetAttributeValue("iconId", "not-a-number");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void SetWorksheetFilterSortExtensionListsInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var root = worksheetXml.Root!;

        var autoFilter = root.Element(workbookNs + "autoFilter")!;
        autoFilter.Elements(workbookNs + "extLst").Remove();
        autoFilter.Add(
            CreateInvalidExtensionList(workbookNs, WorksheetAutoFilterExtensionUri, "FreeXWorksheetAutoFilterExtension", "customAutoFilterExtLstFlag", "customAutoFilterExtFlag", "nativeAutoFilterExtLstChild"),
            new XElement(workbookNs + "extLst", new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-WORKSHEET-AUTOFILTER-EXTLST}"))));

        var filterColumn = autoFilter.Element(workbookNs + "filterColumn")!;
        filterColumn.Elements(workbookNs + "extLst").Remove();
        filterColumn.Add(
            CreateInvalidExtensionList(workbookNs, WorksheetFilterColumnExtensionUri, "FreeXWorksheetFilterColumnExtension", "customFilterColumnExtLstFlag", "customFilterColumnExtFlag", "nativeFilterColumnExtLstChild"),
            new XElement(workbookNs + "extLst", new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-WORKSHEET-FILTER-COLUMN-EXTLST}"))));

        root.Elements(workbookNs + "sortState").Remove();
        autoFilter.AddAfterSelf(CreateInvalidWorksheetSortStateElement(workbookNs));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void SetWorksheetDataConsolidationInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var dataConsolidate = worksheetXml.Root!.Element(workbookNs + "dataConsolidate")!;
        dataConsolidate.SetAttributeValue("function", "invalid");
        dataConsolidate.SetAttributeValue("leftLabels", "maybe");
        dataConsolidate.SetAttributeValue("startLabels", "maybe");
        dataConsolidate.SetAttributeValue("topLabels", "maybe");
        dataConsolidate.SetAttributeValue("link", "maybe");
        dataConsolidate.SetAttributeValue("customDataConsolidationFlag", "removed");
        dataConsolidate.Add(new XElement(workbookNs + "nativeDataConsolidateChild"));
        var dataRefs = dataConsolidate.Element(workbookNs + "dataRefs")!;
        dataRefs.SetAttributeValue("count", "not-a-number");
        dataRefs.SetAttributeValue("customDataRefsFlag", "removed");
        dataRefs.Add(new XElement(workbookNs + "nativeDataRefsChild"));
        var dataRef = dataRefs.Element(workbookNs + "dataRef")!;
        dataRef.SetAttributeValue("customDataRefFlag", "removed");
        dataRef.Add(new XElement(workbookNs + "nativeDataRefChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
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

    private static void SetWorksheetCustomPropertiesInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var customProperties = worksheetXml.Root!.Element(worksheetNs + "customProperties")!;
        customProperties.SetAttributeValue("nativeContainer", "kept");
        customProperties.Add(
            new XElement(worksheetNs + "nativeCustomPropertiesChild"),
            new XElement(
                worksheetNs + "customPr",
                new XAttribute("name", ""),
                new XAttribute("id", "9"),
                new XAttribute("unsupportedAttr", "removed")));

        var customProperty = customProperties.Element(worksheetNs + "customPr")!;
        customProperty.SetAttributeValue("unsupportedAttr", "kept");
        customProperty.Add(new XElement(worksheetNs + "nativeCustomPrChild"));
        worksheetXml.Root!.Add(new XElement(
            worksheetNs + "customProperties",
            new XAttribute("nativeDuplicateContainer", "removed")));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetCustomPropertiesSanitized(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var customProperties = worksheetXml.Root!
            .Elements(worksheetNs + "customProperties")
            .Should()
            .ContainSingle()
            .Subject;
        customProperties.Attribute("nativeContainer").Should().BeNull();
        customProperties.Element(worksheetNs + "nativeCustomPropertiesChild").Should().BeNull();

        var customProperty = customProperties.Elements(worksheetNs + "customPr")
            .Should()
            .ContainSingle()
            .Subject;
        customProperty.Attribute("name")!.Value.Should().Be("FreeXModeledProperty");
        customProperty.Attribute("id").Should().BeNull();
        customProperty.Attribute(relNs + "id")!.Value.Should().NotBeNullOrWhiteSpace();
        customProperty.Attribute("unsupportedAttr").Should().BeNull();
        customProperty.Elements().Should().BeEmpty();
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

    private static void SetWorksheetCellWatchesInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var cellWatches = worksheetXml.Root!.Element(worksheetNs + "cellWatches")!;
        cellWatches.SetAttributeValue("nativeContainer", "kept");
        cellWatches.Element(worksheetNs + "cellWatch")!.SetAttributeValue("nativeWatch", "kept");
        cellWatches.Element(worksheetNs + "cellWatch")!.Add(new XElement(worksheetNs + "nativeCellWatchChild"));
        cellWatches.Add(
            new XElement(
                worksheetNs + "cellWatch",
                new XAttribute("r", "NotARef"),
                new XAttribute("nativeWatch", "removed")),
            new XElement(worksheetNs + "nativeCellWatchesChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetCellWatchesSanitized(MemoryStream stream)
    {
        var cellWatches = ReadWorksheetChildElement(stream, "cellWatches");
        var worksheetNs = cellWatches.Name.Namespace;
        cellWatches.Attribute("nativeContainer").Should().BeNull();
        cellWatches.Element(worksheetNs + "nativeCellWatchesChild").Should().BeNull();

        var cellWatch = cellWatches.Elements(worksheetNs + "cellWatch")
            .Should()
            .ContainSingle()
            .Subject;
        cellWatch.Attribute("r")!.Value.Should().Be("B2");
        cellWatch.Attribute("nativeWatch").Should().BeNull();
        cellWatch.Element(worksheetNs + "nativeCellWatchChild").Should().BeNull();
    }

    private static void SetWorksheetIgnoredErrorsInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var ignoredErrors = worksheetXml.Root!.Element(worksheetNs + "ignoredErrors")!;
        ignoredErrors.SetAttributeValue("nativeContainer", "kept");
        ignoredErrors.Add(
            new XElement(worksheetNs + "nativeIgnoredErrorsChild"),
            new XElement(
                worksheetNs + "ignoredError",
                new XAttribute("sqref", "NotARef"),
                new XAttribute("numberStoredAsText", "1"),
                new XAttribute("nativeIgnoredError", "removed")));

        var ignoredError = ignoredErrors.Element(worksheetNs + "ignoredError")!;
        ignoredError.SetAttributeValue("twoDigitTextYear", "true");
        ignoredError.SetAttributeValue("formulaRange", "maybe");
        ignoredError.SetAttributeValue("nativeIgnoredError", "kept");
        ignoredError.Add(new XElement(worksheetNs + "nativeIgnoredErrorChild"));
        worksheetXml.Root!.Add(new XElement(
            worksheetNs + "ignoredErrors",
            new XAttribute("nativeDuplicateContainer", "removed")));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetIgnoredErrorsSanitized(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var ignoredErrors = worksheetXml.Root!
            .Elements(worksheetNs + "ignoredErrors")
            .Should()
            .ContainSingle()
            .Subject;
        ignoredErrors.Attribute("nativeContainer").Should().BeNull();
        ignoredErrors.Element(worksheetNs + "nativeIgnoredErrorsChild").Should().BeNull();

        var entries = ignoredErrors.Elements(worksheetNs + "ignoredError").ToList();
        entries.Should().NotBeEmpty();
        entries.Select(entry => entry.Attribute("sqref")?.Value).Should().NotContain("NotARef");
        foreach (var entry in entries)
        {
            entry.Attribute("nativeIgnoredError").Should().BeNull();
            entry.Attribute("formulaRange").Should().BeNull();
            entry.Elements().Should().BeEmpty();
        }

        var a1 = entries.Single(entry => entry.Attribute("sqref")?.Value == "A1");
        a1.Attribute("numberStoredAsText")!.Value.Should().Be("1");
        a1.Attribute("twoDigitTextYear")!.Value.Should().Be("1");
    }

    private static Workbook CreateWorksheetScenariosSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetScenariosPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("manual"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Result"));
        workbook.Scenarios.Add(new WorkbookScenario(
            "BestCase",
            [
                new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(42)),
                new ScenarioCellValue(new CellAddress(sheet.Id, 1, 2), new TextValue("Seattle"))
            ],
            "Scenario comment",
            Hidden: true,
            Locked: true,
            User: "FreeXTest"));
        return workbook;
    }

    private static Workbook CreateProtectedRangesSourceWorkbook()
    {
        var workbook = new Workbook("ProtectedRangesPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Locked"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(12));
        sheet.AllowEditRanges.Add(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3)));
        return workbook;
    }

    private static Workbook CreateWorksheetCalculationPropertiesSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetCalculationPropertiesPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("calc"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        sheet.FullCalculationOnLoad = true;
        return workbook;
    }

    private static void SetWorksheetCalculationPropertiesInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var sheetCalcPr = worksheetXml.Root!.Element(worksheetNs + "sheetCalcPr")!;
        sheetCalcPr.SetAttributeValue("calcId", "999");
        sheetCalcPr.Add(new XElement(worksheetNs + "nativeSheetCalcPrChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetCalculationPropertiesSanitized(MemoryStream stream)
    {
        var sheetCalcPr = ReadWorksheetChildElement(stream, "sheetCalcPr");
        sheetCalcPr.Attribute("fullCalcOnLoad")!.Value.Should().Be("1");
        sheetCalcPr.Attribute("calcId").Should().BeNull();
        sheetCalcPr.Elements().Should().BeEmpty();
    }

    private static Workbook CreateCustomSheetViewsSourceWorkbook()
    {
        var workbook = new Workbook("CustomSheetViewsPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("view state"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Review",
            [
                new WorksheetCustomViewState(
                    "Data",
                    WorksheetViewMode.PageLayout,
                    FrozenRows: 1,
                    FrozenCols: 1,
                    SplitRow: null,
                    SplitColumn: null,
                    ShowGridlines: false,
                    ShowHeadings: false,
                    ShowRulers: false,
                    ZoomPercent: 125,
                    ShowFormulas: true)
            ],
            Id: "{33333333-3333-3333-3333-333333333333}"));
        return workbook;
    }

    private static void SetCustomWorkbookViewsInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var customWorkbookViews = workbookXml.Root!.Element(workbookNs + "customWorkbookViews")!;
        customWorkbookViews.SetAttributeValue("customCustomWorkbookViewsFlag", "removed");
        customWorkbookViews.Add(new XElement(workbookNs + "nativeCustomWorkbookViewsChild"));
        var customWorkbookView = customWorkbookViews.Element(workbookNs + "customWorkbookView")!;
        customWorkbookView.SetAttributeValue("autoUpdate", "maybe");
        customWorkbookView.SetAttributeValue("includePrintSettings", "maybe");
        customWorkbookView.SetAttributeValue("mergeInterval", "not-a-number");
        customWorkbookView.SetAttributeValue("activeSheetId", "not-a-number");
        customWorkbookView.SetAttributeValue("xWindow", "not-a-number");
        customWorkbookView.SetAttributeValue("yWindow", "not-a-number");
        customWorkbookView.SetAttributeValue("showObjects", "invalid");
        customWorkbookView.SetAttributeValue("showComments", "invalid");
        customWorkbookView.SetAttributeValue("customCustomWorkbookViewFlag", "removed");
        customWorkbookView.Add(new XElement(workbookNs + "nativeCustomWorkbookViewChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void AssertInvalidCustomWorkbookViewAttributesRemoved(XElement customWorkbookView)
    {
        customWorkbookView.Attribute("name")!.Value.Should().Be("Review");
        customWorkbookView.Attribute("guid")!.Value.Should().Be("{33333333-3333-3333-3333-333333333333}");
        customWorkbookView.Attribute("autoUpdate").Should().BeNull();
        customWorkbookView.Attribute("includePrintSettings").Should().BeNull();
        customWorkbookView.Attribute("mergeInterval").Should().BeNull();
        customWorkbookView.Attribute("activeSheetId")!.Value.Should().Be("1");
        customWorkbookView.Attribute("xWindow").Should().BeNull();
        customWorkbookView.Attribute("yWindow").Should().BeNull();
        customWorkbookView.Attribute("showObjects").Should().BeNull();
        customWorkbookView.Attribute("showComments").Should().BeNull();
        customWorkbookView.Attribute("customCustomWorkbookViewFlag").Should().BeNull();
        customWorkbookView.Element(customWorkbookView.Name.Namespace + "nativeCustomWorkbookViewChild").Should().BeNull();
    }

    private static Workbook CreateWorksheetAdditionalViewsSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetAdditionalViewsPatchSave")
        {
            AdditionalViews = new WorkbookAdditionalViewsModel
            {
                Views =
                {
                    new WorkbookAdditionalViewModel
                    {
                        NativeXml = """
                            <workbookView xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" visibility="hidden" minimized="1" showHorizontalScroll="0" showVerticalScroll="0" showSheetTabs="0" tabRatio="700" firstSheet="0" activeTab="0" />
                            """
                    }
                }
            }
        };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("additional sheet view"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        sheet.AdditionalViews = new WorksheetAdditionalViewsModel
        {
            Views =
            {
                new WorksheetAdditionalViewModel
                {
                    WorkbookViewId = "1"
                }
            }
        };
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

    private static void SetWorksheetSheetViewInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var sheetView = worksheetXml.Root!
            .Element(workbookNs + "sheetViews")!
            .Elements(workbookNs + "sheetView")
            .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));
        sheetView.SetAttributeValue("view", "invalid");
        sheetView.SetAttributeValue("showGridLines", "maybe");
        sheetView.SetAttributeValue("zoomScale", "not-a-number");
        sheetView.SetAttributeValue("topLeftCell", "BAD");
        sheetView.SetAttributeValue("customSheetViewAttr", "removed");
        var pane = sheetView.Element(workbookNs + "pane")!;
        pane.SetAttributeValue("xSplit", "not-a-number");
        pane.SetAttributeValue("topLeftCell", "BAD");
        pane.SetAttributeValue("activePane", "badPane");
        pane.SetAttributeValue("state", "badState");
        pane.SetAttributeValue("customPaneAttr", "removed");
        var selection = sheetView.Element(workbookNs + "selection");
        if (selection is null)
        {
            selection = new XElement(workbookNs + "selection");
            sheetView.Add(selection);
        }

        selection.SetAttributeValue("pane", "badPane");
        selection.SetAttributeValue("activeCell", "BAD");
        selection.SetAttributeValue("activeCellId", "not-a-number");
        selection.SetAttributeValue("sqref", "BAD");
        selection.SetAttributeValue("customSelectionAttr", "removed");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void SetWorksheetSheetViewsInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var sheetViews = worksheetXml.Root!.Element(workbookNs + "sheetViews")!;
        sheetViews.SetAttributeValue("nativeSheetViewsAttr", "kept");
        foreach (var sheetView in sheetViews.Elements(workbookNs + "sheetView"))
            sheetView.SetAttributeValue("customSheetViewAttr", "removed");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetSheetViewsSanitized(MemoryStream stream)
    {
        var sheetViews = ReadWorksheetChildElement(stream, "sheetViews");
        sheetViews.Attribute("nativeSheetViewsAttr").Should().BeNull();
        sheetViews.Elements(sheetViews.Name.Namespace + "sheetView")
            .Should()
            .OnlyContain(sheetView => sheetView.Attribute("customSheetViewAttr") == null);
    }

    private static Workbook CreatePhoneticPropertiesSourceWorkbook()
    {
        var workbook = new Workbook("PhoneticPropertiesPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.PhoneticProperties = new WorksheetPhoneticProperties("1", "fullwidthKatakana", "center");
        return workbook;
    }

    private static void SetWorksheetPhoneticProperties(
        MemoryStream stream,
        string fontId,
        string type,
        string alignment)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var phoneticPr = worksheetXml.Root!.Element(workbookNs + "phoneticPr")!;
        phoneticPr.SetAttributeValue("fontId", fontId);
        phoneticPr.SetAttributeValue("type", type);
        phoneticPr.SetAttributeValue("alignment", alignment);
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void SetWorksheetPhoneticPropertiesInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var phoneticPr = worksheetXml.Root!.Element(workbookNs + "phoneticPr")!;
        phoneticPr.SetAttributeValue("nativeOnly", "kept");
        phoneticPr.Add(new XElement(workbookNs + "nativePhoneticPrChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetPhoneticPropertiesSanitized(MemoryStream stream)
    {
        var phoneticPr = ReadWorksheetChildElement(stream, "phoneticPr");
        phoneticPr.Attribute("fontId")!.Value.Should().Be("1");
        phoneticPr.Attribute("type")!.Value.Should().Be("fullwidthKatakana");
        phoneticPr.Attribute("alignment")!.Value.Should().Be("center");
        phoneticPr.Attribute("nativeOnly").Should().BeNull();
        phoneticPr.Elements().Should().BeEmpty();
    }

    private static void SetWorksheetSheetFormatInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var sheetFormat = worksheetXml.Root!.Element(workbookNs + "sheetFormatPr")!;
        sheetFormat.SetAttributeValue("nativeSheetFormatAttr", "kept");
        sheetFormat.SetAttributeValue("invalidSheetFormatAttr", "removed");
        sheetFormat.Add(new XElement(
            freexNs + "sheetFormatNativeChild",
            new XAttribute("id", "sheet-format")));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetSheetFormatSanitized(MemoryStream stream)
    {
        var sheetFormat = ReadWorksheetChildElement(stream, "sheetFormatPr");
        sheetFormat.Attribute("baseColWidth")!.Value.Should().Be("12");
        sheetFormat.Attribute("zeroHeight")!.Value.Should().Be("0");
        sheetFormat.Attribute("thickTop")!.Value.Should().Be("1");
        sheetFormat.Attribute("thickBottom")!.Value.Should().Be("0");
        sheetFormat.Attribute("outlineLevelRow")!.Value.Should().Be("2");
        sheetFormat.Attribute("outlineLevelCol")!.Value.Should().Be("2");
        sheetFormat.Attribute("nativeSheetFormatAttr").Should().BeNull();
        sheetFormat.Attribute("invalidSheetFormatAttr").Should().BeNull();
        sheetFormat.Elements().Should().BeEmpty();
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

    private static Workbook CreateWorksheetGridXmlSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetGridXmlSchema");
        var sheet = workbook.AddSheet("Data");
        sheet.ColumnWidths[2] = 14.0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        var formulaCell = Cell.FromFormula("A1*2");
        formulaCell.Value = new NumberValue(2);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), formulaCell);
        return workbook;
    }

    private static void SetWorksheetGridXmlInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var root = worksheetXml.Root!;

        var columns = root.Element(worksheetNs + "cols")!;
        columns.SetAttributeValue("nativeColsAttr", "kept");
        var column = columns.Elements(worksheetNs + "col")
            .Single(element => element.Attribute("min")?.Value == "2" && element.Attribute("max")?.Value == "2");
        column.SetAttributeValue("bestFit", "1");
        column.SetAttributeValue("phonetic", "1");
        column.SetAttributeValue("customAttr", "column-native");

        var sheetData = root.Element(worksheetNs + "sheetData")!;
        sheetData.SetAttributeValue("nativeSheetDataAttr", "kept");
        var row = sheetData.Elements(worksheetNs + "row")
            .Single(element => element.Attribute("r")?.Value == "2");
        row.SetAttributeValue("thickTop", "1");
        row.SetAttributeValue("ph", "1");
        row.SetAttributeValue("customAttr", "row-native");
        row.Add(
            new XElement(freexNs + "rowNativeChild", new XAttribute("value", "kept")),
            new XElement(
                worksheetNs + "extLst",
                new XElement(
                    worksheetNs + "ext",
                    new XAttribute("uri", "{FREEX-ROW-GRID-EXT}"),
                    new XElement(freexNs + "rowExt", new XAttribute("value", "row-extension")))));

        var cell = row.Elements(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == "A2");
        cell.SetAttributeValue("cm", "2");
        cell.SetAttributeValue("vm", "1");
        cell.SetAttributeValue("ph", "1");
        cell.SetAttributeValue("customAttr", "cell-native");
        cell.Add(
            new XElement(freexNs + "cellNativeChild", new XAttribute("value", "kept")),
            new XElement(
                worksheetNs + "extLst",
                new XElement(
                    worksheetNs + "ext",
                    new XAttribute("uri", "{FREEX-CELL-GRID-EXT}"),
                    new XElement(freexNs + "cellExt", new XAttribute("value", "cell-extension")))));

        var formula = cell.Element(worksheetNs + "f")!;
        formula.SetAttributeValue("t", "array");
        formula.SetAttributeValue("ref", "A2:A2");
        formula.SetAttributeValue("ca", "1");
        formula.SetAttributeValue("customAttr", "formula-native");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetGridXmlSanitized(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var root = worksheetXml.Root!;

        var columns = root.Element(worksheetNs + "cols")!;
        columns.Attribute("nativeColsAttr").Should().BeNull();
        var column = columns.Elements(worksheetNs + "col")
            .Single(element => element.Attribute("min")?.Value == "2" && element.Attribute("max")?.Value == "2");
        column.Attribute("bestFit")!.Value.Should().Be("1");
        column.Attribute("phonetic")!.Value.Should().Be("1");
        column.Attribute("customAttr").Should().BeNull();

        var sheetData = root.Element(worksheetNs + "sheetData")!;
        sheetData.Attribute("nativeSheetDataAttr").Should().BeNull();
        var row = sheetData.Elements(worksheetNs + "row")
            .Single(element => element.Attribute("r")?.Value == "2");
        row.Attribute("thickTop")!.Value.Should().Be("1");
        row.Attribute("ph")!.Value.Should().Be("1");
        row.Attribute("customAttr").Should().BeNull();
        row.Element(freexNs + "rowNativeChild").Should().BeNull();
        row.Element(worksheetNs + "extLst")!
            .Element(worksheetNs + "ext")!
            .Element(freexNs + "rowExt")!
            .Attribute("value")!.Value.Should().Be("row-extension");

        var cell = row.Elements(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == "A2");
        cell.Attribute("cm").Should().BeNull();
        cell.Attribute("vm").Should().BeNull();
        cell.Attribute("ph")!.Value.Should().Be("1");
        cell.Attribute("customAttr").Should().BeNull();
        cell.Element(freexNs + "cellNativeChild").Should().BeNull();
        cell.Element(worksheetNs + "extLst")!
            .Element(worksheetNs + "ext")!
            .Element(freexNs + "cellExt")!
            .Attribute("value")!.Value.Should().Be("cell-extension");

        var formula = cell.Element(worksheetNs + "f")!;
        formula.Attribute("t")!.Value.Should().Be("array");
        formula.Attribute("ref")!.Value.Should().Be("A2:A2");
        formula.Attribute("ca")!.Value.Should().Be("1");
        formula.Attribute("customAttr").Should().BeNull();
    }

    private static Workbook CreateWorksheetDimensionSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetDimensionSchema");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        return workbook;
    }

    private static Workbook CreateInvalidWorksheetDimensionSourceWorkbook()
    {
        var workbook = CreateWorksheetDimensionSourceWorkbook();
        workbook.Name = "WorksheetDimensionInvalidSchema";
        workbook.GetSheetAt(0).DimensionMetadata = CreateWorksheetDimensionMetadata();
        return workbook;
    }

    private static NativeXmlPreserveBag CreateWorksheetDimensionMetadata()
    {
        var bag = new NativeXmlPreserveBag();
        bag.Set("dimension", """<e nativeDimensionAttr="kept" />""");
        return bag;
    }

    private static void SetWorksheetDimensionInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var dimension = worksheetXml.Root!.Element(worksheetNs + "dimension")!;
        dimension.SetAttributeValue("nativeDimensionAttr", "kept");
        dimension.Add(new XElement(worksheetNs + "nativeDimensionChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetDimensionSanitized(MemoryStream stream)
    {
        var dimension = ReadWorksheetChildElement(stream, "dimension");
        dimension.Attribute("ref").Should().NotBeNull();
        dimension.Attribute("nativeDimensionAttr").Should().BeNull();
        dimension.Elements().Should().BeEmpty();
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

    private static Workbook CreateInvalidPageLayoutSourceWorkbook()
    {
        var workbook = CreatePageLayoutSourceWorkbook();
        workbook.Name = "PageLayoutInvalidSchema";
        var sheet = workbook.GetSheetAt(0);
        sheet.PrintOptionsMetadata = new NativeXmlPreserveBag();
        sheet.PrintOptionsMetadata.Set(
            "printOptions",
            """<e gridLinesSet="maybe" customAttr="print-native"><nativePrintOptionsChild /></e>""");
        sheet.PageMarginsMetadata = new NativeXmlPreserveBag();
        sheet.PageMarginsMetadata.Set(
            "pageMargins",
            """<e customAttr="page-margins-native"><nativePageMarginsChild /></e>""");
        sheet.PageSetupMetadata = new NativeXmlPreserveBag();
        sheet.PageSetupMetadata.Set(
            "pageSetup",
            """<e customAttr="page-setup-native"><nativePageSetupChild /></e>""");
        sheet.HeaderFooterMetadata = new NativeXmlPreserveBag();
        sheet.HeaderFooterMetadata.Set(
            "headerFooter",
            """<e nativeHeaderFooterAttr="kept"><nativeHeaderFooterChild /></e>""");
        return workbook;
    }

    private static void SetPageLayoutInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var root = worksheetXml.Root!;

        var printOptions = root.Element(worksheetNs + "printOptions")!;
        printOptions.SetAttributeValue("gridLines", "maybe");
        printOptions.SetAttributeValue("gridLinesSet", "maybe");
        printOptions.SetAttributeValue("customAttr", "print-native");
        printOptions.Add(new XElement(worksheetNs + "nativePrintOptionsChild"));

        var pageMargins = root.Element(worksheetNs + "pageMargins")!;
        pageMargins.SetAttributeValue("left", "not-a-number");
        pageMargins.SetAttributeValue("customAttr", "page-margins-native");
        pageMargins.Add(new XElement(worksheetNs + "nativePageMarginsChild"));

        var pageSetup = root.Element(worksheetNs + "pageSetup")!;
        pageSetup.SetAttributeValue("orientation", "sideways");
        pageSetup.SetAttributeValue("copies", "not-a-number");
        pageSetup.SetAttributeValue("customAttr", "page-setup-native");
        pageSetup.Add(new XElement(worksheetNs + "nativePageSetupChild"));

        var headerFooter = root.Element(worksheetNs + "headerFooter")!;
        headerFooter.SetAttributeValue("differentFirst", "maybe");
        headerFooter.SetAttributeValue("nativeHeaderFooterAttr", "kept");
        headerFooter.Add(new XElement(worksheetNs + "nativeHeaderFooterChild"));

        var sheetProperties = root.Element(worksheetNs + "sheetPr");
        if (sheetProperties is null)
        {
            sheetProperties = new XElement(worksheetNs + "sheetPr");
            root.AddFirst(sheetProperties);
        }

        var pageSetupProperties = sheetProperties.Element(worksheetNs + "pageSetUpPr");
        if (pageSetupProperties is null)
        {
            pageSetupProperties = new XElement(worksheetNs + "pageSetUpPr");
            sheetProperties.Add(pageSetupProperties);
        }

        pageSetupProperties.SetAttributeValue("fitToPage", "maybe");
        pageSetupProperties.SetAttributeValue("customAttr", "page-setup-properties-native");
        pageSetupProperties.Add(new XElement(worksheetNs + "nativePageSetupPropertiesChild"));

        var outlineProperties = sheetProperties.Element(worksheetNs + "outlinePr");
        if (outlineProperties is null)
        {
            outlineProperties = new XElement(worksheetNs + "outlinePr");
            sheetProperties.Add(outlineProperties);
        }

        outlineProperties.SetAttributeValue("summaryBelow", "maybe");
        outlineProperties.SetAttributeValue("customAttr", "outline-native");
        outlineProperties.Add(new XElement(worksheetNs + "nativeOutlineChild"));

        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertPageLayoutSanitized(MemoryStream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var printOptions = ReadWorksheetChildElement(stream, "printOptions");
        printOptions.Attribute("gridLines")?.Value.Should().NotBe("maybe");
        printOptions.Attribute("gridLinesSet").Should().BeNull();
        printOptions.Attribute("customAttr").Should().BeNull();
        printOptions.HasElements.Should().BeFalse();

        var pageMargins = ReadWorksheetChildElement(stream, "pageMargins");
        pageMargins.Attribute("left")!.Value.Should().NotBe("not-a-number");
        pageMargins.Attribute("customAttr").Should().BeNull();
        pageMargins.HasElements.Should().BeFalse();

        var pageSetup = ReadWorksheetChildElement(stream, "pageSetup");
        pageSetup.Attribute("orientation")?.Value.Should().NotBe("sideways");
        pageSetup.Attribute("copies")?.Value.Should().NotBe("not-a-number");
        pageSetup.Attribute("customAttr").Should().BeNull();
        pageSetup.HasElements.Should().BeFalse();

        var headerFooter = ReadWorksheetChildElement(stream, "headerFooter");
        headerFooter.Attribute("differentFirst")?.Value.Should().NotBe("maybe");
        headerFooter.Attribute("nativeHeaderFooterAttr").Should().BeNull();
        headerFooter.Element(worksheetNs + "nativeHeaderFooterChild").Should().BeNull();

        var sheetProperties = ReadWorksheetChildElement(stream, "sheetPr");
        var pageSetupProperties = sheetProperties.Element(worksheetNs + "pageSetUpPr")!;
        pageSetupProperties.Attribute("fitToPage")?.Value.Should().NotBe("maybe");
        pageSetupProperties.Attribute("customAttr").Should().BeNull();
        pageSetupProperties.HasElements.Should().BeFalse();

        var outlineProperties = sheetProperties.Element(worksheetNs + "outlinePr")!;
        outlineProperties.Attribute("summaryBelow")?.Value.Should().NotBe("maybe");
        outlineProperties.Attribute("customAttr").Should().BeNull();
        outlineProperties.HasElements.Should().BeFalse();
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

    private static Workbook CreateInvalidManualPageBreakSourceWorkbook()
    {
        var workbook = new Workbook("ManualPageBreakInvalidSchema");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(4);
        sheet.RowPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["manualBreakCount"] = "not-a-number",
                ["customAttr"] = "row-container-native"
            },
            BreakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>
            {
                [20] = new(StringComparer.Ordinal)
                {
                    ["min"] = "not-a-number",
                    ["max"] = "not-a-number",
                    ["man"] = "maybe",
                    ["pt"] = "maybe",
                    ["customAttr"] = "row-native"
                }
            }
        };
        sheet.ColumnPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["manualBreakCount"] = "not-a-number",
                ["customAttr"] = "col-container-native"
            },
            BreakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>
            {
                [4] = new(StringComparer.Ordinal)
                {
                    ["min"] = "not-a-number",
                    ["max"] = "not-a-number",
                    ["man"] = "maybe",
                    ["pt"] = "maybe",
                    ["customAttr"] = "col-native"
                }
            }
        };
        return workbook;
    }

    private static void SetManualPageBreakInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        SetInvalidPageBreakAttributes(worksheetXml.Root!.Element(worksheetNs + "rowBreaks")!, worksheetNs, "20", "row-native");
        SetInvalidPageBreakAttributes(worksheetXml.Root!.Element(worksheetNs + "colBreaks")!, worksheetNs, "4", "col-native");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void SetInvalidPageBreakAttributes(
        XElement pageBreaks,
        XNamespace worksheetNs,
        string validBreakId,
        string customAttr)
    {
        pageBreaks.SetAttributeValue("count", "not-a-number");
        pageBreaks.SetAttributeValue("manualBreakCount", "not-a-number");
        pageBreaks.SetAttributeValue("customAttr", "container-native");
        var breakElement = pageBreaks.Elements(worksheetNs + "brk")
            .Single(element => element.Attribute("id")?.Value == validBreakId);
        breakElement.SetAttributeValue("min", "not-a-number");
        breakElement.SetAttributeValue("max", "not-a-number");
        breakElement.SetAttributeValue("man", "maybe");
        breakElement.SetAttributeValue("pt", "maybe");
        breakElement.SetAttributeValue("customAttr", customAttr);
        pageBreaks.Add(new XElement(
            worksheetNs + "brk",
            new XAttribute("id", "not-a-number"),
            new XAttribute("max", "not-a-number"),
            new XAttribute("customAttr", "removed-invalid-break")));
        pageBreaks.Add(new XElement(worksheetNs + "nativePageBreakChild"));
    }

    private static void AssertManualPageBreaksSanitized(MemoryStream stream)
    {
        AssertPageBreaksSanitized(ReadWorksheetChildElement(stream, "rowBreaks"), "20");
        AssertPageBreaksSanitized(ReadWorksheetChildElement(stream, "colBreaks"), "4");
    }

    private static void AssertPageBreaksSanitized(XElement pageBreaks, string expectedBreakId)
    {
        var worksheetNs = pageBreaks.Name.Namespace;
        pageBreaks.Attribute("count")!.Value.Should().Be("1");
        pageBreaks.Attribute("manualBreakCount")!.Value.Should().Be("1");
        pageBreaks.Attribute("customAttr").Should().BeNull();
        pageBreaks.Elements().Should().ContainSingle();
        var breakElement = pageBreaks.Elements(worksheetNs + "brk").Should().ContainSingle().Subject;
        breakElement.Attribute("id")!.Value.Should().Be(expectedBreakId);
        breakElement.Attribute("min").Should().BeNull();
        breakElement.Attribute("max").Should().BeNull();
        breakElement.Attribute("man").Should().BeNull();
        breakElement.Attribute("pt").Should().BeNull();
        breakElement.Attribute("customAttr").Should().BeNull();
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

    private static void SetMergedCellInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var mergeCells = worksheetXml.Root!.Element(worksheetNs + "mergeCells")!;
        mergeCells.SetAttributeValue("count", "not-a-number");
        mergeCells.SetAttributeValue("nativeMergeContainerAttr", "kept");
        var mergeCell = mergeCells.Elements(worksheetNs + "mergeCell")
            .Single(element => element.Attribute("ref")?.Value == "A1:C1");
        mergeCell.SetAttributeValue("nativeMergeCellAttr", "kept");
        mergeCell.Add(new XElement(worksheetNs + "nativeMergeCellChild"));
        mergeCells.Add(new XElement(
            worksheetNs + "mergeCell",
            new XAttribute("ref", "not-a-range"),
            new XAttribute("nativeMergeCellAttr", "removed")));
        mergeCells.Add(new XElement(worksheetNs + "nativeMergeCellsChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertMergedCellsSanitized(MemoryStream stream)
    {
        var mergeCells = ReadWorksheetChildElement(stream, "mergeCells");
        var worksheetNs = mergeCells.Name.Namespace;
        mergeCells.Attribute("count")!.Value.Should().Be("2");
        mergeCells.Attribute("nativeMergeContainerAttr").Should().BeNull();
        mergeCells.Element(worksheetNs + "nativeMergeCellsChild").Should().BeNull();
        mergeCells.Elements(worksheetNs + "mergeCell").Should().HaveCount(2);

        var firstMergeCell = mergeCells.Elements(worksheetNs + "mergeCell")
            .Single(element => element.Attribute("ref")?.Value == "A1:C1");
        firstMergeCell.Attribute("nativeMergeCellAttr").Should().BeNull();
        firstMergeCell.Element(worksheetNs + "nativeMergeCellChild").Should().BeNull();

        mergeCells.Elements(worksheetNs + "mergeCell")
            .Select(element => element.Attribute("ref")?.Value)
            .Should()
            .NotContain("not-a-range");
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

    private static void AddCssFontFamilyRichComment(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var commentsXml = LoadPackageXml(archive, "xl/comments1.xml");
        var text = commentsXml.Root!
            .Element(workbookNs + "commentList")!
            .Elements(workbookNs + "comment")
            .Single(comment => comment.Attribute("ref")?.Value == "C2")
            .Element(workbookNs + "text")!;

        text.ReplaceNodes(new XElement(
            workbookNs + "r",
            new XElement(
                workbookNs + "rPr",
                new XElement(workbookNs + "rFont", new XAttribute("val", "\"Google Sans\", Roboto, sans-serif")),
                new XElement(workbookNs + "sz", new XAttribute("val", "9"))),
            new XElement(workbookNs + "t", "Original note")));
        ReplacePackageXml(archive, "xl/comments1.xml", commentsXml);
    }

    private static string ReadLegacyCommentRunFont(Stream stream, string reference)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return ReadPackageRootElement(stream, "xl/comments1.xml")
            .Element(workbookNs + "commentList")!
            .Elements(workbookNs + "comment")
            .Single(comment => comment.Attribute("ref")?.Value == reference)
            .Element(workbookNs + "text")!
            .Element(workbookNs + "r")!
            .Element(workbookNs + "rPr")!
            .Element(workbookNs + "rFont")!
            .Attribute("val")!
            .Value;
    }

    private static XElement ReadPackageRootElement(Stream stream, string entryName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        return new XElement(LoadPackageXml(archive, entryName).Root!);
    }

    private static XElement ReadWorksheetSingleCellTableRootElement(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string relationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableSingleCells";
        const string worksheetPath = "xl/worksheets/sheet1.xml";

        var relsXml = LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        var relationship = relsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Single(element => element.Attribute("Type")?.Value == relationshipType);
        var partPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, relationship.Attribute("Target")!.Value);
        return new XElement(LoadPackageXml(archive, partPath).Root!);
    }

    private static byte[] ReadWorksheetCustomPropertyPartBytes(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string relationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customProperty";
        const string worksheetPath = "xl/worksheets/sheet1.xml";

        var relsXml = LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
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
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        return new XElement(worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Elements(worksheetNs + "row")
            .Single(element => element.Attribute("r")?.Value == $"{row}"));
    }

}

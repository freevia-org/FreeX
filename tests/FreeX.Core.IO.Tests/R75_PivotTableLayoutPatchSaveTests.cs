using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R75-io-pivottable-layout-4-1: on a PATCH-save (hasSourcePackage=true) of a LOADED pivot,
/// <c>XlsxPivotTableWriter.Save</c> is gated off, and only <c>RewritePivotTableFilterState</c> patched the
/// preserved pivotTableDefinition -- so edits to Grand Totals / Report Layout / a data field's summary
/// function were silently dropped. Fixed by <c>XlsxFileAdapter.RewritePivotTableLayoutState</c>
/// (XlsxFileAdapter.SavePostProcessing.cs), which patches those settings on the preserved part in place.
/// </summary>
public sealed class R75_PivotTableLayoutPatchSaveTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void SaveThenReload_GrandTotalsToggledOff_SurvivesSourcePreservedSave()
    {
        using var source = SaveWorkbook(CreateTwoRowFieldPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.ShowRowGrandTotals.Should().BeTrue("the OOXML schema default for rowGrandTotals is true");

        // User unchecks "Show grand totals for rows" in FreeX.
        pivot.ShowRowGrandTotals = false;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var pivotRoot = ReadPivotXml(saved).Root!;
        pivotRoot.Attribute("rowGrandTotals")?.Value.Should().Be("0",
            "clearing ShowRowGrandTotals in the model must be reflected on the preserved pivotTableDefinition, not silently dropped");
        pivotRoot.Attribute("colGrandTotals")?.Value.Should().NotBe("0",
            "only the row grand-total flag was toggled -- the column grand-total flag must be untouched");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().ShowRowGrandTotals.Should().BeFalse();
    }

    [Fact]
    public void SaveThenReload_ReportLayoutChangedToOutline_SurvivesSourcePreservedSaveOnRootAndEachAxisField()
    {
        using var source = SaveWorkbook(CreateTwoRowFieldPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.ReportLayout.Should().Be(PivotReportLayout.Tabular);

        pivot.ReportLayout = PivotReportLayout.Outline;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var pivotRoot = ReadPivotXml(saved).Root!;
        pivotRoot.Attribute("compact")?.Value.Should().Be("0");
        pivotRoot.Attribute("outline")?.Value.Should().Be("1");
        pivotRoot.Attribute("outlineData")?.Value.Should().Be("1");

        var pivotFields = pivotRoot.Element(WorkbookNs + "pivotFields")!.Elements(WorkbookNs + "pivotField").ToList();
        var axisFields = pivotFields.Where(f => f.Attribute("axis")?.Value is "axisRow" or "axisCol").ToList();
        axisFields.Should().HaveCountGreaterThan(0);
        axisFields.Should().OnlyContain(f => f.Attribute("compact")!.Value == "0" && f.Attribute("outline")!.Value == "1",
            "real Excel renders each field's own header form from ITS OWN compact/outline attributes, so the " +
            "per-field attributes -- not just the table-level ones -- must reflect the new report layout");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().ReportLayout.Should().Be(PivotReportLayout.Outline);
    }

    [Fact]
    public void SaveThenReload_DataFieldSummaryFunctionChanged_SurvivesSourcePreservedSave()
    {
        using var source = SaveWorkbook(CreateTwoRowFieldPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.DataFields.Single().SummaryFunction.Should().Be("sum");

        // User changes the data field's summary function from Sum to Count in FreeX.
        pivot.DataFields[0] = pivot.DataFields[0] with { SummaryFunction = "count" };

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var dataFieldXml = ReadPivotXml(saved).Root!
            .Element(WorkbookNs + "dataFields")!
            .Element(WorkbookNs + "dataField")!;
        dataFieldXml.Attribute("subtotal")?.Value.Should().Be("count",
            "changing the data field's summary function in the model must survive a source-preserved save");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().DataFields.Single().SummaryFunction.Should().Be("count");
    }

    [Fact]
    public void SaveThenReload_UnrelatedCellEdit_LeavesPivotTableDefinitionByteIdentical()
    {
        // No-regression sibling: an edit that never touches the pivot model at all must leave the
        // preserved pivotTableDefinition part exactly as it was -- the new rewrite pass must not itself
        // introduce a spurious change on every save.
        using var source = SaveWorkbook(CreateTwoRowFieldPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var originalBytes = ReadPivotEntryBytes(source);

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 20, 20), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var savedBytes = ReadPivotEntryBytes(saved);
        savedBytes.Should().Equal(originalBytes,
            "a resave that never touched the pivot model must leave the preserved pivotTableDefinition part byte-unchanged");
    }

    private static Workbook CreateTwoRowFieldPivotWorkbook()
    {
        var workbook = new Workbook("PivotLayoutPatchSaveWorkbook");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:C3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", SharedItemCount: 2, ContainsString: true, SharedItems: ["East", "West"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Product", SharedItemCount: 2, ContainsString: true, SharedItems: ["Widget", "Gadget"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 9, 3)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    private static XDocument ReadPivotXml(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/pivotTables/pivotTable1.xml")!;
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    private static byte[] ReadPivotEntryBytes(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/pivotTables/pivotTable1.xml")!;
        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }
}

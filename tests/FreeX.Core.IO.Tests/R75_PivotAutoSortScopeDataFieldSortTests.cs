using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R75-io-pivottable-layout-4-4: a sort-by-data-field ("Sort by Total Revenue descending") is recorded on
/// a <c>&lt;pivotField&gt;</c> as <c>sortType</c> PLUS an <c>&lt;autoSortScope&gt;</c> child identifying
/// which data field drives the order. <c>ReadNativePivotFieldSorts</c> previously read only
/// <c>sortType</c>, misreading a sort-by-value as a plain Label sort ("Sort by Total Revenue descending"
/// silently became "sort Product names Z-A"). Fixed by reading the <c>&lt;autoSortScope&gt;</c>'s
/// <c>&lt;reference field="4294967294"&gt;&lt;x v="N"/&gt;&lt;/reference&gt;</c> -- the real Excel wire
/// format identifying data field N -- when present.
/// </summary>
public sealed class R75_PivotAutoSortScopeDataFieldSortTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_SortTypeWithAutoSortScopeReferencingDataField_ReadsAsValueSortByThatDataField()
    {
        using var source = SaveWorkbook(CreateProductRevenuePivotWorkbook());
        InjectSortTypeAndAutoSortScope(source, fieldIndex: 0, sortType: "descending", dataFieldIndex: 0);

        var loaded = new XlsxFileAdapter().Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();

        var sort = pivot.Sorts.Should().ContainSingle().Subject;
        sort.Target.Should().Be(PivotSortTarget.Value,
            "an autoSortScope identifying a data field means the row's order is driven by that data field's " +
            "VALUES, not the row field's own labels");
        sort.Direction.Should().Be(PivotSortDirection.Descending);
        sort.DataFieldIndex.Should().Be(0, "the injected autoSortScope's <x v=\"0\"/> names the first data field");
        sort.FieldIndex.Should().Be(0, "the sort applies to Product, pivotField index 0");
    }

    [Fact]
    public void Load_SortTypeWithoutAutoSortScope_StillReadsAsPlainLabelSort()
    {
        // Sibling no-regression: an ordinary A-Z/Z-A label sort (sortType with NO autoSortScope) must be
        // completely unaffected by the new autoSortScope handling.
        using var source = SaveWorkbook(CreateProductRevenuePivotWorkbook());
        InjectSortTypeAndAutoSortScope(source, fieldIndex: 0, sortType: "descending", dataFieldIndex: null);

        var loaded = new XlsxFileAdapter().Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();

        var sort = pivot.Sorts.Should().ContainSingle().Subject;
        sort.Target.Should().Be(PivotSortTarget.Label);
        sort.Direction.Should().Be(PivotSortDirection.Descending);
        sort.FieldIndex.Should().Be(0);
    }

    [Fact]
    public void Load_NoSortTypeAtAll_StillReadsNoSort()
    {
        // Sibling no-regression: a field with no sortType at all must still produce no PivotSortModel.
        using var source = SaveWorkbook(CreateProductRevenuePivotWorkbook());

        var loaded = new XlsxFileAdapter().Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();

        pivot.Sorts.Should().BeEmpty();
    }

    private static Workbook CreateProductRevenuePivotWorkbook()
    {
        var workbook = new Workbook("PivotAutoSortScopeWorkbook");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Product", SharedItemCount: 2, ContainsString: true, SharedItems: ["Widget", "Gadget"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Revenue", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Revenue", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    /// <summary>
    /// Rewrites pivotTable1.xml's <paramref name="fieldIndex"/>'th &lt;pivotField&gt; to carry sortType
    /// plus, when <paramref name="dataFieldIndex"/> is given, a real-Excel-shaped
    /// &lt;autoSortScope&gt;&lt;pivotArea&gt;&lt;references&gt;&lt;reference field="4294967294"&gt;
    /// &lt;x v="N"/&gt;&lt;/reference&gt; identifying that data field.
    /// </summary>
    private static void InjectSortTypeAndAutoSortScope(MemoryStream package, int fieldIndex, string sortType, int? dataFieldIndex)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/pivotTables/pivotTable1.xml")!;
            XDocument xml;
            using (var entryStream = entry.Open())
                xml = XDocument.Load(entryStream);

            var pivotField = xml.Root!.Element(WorkbookNs + "pivotFields")!.Elements(WorkbookNs + "pivotField").ElementAt(fieldIndex);
            pivotField.SetAttributeValue("sortType", sortType);
            pivotField.Element(WorkbookNs + "autoSortScope")?.Remove();

            if (dataFieldIndex is { } dfi)
            {
                var autoSortScope = new XElement(
                    WorkbookNs + "autoSortScope",
                    new XElement(
                        WorkbookNs + "pivotArea",
                        new XAttribute("dataOnly", "0"),
                        new XAttribute("outline", "0"),
                        new XAttribute("fieldPosition", "0"),
                        new XElement(
                            WorkbookNs + "references",
                            new XAttribute("count", "1"),
                            new XElement(
                                WorkbookNs + "reference",
                                new XAttribute("field", "4294967294"),
                                new XAttribute("selected", "0"),
                                new XElement(WorkbookNs + "x", new XAttribute("v", dfi.ToString()))))));

                // CT_PivotField schema order is items?, autoSortScope?, extLst? -- insert right after
                // <items> (always present) so a trailing extLst (e.g. the x14 fillDownLabels extension a
                // fresh FreeX save emits) stays last.
                var itemsElement = pivotField.Element(WorkbookNs + "items");
                if (itemsElement is not null)
                    itemsElement.AddAfterSelf(autoSortScope);
                else
                    pivotField.Add(autoSortScope);
            }

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/pivotTables/pivotTable1.xml");
            using var writeStream = newEntry.Open();
            xml.Save(writeStream);
        }

        package.Position = 0;
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }
}

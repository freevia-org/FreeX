using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R30-io-pivot-cache-deep-2: <see cref="XlsxPivotCacheReader"/> drops any &lt;m/&gt; (missing/blank) OOXML
/// sharedItems child before <see cref="PivotCacheFieldModel.SharedItems"/> is materialized, shifting every
/// later item's position out of alignment with the raw OOXML index space a native
/// &lt;pivotField&gt;&lt;items&gt;&lt;item x="N" hidden="1"/&gt; is defined against. The fix mirrors the
/// existing ReadNativePageFieldSelectedItem guard: decline to resolve a field's hidden-item selection when
/// the field's declared sharedItems @count (<see cref="PivotCacheFieldModel.SharedItemCount"/>) is larger
/// than the materialized <see cref="PivotCacheFieldModel.SharedItems"/> list, rather than silently keeping
/// or hiding the wrong item. These tests pin (1) the fixed decline-to-resolve behavior when a shift is
/// detectable, and (2) the sibling case -- no dropped item, so the raw index space matches -- still
/// resolves the correct visible-items list exactly as before.
///
/// R30-io-pivot-cache-deep-3: the modeled pivot-cache writer (XlsxPivotTableWriter.Cache.cs,
/// ToPivotCacheFieldXml) never emitted a native &lt;fieldGroup&gt;&lt;rangePr&gt; for date/number-range
/// grouping -- only a FreeX-private extLst extension real Excel ignores -- so a fresh workbook's first save
/// silently lost grouping. The fix emits the native element from the field's Grouping/GroupStart/
/// GroupEnd/GroupInterval; this is pinned by a save+reload round-trip below.
/// </summary>
public sealed class R30_PivotRowFieldSharedItemShiftAndFieldGroupTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_NativeRowFieldHiddenItem_WithRawMissingSharedItemShiftingIndices_DeclinesToResolveSelection()
    {
        // Raw OOXML sharedItems: <s v="North"/><m/><s v="South"/><s v="East"/> (indices 0..3, count="4").
        // The reader drops the <m/>, materializing SharedItems as ["North","South","East"] (only 3 entries,
        // shifted by one from index 1 onward). The native pivotField hides raw index 2 (the real "South").
        // Old (buggy) code indexed straight into the shifted materialized list (materialized[2] = "East")
        // and would have wrongly hidden "East" while wrongly keeping "South" visible.
        using var package = CreatePivotWorkbookPackage(withDroppedSharedItem: true, hiddenRawIndex: 2);

        var workbook = new XlsxFileAdapter().Load(package);

        var rowField = workbook.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject
            .RowFields.Should().ContainSingle().Subject;
        rowField.SourceFieldIndex.Should().Be(0);
        // Old (buggy) behavior indexed into the shifted list and returned ["North", "South"] (wrongly
        // hiding "East" and wrongly keeping "South"). The fix declines to guess and leaves it unresolved.
        rowField.SelectedItems.Should().BeNull();
    }

    [Fact]
    public void Load_NativeRowFieldHiddenItem_WithNoMissingSharedItems_StillResolvesVisibleSelection()
    {
        // Sibling case: no <m/> was ever dropped, so SharedItemCount matches the materialized list and the
        // raw item @x index lines up with it exactly like before the fix -- must keep resolving correctly.
        using var package = CreatePivotWorkbookPackage(withDroppedSharedItem: false, hiddenRawIndex: 1);

        var workbook = new XlsxFileAdapter().Load(package);

        var rowField = workbook.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject
            .RowFields.Should().ContainSingle().Subject;
        rowField.SourceFieldIndex.Should().Be(0);
        // Raw index 1 ("South") is hidden; the remaining visible items are "North" and "East".
        rowField.SelectedItems.Should().Equal("North", "East");
    }

    [Fact]
    public void SaveThenLoad_PivotCacheFieldWithMonthGrouping_RoundTripsNativeFieldGroup()
    {
        var workbook = CreatePivotWorkbook();
        workbook.PivotCaches.Single().Fields[0] = workbook.PivotCaches.Single().Fields[0] with
        {
            Grouping = PivotFieldGrouping.Month,
            GroupStart = 1,
            GroupEnd = 12,
        };

        var adapter = new XlsxFileAdapter();
        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var cacheXml = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotCache/pivotCacheDefinition1.xml");
        var cacheField = cacheXml.Root!
            .Element(WorkbookNs + "cacheFields")!
            .Elements(WorkbookNs + "cacheField")
            .First(field => string.Equals(field.Attribute("name")?.Value, "Region", StringComparison.Ordinal));
        var rangePr = cacheField.Element(WorkbookNs + "fieldGroup")?.Element(WorkbookNs + "rangePr");
        rangePr.Should().NotBeNull("the native fieldGroup/rangePr must be emitted, not just the FreeX extLst");
        rangePr!.Attribute("groupBy")!.Value.Should().Be("months");
        rangePr.Attribute("startNum")!.Value.Should().Be("1");
        rangePr.Attribute("endNum")!.Value.Should().Be("12");

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedField = loaded.PivotCaches.Single().Fields.Single(field => field.Name == "Region");
        loadedField.Grouping.Should().Be(PivotFieldGrouping.Month);
        loadedField.GroupStart.Should().Be(1);
        loadedField.GroupEnd.Should().Be(12);
    }

    [Fact]
    public void SaveThenLoad_PivotCacheFieldWithoutGrouping_DoesNotEmitFieldGroup()
    {
        // Sibling case: an ordinary (ungrouped) cache field must not gain a spurious fieldGroup element.
        var workbook = CreatePivotWorkbook();

        var adapter = new XlsxFileAdapter();
        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var cacheXml = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotCache/pivotCacheDefinition1.xml");
        var cacheField = cacheXml.Root!
            .Element(WorkbookNs + "cacheFields")!
            .Elements(WorkbookNs + "cacheField")
            .First(field => string.Equals(field.Attribute("name")?.Value, "Region", StringComparison.Ordinal));
        cacheField.Element(WorkbookNs + "fieldGroup").Should().BeNull();
    }

    private static MemoryStream CreatePivotWorkbookPackage(bool withDroppedSharedItem, int hiddenRawIndex)
    {
        var package = XlsxPackageTestHelper.SaveWorkbook(CreatePivotWorkbook());

        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml", document =>
        {
            var sharedItems = document.Root!
                .Element(WorkbookNs + "cacheFields")!
                .Elements(WorkbookNs + "cacheField")
                .First(field => string.Equals(field.Attribute("name")?.Value, "Region", StringComparison.Ordinal))
                .Element(WorkbookNs + "sharedItems")!;
            sharedItems.RemoveNodes();
            if (withDroppedSharedItem)
            {
                sharedItems.SetAttributeValue("count", "4");
                sharedItems.Add(
                    new XElement(WorkbookNs + "s", new XAttribute("v", "North")),
                    new XElement(WorkbookNs + "m"),
                    new XElement(WorkbookNs + "s", new XAttribute("v", "South")),
                    new XElement(WorkbookNs + "s", new XAttribute("v", "East")));
            }
            else
            {
                sharedItems.SetAttributeValue("count", "3");
                sharedItems.Add(
                    new XElement(WorkbookNs + "s", new XAttribute("v", "North")),
                    new XElement(WorkbookNs + "s", new XAttribute("v", "South")),
                    new XElement(WorkbookNs + "s", new XAttribute("v", "East")));
            }
        });

        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            var pivotField = document.Root!
                .Element(WorkbookNs + "pivotFields")!
                .Elements(WorkbookNs + "pivotField")
                .First();
            var items = pivotField.Element(WorkbookNs + "items")!;
            items.RemoveNodes();
            items.SetAttributeValue("count", "1");
            items.Add(new XElement(
                WorkbookNs + "item",
                new XAttribute("x", hiddenRawIndex.ToString()),
                new XAttribute("hidden", "1")));
        });

        return package;
    }

    private static Workbook CreatePivotWorkbook()
    {
        var workbook = new Workbook("PivotRowFieldSharedItemShift");
        var sheet = workbook.AddSheet("PivotData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            SharedItemCount: 3,
            ContainsString: true,
            SharedItems: ["North", "South", "East"],
            SharedItemKinds: ['s', 's', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }
}

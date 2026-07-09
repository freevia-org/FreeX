using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R19-pivot-cache-records-1/2/3: pivot cache definition vs. cache records fidelity.
///
/// (1) <c>ToPivotCacheSharedItemsXml</c> used to re-emit the raw, preserved
/// <c>sharedItems/@count</c> even though the reader silently drops any <c>&lt;m/&gt;</c> (missing-value)
/// item when populating <see cref="PivotCacheFieldModel.SharedItems"/> -- producing a saved
/// <c>sharedItems</c> element whose <c>count</c> attribute no longer matched the number of child
/// elements actually written, which Excel treats as unreadable content.
///
/// (2) <see cref="AddPivotTableCommand"/> used to build every cache field via
/// <c>new PivotCacheFieldModel(header)</c>, leaving every type/range flag at its default -- so a
/// FreeX-created pivot over a numeric column saved a bare, type-blind <c>&lt;sharedItems/&gt;</c> that
/// Excel's schema defaults interpret as text-only.
///
/// (3) Cache-field metadata loaded once at file-open time was never resynced against edited source
/// data before save, so an edited-then-saved workbook could round-trip with a cache definition that
/// contradicts its own freshly regenerated cache records.
/// </summary>
public sealed class R19_pivot_cache_Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XElement ReadSharedItemsElement(MemoryStream package, string fieldName)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!;
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var cacheField = document.Root!
            .Element(WorkbookNs + "cacheFields")!
            .Elements(WorkbookNs + "cacheField")
            .Single(field => field.Attribute("name")!.Value == fieldName);
        return cacheField.Element(WorkbookNs + "sharedItems")!;
    }

    // records-1 + records-2 end-to-end: create a pivot over a numeric column entirely inside FreeX
    // (never loaded from an existing xlsx), save it, and verify the saved sharedItems for the numeric
    // field both declares containsNumber and has a count attribute equal to its actual child count.
    [Fact]
    public void CreatedPivot_OverNumericColumn_SavesSharedItemsDeclaringNumberWithMatchingCount()
    {
        var workbook = new Workbook("PivotCacheCreate");
        var sheet = workbook.AddSheet("Data");
        SetText(sheet, 1, 1, "Category");
        SetText(sheet, 1, 2, "Amount");
        var rows = new (string Category, double Amount)[]
        {
            ("A", 10), ("B", 20), ("A", 30), ("C", 40),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var row = (uint)i + 2;
            SetText(sheet, row, 1, rows[i].Category);
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(rows[i].Amount));
        }

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        var targetRange = new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 10, 6));
        var ctx = new TestCommandContext(workbook);
        var command = new AddPivotTableCommand(
            sheet.Id, sourceRange, targetRange, "PivotTable1",
            rowFieldIndexes: [0], dataFieldIndexes: [1]);
        command.Apply(ctx).Success.Should().BeTrue();

        // Sanity check on the in-memory model before touching the file layer at all (records-2).
        var amountField = workbook.PivotCaches.Single().Fields.Single(f => f.Name == "Amount");
        amountField.ContainsNumber.Should().BeTrue("the Amount column is entirely numeric source data");
        amountField.ContainsString.Should().BeFalse();
        amountField.MinValue.Should().Be(10);
        amountField.MaxValue.Should().Be(40);

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);

        var sharedItems = ReadSharedItemsElement(ms, "Amount");
        var containsNumberAttribute = sharedItems.Attribute("containsNumber");
        containsNumberAttribute.Should().NotBeNull(
            "a FreeX-created pivot over a numeric column must declare containsNumber on save (records-2)");
        containsNumberAttribute!.Value.Should().Be("1");
        sharedItems.Attribute("containsString").Should().BeNull();

        // Freshly created fields carry no explicit shared-item list (only the widened type/range
        // metadata above), so no "count" attribute is expected here -- but if one is ever emitted, it
        // must never disagree with the number of children actually written (records-1). The unambiguous,
        // always-non-vacuous regression test for the stale-count bug itself is
        // SharedItemsWithStalePreservedCount_SavesCountMatchingActualChildren below.
        var countAttribute = sharedItems.Attribute("count");
        if (countAttribute is not null)
        {
            int.Parse(countAttribute.Value).Should().Be(sharedItems.Elements().Count(),
                "sharedItems/@count must equal the number of emitted child items or Excel flags the part unreadable (records-1)");
        }
    }

    // records-1: directly exercises the stale-count path. A field whose SharedItemCount was preserved
    // from a raw sharedItems/@count that included a filtered-out <m/> item (so SharedItemCount is
    // larger than SharedItems.Count, mirroring what XlsxPivotCacheReader produces) must not re-emit that
    // stale, too-large count on save -- it must equal the number of items actually written.
    [Fact]
    public void SharedItemsWithStalePreservedCount_SavesCountMatchingActualChildren()
    {
        var workbook = new Workbook("PivotCacheStaleCount");
        var sheet = workbook.AddSheet("Data");
        SetText(sheet, 1, 1, "Category");
        SetText(sheet, 1, 2, "Amount");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B2",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        // Simulates what XlsxPivotCacheReader.Load produces for a field whose original sharedItems had
        // 5 children including one bare <m/> (missing) item: the raw count is preserved (5) but the
        // filtered SharedItems list only has the 4 surviving entries.
        cache.Fields.Add(new PivotCacheFieldModel(
            "Category",
            SharedItemCount: 5,
            ContainsString: true,
            SharedItems: ["A", "B", "C", "D"],
            SharedItemKinds: ['s', 's', 's', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 5, 5)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);

        var sharedItems = ReadSharedItemsElement(ms, "Category");
        var actualChildCount = sharedItems.Elements().Count();
        actualChildCount.Should().Be(4, "only the 4 non-missing shared items are ever emitted as children");
        int.Parse(sharedItems.Attribute("count")!.Value).Should().Be(actualChildCount,
            "the stale preserved SharedItemCount (5) must not be re-emitted once it disagrees with the actual emitted children");
    }

    // records-3: a cache field's metadata was populated once (as if loaded from an Excel-authored file
    // declaring a numeric-only column with a known min/max) and never resynced. Editing the underlying
    // source data to include a non-numeric value, then saving, must widen the saved sharedItems metadata
    // (containsString + containsMixedTypes) and the numeric bounds to agree with what the cache records
    // just regenerated from the live worksheet actually contain.
    [Fact]
    public void EditedNumericColumn_ResyncsCacheDefinitionMetadataBeforeSave()
    {
        var workbook = new Workbook("PivotCacheResync");
        var sheet = workbook.AddSheet("Data");
        SetText(sheet, 1, 1, "Amount");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(1000));
        // This cell was edited after the cache metadata below was captured (e.g. at file-open time).
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("N/A"));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:A4",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        // Stale metadata: as if loaded from an Excel file when the column was numeric-only.
        cache.Fields.Add(new PivotCacheFieldModel(
            "Amount",
            ContainsNumber: true,
            MinValue: 10,
            MaxValue: 1000));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 4)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(0, "Count of Amount", "count"));
        sheet.PivotTables.Add(pivot);

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);

        var sharedItems = ReadSharedItemsElement(ms, "Amount");
        var containsNumberAttribute = sharedItems.Attribute("containsNumber");
        containsNumberAttribute.Should().NotBeNull("the field is still numeric for two of its three data rows");
        containsNumberAttribute!.Value.Should().Be("1");

        var containsStringAttribute = sharedItems.Attribute("containsString");
        containsStringAttribute.Should().NotBeNull(
            "the cache definition must widen to reflect the edited text value now present in the live records (records-3)");
        containsStringAttribute!.Value.Should().Be("1");

        var containsMixedTypesAttribute = sharedItems.Attribute("containsMixedTypes");
        containsMixedTypesAttribute.Should().NotBeNull(
            "a field observed with both numeric and string data must be marked mixed so it agrees with its own records");
        containsMixedTypesAttribute!.Value.Should().Be("1");
    }

    private static void SetText(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));
}

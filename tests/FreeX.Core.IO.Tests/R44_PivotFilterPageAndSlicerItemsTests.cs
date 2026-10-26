using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R44-io-pivot-filter-page-3-1: on the source-package (hasSourcePackage) save path,
/// <c>XlsxPivotTableWriter.Save</c> -- the only code that regenerates a pivot table's
/// <c>&lt;pivotFields&gt;/&lt;items&gt;</c> hidden flags and <c>&lt;pageFields&gt;</c> from the live
/// <see cref="PivotTableModel"/> -- is gated behind <c>!hasSourcePackage</c>, so it never runs for a
/// workbook loaded from an existing .xlsx. The preserved pivotTableDefinition part is copied verbatim,
/// silently discarding any edit to a field's manual item filter or page/report filter selection. Fixed
/// by <c>XlsxFileAdapter.RewritePivotTableFilterState</c> (XlsxFileAdapter.SavePostProcessing.cs), which
/// patches just those two things on the preserved part in place.
///
/// R44-io-pivot-filter-page-3-2: a pivot slicer's cache definition never carried the native
/// <c>&lt;data&gt;&lt;tabular&gt;&lt;items&gt;</c> item/selection list -- only a private fx: extension --
/// so real Excel (and FreeX's own reload, which gates <c>SlicerItemResolver</c> on
/// <see cref="SlicerModel.CacheItems"/> being non-empty) had nothing to draw the slicer's button tiles
/// from. Fixed by <c>XlsxSlicerTimelineWriter.BuildPivotSlicerCacheDataElement</c>.
///
/// R83-io-slicer-tabular-pivotcacheid: that same native <c>&lt;tabular&gt;</c> element was emitted
/// WITHOUT the <c>pivotCacheId</c> attribute the OOXML schema (CT_TabularSlicerCache, x14 namespace)
/// marks required, so every freshly-authored .xlsx with a pivot slicer bound to a field with resolvable
/// shared items failed strict schema validation ("The required attribute 'pivotCacheId' is missing") and
/// could be repaired destructively by real Excel on open. Fixed by stamping the owning pivot cache's
/// CacheId onto the <c>&lt;tabular&gt;</c> element in the same method.
/// </summary>
public sealed class R44_PivotFilterPageAndSlicerItemsTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    // ── R44-io-pivot-filter-page-3-1 ────────────────────────────────────────────────────────────

    [Fact]
    public void SaveThenReload_UnhiddenManualItemFilter_SurvivesSourcePreservedSave()
    {
        // Simulates: open an Excel-authored workbook whose "Region" row field has "West" hidden by a
        // manual item filter, re-check "West" in FreeX, save the SAME file. Before the fix the saved
        // pivotTableDefinition part is byte-identical to the original -- "West" stays hidden.
        using var source = SaveWorkbook(CreateRegionPivotWorkbook());
        InjectNativeManualItemFilter(source, hiddenIndexes: [1]); // "West" (index 1) hidden.

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.RowFields.Single().SelectedItems.Should().BeEquivalentTo(["East"],
            "the native hidden-item flag injected above must resolve to only 'East' being visible");

        // User re-checks "West" in the field's filter dialog: SelectedItems now includes both values.
        pivot.RowFields[0] = pivot.RowFields[0] with { SelectedItems = ["East", "West"] };

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var itemsXml = ReadPivotFieldItems(saved, fieldIndex: 0);
        itemsXml.Should().OnlyContain(item => item.Attribute("hidden") == null,
            "un-hiding an item in the model must clear the native hidden flag on save, not leave the " +
            "preserved (pre-edit) XML untouched");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSelectedItems = reloaded.GetSheetAt(0).PivotTables.Single().RowFields.Single().SelectedItems;
        // When NO items are hidden, XlsxPivotTableReader.Fields.cs's ReadNativePivotFieldSelections
        // legitimately records no selection at all (null means "unfiltered", exactly like a field that
        // was never manually filtered) rather than an explicit list of every item -- so null here is the
        // CORRECT round-tripped "West is no longer excluded" outcome, not a regression.
        (reloadedSelectedItems is null || reloadedSelectedItems.Contains("West"))
            .Should().BeTrue("West must no longer be reported as excluded now that nothing is hidden");
    }

    [Fact]
    public void SaveThenReload_UnrelatedCellEdit_PreservesOriginalManualItemFilter()
    {
        // Sibling/no-regression: an edit that never touches the pivot model at all must leave the
        // preserved manual item filter exactly as it was -- the new rewrite pass must not itself start
        // clearing or corrupting hidden flags it wasn't asked to change.
        using var source = SaveWorkbook(CreateRegionPivotWorkbook());
        InjectNativeManualItemFilter(source, hiddenIndexes: [1]);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var itemsXml = ReadPivotFieldItems(saved, fieldIndex: 0);
        itemsXml.Single(item => item.Attribute("x")?.Value == "1")
            .Attribute("hidden")!.Value.Should().Be("1",
                "an untouched manual item filter must survive a resave that never mutated it");
        itemsXml.Single(item => item.Attribute("x")?.Value == "0")
            .Attribute("hidden").Should().BeNull();
    }

    [Fact]
    public void SaveThenReload_EditedPageFieldSelection_SurvivesSourcePreservedSave()
    {
        // Simulates: an Excel-authored workbook's page/report filter for "Region" is set to "East";
        // the user changes it to "West" in FreeX and saves the same file.
        var workbook = CreateRegionPivotWorkbook();
        workbook.GetSheetAt(0).PivotTables.Single().PageFields.Add(new PivotFieldModel(0));
        using var source = SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.PageFields.Should().ContainSingle();

        pivot.PageFields[0] = pivot.PageFields[0] with { SelectedItem = "East" };

        using var firstSave = new MemoryStream();
        adapter.Save(loaded, firstSave);
        firstSave.Position = 0;
        var reloadedOnce = adapter.Load(firstSave);
        var reloadedPivot = reloadedOnce.GetSheetAt(0).PivotTables.Single();
        reloadedPivot.PageFields.Single().SelectedItem.Should().Be("East");

        // Now change the selection and force the source-preserved path on a SECOND save of this
        // already-loaded workbook.
        reloadedPivot.PageFields[0] = reloadedPivot.PageFields[0] with { SelectedItem = "West" };

        using var saved = new MemoryStream();
        adapter.Save(reloadedOnce, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var pageFieldXml = ReadPivotXml(saved).Root!
            .Element(WorkbookNs + "pageFields")!
            .Element(WorkbookNs + "pageField")!;
        pageFieldXml.Attribute("item")!.Value.Should().Be("1",
                "the page filter's selected item must be rewritten to West's shared-item index (1)");

        saved.Position = 0;
        var reloadedTwice = adapter.Load(saved);
        reloadedTwice.GetSheetAt(0).PivotTables.Single().PageFields.Single().SelectedItem.Should().Be("West");
    }

    [Fact]
    public void Save_OverlappingDuplicatePivotFields_UsesExistingAxisAndLastSelectionPrecedence()
    {
        using var source = SaveWorkbook(CreateRegionPivotWorkbook());
        InjectNativeManualItemFilter(source, hiddenIndexes: [1]); // Region's original Row field selects East.

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        var region = pivot.RowFields.Single();

        // These overlaps are unusual but valid model input. The preserved-part implementation has
        // always chosen Row for the pivotField@axis, but the last matching field across Row,
        // Column, then Page for item-filter state and the last Page field for page selection.
        pivot.ColumnFields.Add(region with { SelectedItems = ["East"] });
        pivot.PageFields.Add(region with { SelectedItem = "East", SelectedItems = ["East"] });
        pivot.PageFields.Add(region with { SelectedItem = "West", SelectedItems = ["West"] });

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadPivotXml(saved).Root!;
        root.Element(WorkbookNs + "pivotFields")!
            .Elements(WorkbookNs + "pivotField")
            .First()
            .Attribute("axis")!.Value.Should().Be("axisRow",
                "axis precedence remains Row > Column > Page even when the source field is duplicated");

        var pageFields = root.Element(WorkbookNs + "pageFields")!
            .Elements(WorkbookNs + "pageField")
            .ToList();
        pageFields.Should().HaveCount(2);
        pageFields.Should().OnlyContain(field => field.Attribute("item") != null && field.Attribute("item")!.Value == "1",
            "both preserved page-field entries use the last same-index Page model selection, West");

        var items = ReadPivotFieldItems(saved, fieldIndex: 0);
        items.Single(item => item.Attribute("x")?.Value == "0")
            .Attribute("hidden")!.Value.Should().Be("1",
                "the last Page field's SelectedItems takes precedence over Row/Column selections");
        items.Single(item => item.Attribute("x")?.Value == "1")
            .Attribute("hidden").Should().BeNull();
    }

    private static Workbook CreateRegionPivotWorkbook()
    {
        var workbook = new Workbook("PivotFilterPageWorkbook");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
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
            SharedItemCount: 2,
            ContainsString: true,
            SharedItems: ["East", "West"],
            SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    /// <summary>
    /// Rewrites pivotTable1.xml's first (Region) pivotField's &lt;items&gt; to a genuine native,
    /// per-value list with the given raw shared-item indexes hidden -- what a real Excel-authored
    /// manual item filter looks like (the fresh FreeX writer only ever emits a single
    /// &lt;item t="default"/&gt; placeholder, never per-value hidden flags).
    /// </summary>
    private static void InjectNativeManualItemFilter(MemoryStream package, int[] hiddenIndexes)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/pivotTables/pivotTable1.xml")!;
            XDocument xml;
            using (var entryStream = entry.Open())
                xml = XDocument.Load(entryStream);

            var pivotField = xml.Root!.Element(WorkbookNs + "pivotFields")!.Elements(WorkbookNs + "pivotField").First();
            var items = new XElement(
                WorkbookNs + "items",
                new XAttribute("count", "3"),
                new XElement(WorkbookNs + "item", new XAttribute("x", "0"), hiddenIndexes.Contains(0) ? new XAttribute("hidden", "1") : null),
                new XElement(WorkbookNs + "item", new XAttribute("x", "1"), hiddenIndexes.Contains(1) ? new XAttribute("hidden", "1") : null),
                new XElement(WorkbookNs + "item", new XAttribute("t", "default")));
            pivotField.Element(WorkbookNs + "items")!.ReplaceWith(items);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/pivotTables/pivotTable1.xml");
            using var writeStream = newEntry.Open();
            xml.Save(writeStream);
        }

        package.Position = 0;
    }

    private static List<XElement> ReadPivotFieldItems(MemoryStream package, int fieldIndex)
    {
        var pivotFields = ReadPivotXml(package).Root!.Element(WorkbookNs + "pivotFields")!.Elements(WorkbookNs + "pivotField").ToList();
        return pivotFields[fieldIndex].Element(WorkbookNs + "items")!.Elements(WorkbookNs + "item").ToList();
    }

    private static XDocument ReadPivotXml(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/pivotTables/pivotTable1.xml")!;
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    // ── R44-io-pivot-filter-page-3-2 ────────────────────────────────────────────────────────────

    [Fact]
    public void SaveSlicerTimelines_PivotSlicerWithNoExplicitSelection_WritesNativeTabularItemsAllSelected()
    {
        using var saved = SaveWorkbook(CreateRegionPivotWorkbookWithSlicer());

        var items = ReadNativeSlicerCacheItems(saved);
        items.Should().HaveCount(2,
            "the native <data><tabular><items> list must carry one <i> per shared item so real Excel " +
            "(and FreeX's own reload) has buttons to render");
        items.Should().OnlyContain(item => item.Selected,
            "a slicer with no explicit SelectedItems recorded is in the unfiltered '(All)' state, so " +
            "every item starts selected");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var slicer = reloaded.Slicers.Should().ContainSingle().Subject;
        slicer.CacheItems.Should().HaveCount(2,
            "SlicerItemResolver gates entirely on CacheItems.Count > 0 -- an empty list here means the " +
            "reloaded slicer renders with zero item buttons");
    }

    [Fact]
    public void SaveSlicerTimelines_PivotSlicerWithExplicitSelection_MarksOnlySelectedItemsNative()
    {
        var workbook = CreateRegionPivotWorkbookWithSlicer();
        var slicer = workbook.Slicers.Single();
        slicer.SelectedItems.Add("East");

        using var saved = SaveWorkbook(workbook);

        var items = ReadNativeSlicerCacheItems(saved);
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(0,
            "only 'East' (shared-item index 0) was explicitly selected");
        items.Where(item => item.Index != 0).Should().OnlyContain(item => !item.Selected);
    }

    [Fact]
    public void SaveSlicerTimelines_TableSlicer_StillEmitsNoNativeTabularItemsList()
    {
        // No-regression sibling: a table slicer has no bound pivot cache field at all -- it must keep
        // relying purely on the x15:tableSlicerCache binding, exactly as before this fix.
        var workbook = new Workbook("TableSlicerNoRegressionR44");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Widget"));

        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "CategoryTable",
            DisplayName = "CategoryTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            HasAutoFilter = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(11, "Category"));
        sheet.StructuredTables.Add(table);

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            Caption = "Category",
            SourceFieldName = "Category",
            SourceTableId = 9,
            SourceTableColumnId = 11,
        });

        using var saved = SaveWorkbook(workbook);
        var cacheXml = ReadPackageXml(saved, "xl/slicerCaches/slicerCache1.xml");
        cacheXml.Root!.Descendants().Should().NotContain(element => element.Name.LocalName == "data",
            "a table slicer has no pivot cache field to resolve shared items from and must not gain a " +
            "<data> element from the pivot-slicer fix");
    }

    // ── R83-io-slicer-tabular-pivotcacheid ──────────────────────────────────────────────────────

    [Fact]
    public void SaveSlicerTimelines_FreshPivotSlicerWithSharedItems_TabularCarriesRequiredPivotCacheId()
    {
        // The default (no explicit selection) insert path -- a pivot slicer bound to a field WITH
        // resolvable shared items writes a native <data><tabular><items> list. CT_TabularSlicerCache
        // requires a pivotCacheId attribute on the <tabular> element; before the fix it was omitted, so
        // OpenXmlValidator(Microsoft365) reported "The required attribute 'pivotCacheId' is missing" on
        // /x14:slicerCacheDefinition/x14:data/x14:tabular. The value must be the OWNING pivot cache's id.
        using var saved = SaveWorkbook(CreateRegionPivotWorkbookWithSlicer());

        var tabular = ReadPackageXml(saved, "xl/slicerCaches/slicerCache1.xml")
            .Root!.Descendants(SlicerNs + "tabular").Should().ContainSingle().Subject;
        tabular.Attribute("pivotCacheId").Should().NotBeNull(
            "CT_TabularSlicerCache marks pivotCacheId required");
        tabular.Attribute("pivotCacheId")!.Value.Should().Be("1",
            "the tabular slicer cache's pivotCacheId must be the bound pivot cache's CacheId (1)");

        SchemaErrors(saved).Should().NotContain(error => error.Contains("pivotCacheId"),
            "the freshly-authored pivot slicer cache must no longer trip the required-pivotCacheId rule");
        SchemaErrors(saved).Should().BeEmpty(
            "a freshly-saved pivot slicer bound to a field with shared items must be schema-clean");
    }

    [Fact]
    public void SaveSlicerTimelines_FreshPivotSlicerWithSelection_ValidatesCleanAndSelectionRoundTrips()
    {
        // Sibling with an explicit selection ("West"), proving the pivotCacheId addition left the item /
        // selection payload the reader depends on untouched: the package validates clean AND the reloaded
        // slicer still reports exactly the same selected tile.
        var workbook = CreateRegionPivotWorkbookWithSlicer();
        workbook.Slicers.Single().SelectedItems.Add("West");

        using var saved = SaveWorkbook(workbook);

        SchemaErrors(saved).Should().BeEmpty(
            "adding the required pivotCacheId must make the pivot slicer package schema-clean");

        var items = ReadNativeSlicerCacheItems(saved);
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(1,
            "only 'West' (shared-item index 1) was explicitly selected");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;
        reloadedSlicer.CacheItems.Should().HaveCount(2,
            "both shared items must round-trip as cache items so the reloaded slicer renders its tiles");
        reloadedSlicer.CacheItems.Single(item => item.IsSelected).Index.Should().Be(1,
            "the reloaded slicer must still report only 'West' (index 1) as the selected tile");
    }

    private static Workbook CreateRegionPivotWorkbookWithSlicer()
    {
        var workbook = CreateRegionPivotWorkbook();
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2",
        });
        return workbook;
    }

    private static List<(int Index, bool Selected)> ReadNativeSlicerCacheItems(MemoryStream package)
    {
        var cacheXml = ReadPackageXml(package, "xl/slicerCaches/slicerCache1.xml");
        var itemsElement = cacheXml.Root!.Descendants(SlicerNs + "items").SingleOrDefault();
        itemsElement.Should().NotBeNull("the pivot slicer cache must carry a native <items> list");
        return itemsElement!.Elements(SlicerNs + "i")
            .Select(element => (
                Index: int.Parse(element.Attribute("x")!.Value),
                Selected: element.Attribute("s")?.Value == "1"))
            .ToList();
    }

    private static XDocument ReadPackageXml(MemoryStream package, string path)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(path)!;
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static List<string> SchemaErrors(Stream stream)
    {
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}

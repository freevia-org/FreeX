using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R111-io-structured-table-dategroup-roundtrip-1: a structured Table's AutoFilter
/// &lt;filterColumn&gt;&lt;filters&gt; whose ONLY children are &lt;dateGroupItem&gt; elements -- what
/// Excel writes for its built-in Year/Quarter/Month/Day date-checklist filter on a date column -- was
/// silently dropped in full on load. XlsxStructuredTableMetadataReader.ReadFilterColumns only ever
/// read &lt;filter val=.../&gt; children into Values; it had no dateGroupItem handling at all, unlike
/// the sheet-level WorksheetAutoFilterColumnModel/XlsxWorksheetAutoFilterXmlMapper path which reads
/// dateGroupItem into a typed DateGroups list. Since XlsxStructuredTableNativeMetadataReader
/// .ReadFilterXmls already excludes the whole "filters" element from the NativeFilterXmls raw-XML
/// passthrough (the same way it excludes "customFilters"), a filterColumn with only dateGroupItem
/// children had Values.Count==0 and no NativeFilterXmls fallback either -- it failed every disjunct of
/// the inclusion guard at the end of ReadFilterColumns and the whole filterColumn (including any
/// IncludeBlank flag on the very same &lt;filters&gt; element) vanished from the model. On the next
/// save, the column was completely unfiltered: no dropdown indicator, and the file no longer explains
/// why any data rows were left hidden.
///
/// These tests drive the real product entry point end to end: build a structured Table via the model
/// (there is no live FreeX command that authors a date-grouped filter -- this is an Excel-only
/// authoring path), save it with the real XlsxFileAdapter to get a valid package, inject the
/// dateGroupItem XML into the saved table part exactly as Excel would write it (mirroring the existing
/// worksheet-level AddWorksheetAutoFilterDateGroupMetadata fixture in FileAdapterSmokeTests.cs), then
/// load it back through XlsxFileAdapter.Load and assert on the resulting StructuredTableModel -- not on
/// any hand-built reader-internal fragment. A second save-and-reload proves the criterion round-trips
/// indefinitely, exactly like Excel does.
/// </summary>
public sealed class R111_StructuredTableDateGroupFilterRoundTripTests
{
    private static XNamespace WorkbookNs => "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static (Workbook Workbook, MemoryStream Package) BuildBaseTablePackage()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(45000));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(45031));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T1",
            DisplayName = "T1",
            Range = range,
            HasAutoFilter = true,
            Columns = { new StructuredTableColumnModel(1, "Date") }
        };
        sheet.StructuredTables.Add(table);

        var package = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(wb, package);
        package.Position = 0;
        return (wb, package);
    }

    /// <summary>
    /// Injects a &lt;filterColumn&gt;&lt;filters blank="1"&gt;&lt;dateGroupItem .../&gt;&lt;/filters&gt;
    /// &lt;/filterColumn&gt; into the already-saved table part -- the exact XML shape Excel writes for
    /// its Year/Quarter/Month/Day date-checklist filter, with NO plain &lt;filter val=.../&gt; sibling
    /// at all.
    /// </summary>
    private static void AddTableDateGroupFilterColumn(MemoryStream packageStream)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry("xl/tables/table1.xml")!;
        var tableXml = XlsxPackageTestFixtures.LoadPackageXml(entry);

        // Deliberately NO blank="1" and NO plain <filter val=.../> sibling -- this is the literal shape
        // from the defect report: a <filters> element whose ONLY content is dateGroupItem children.
        // Before the fix, every disjunct of ReadFilterColumns' inclusion guard is false for this exact
        // shape (Values.Count==0, IncludeBlank==false, no CustomFilters/ColorFilter/NativeFilterXmls
        // either), so the WHOLE filterColumn -- not just the date-group data -- is dropped.
        var autoFilter = tableXml.Root!.Element(WorkbookNs + "autoFilter")!;
        autoFilter.Add(new XElement(
            WorkbookNs + "filterColumn",
            new XAttribute("colId", "0"),
            new XElement(
                WorkbookNs + "filters",
                new XElement(
                    WorkbookNs + "dateGroupItem",
                    new XAttribute("year", "2023"),
                    new XAttribute("month", "3"),
                    new XAttribute("dateTimeGrouping", "month")))));

        entry.Delete();
        var newEntry = archive.CreateEntry("xl/tables/table1.xml", CompressionLevel.Optimal);
        using var writer = new StreamWriter(newEntry.Open());
        tableXml.Save(writer);
    }

    [Fact]
    public void DateGroupOnlyFilterColumn_SurvivesLoad_AndRoundTripsOnSecondSave()
    {
        var (_, package) = BuildBaseTablePackage();
        AddTableDateGroupFilterColumn(package);
        package.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var loadedTable = loaded.Sheets[0].StructuredTables.Single();

        // THE ACTUAL DEFECT: before the fix, a filterColumn whose <filters> element has ONLY
        // dateGroupItem children fails every disjunct of ReadFilterColumns' inclusion guard and is
        // dropped entirely -- FilterColumns would be empty here.
        loadedTable.FilterColumns.Should().ContainSingle(
            "a Table filterColumn carrying only Excel's date-grouped checklist criterion must not be dropped on load");
        var filterColumn = loadedTable.FilterColumns[0];
        filterColumn.ColumnId.Should().Be(0);
        filterColumn.IncludeBlank.Should().BeFalse();
        filterColumn.Values.Should().BeEmpty();
        filterColumn.DateGroups.Should().ContainSingle();
        var dateGroup = filterColumn.DateGroups[0];
        dateGroup.Year.Should().Be(2023);
        dateGroup.Month.Should().Be(3);
        dateGroup.DateTimeGrouping.Should().Be("month");

        // Re-save with no edits, then reload -- the criterion must keep round-tripping indefinitely,
        // exactly like Excel does, not just survive a single load.
        var secondSave = new MemoryStream();
        adapter.Save(loaded, secondSave);
        secondSave.Position = 0;

        using (var archive = new ZipArchive(secondSave, ZipArchiveMode.Read, leaveOpen: true))
        {
            var tableXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/tables/table1.xml");
            var filterColumnXml = tableXml.Root!
                .Element(WorkbookNs + "autoFilter")!
                .Element(WorkbookNs + "filterColumn");
            filterColumnXml.Should().NotBeNull("the filterColumn must survive the second save");
            var filtersXml = filterColumnXml!.Element(WorkbookNs + "filters");
            filtersXml.Should().NotBeNull();
            var dateGroupXml = filtersXml!.Element(WorkbookNs + "dateGroupItem");
            dateGroupXml.Should().NotBeNull(
                "BUG: the Table's date-grouped filter criterion must not vanish on save/reload");
            dateGroupXml!.Attribute("year")!.Value.Should().Be("2023");
            dateGroupXml.Attribute("month")!.Value.Should().Be("3");
            dateGroupXml.Attribute("dateTimeGrouping")!.Value.Should().Be("month");
        }

        secondSave.Position = 0;
        var loadedTwice = adapter.Load(secondSave);
        var reloadedTable = loadedTwice.Sheets[0].StructuredTables.Single();
        reloadedTable.FilterColumns.Should().ContainSingle(
            "the date-grouped filter criterion must keep round-tripping through any number of load/save cycles");
        reloadedTable.FilterColumns[0].DateGroups.Should().ContainSingle();
        reloadedTable.FilterColumns[0].DateGroups[0].Year.Should().Be(2023);
    }

    /// <summary>
    /// No-regression sibling: a Table filterColumn with ordinary plain &lt;filter val=.../&gt; checklist
    /// values (no dateGroupItem at all) must keep loading and round-tripping exactly as before --
    /// confirm the new DateGroups plumbing (added to both the inclusion guard and the writer's
    /// &lt;filters&gt; emission) does not disturb the pre-existing Values-only path.
    /// </summary>
    [Fact]
    public void PlainValueFilterColumn_StillRoundTrips_NoRegression()
    {
        var (_, package) = BuildBaseTablePackage();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/tables/table1.xml")!;
            var tableXml = XlsxPackageTestFixtures.LoadPackageXml(entry);
            var autoFilter = tableXml.Root!.Element(WorkbookNs + "autoFilter")!;
            autoFilter.Add(new XElement(
                WorkbookNs + "filterColumn",
                new XAttribute("colId", "0"),
                new XElement(
                    WorkbookNs + "filters",
                    new XElement(WorkbookNs + "filter", new XAttribute("val", "45000")))));
            entry.Delete();
            var newEntry = archive.CreateEntry("xl/tables/table1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(newEntry.Open());
            tableXml.Save(writer);
        }

        package.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var loadedTable = loaded.Sheets[0].StructuredTables.Single();

        loadedTable.FilterColumns.Should().ContainSingle();
        var filterColumn = loadedTable.FilterColumns[0];
        filterColumn.Values.Should().ContainSingle().Which.Should().Be("45000");
        filterColumn.DateGroups.Should().BeEmpty();
        filterColumn.IncludeBlank.Should().BeFalse();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var savedTableXml = XlsxPackageTestFixtures.LoadPackageXml(savedArchive, "xl/tables/table1.xml");
        var filtersXml = savedTableXml.Root!
            .Element(WorkbookNs + "autoFilter")!
            .Element(WorkbookNs + "filterColumn")!
            .Element(WorkbookNs + "filters");
        filtersXml.Should().NotBeNull();
        filtersXml!.Elements(WorkbookNs + "filter").Should().ContainSingle(f => f.Attribute("val")!.Value == "45000");
        filtersXml.Elements(WorkbookNs + "dateGroupItem").Should().BeEmpty(
            "a plain value filter must never gain a spurious dateGroupItem");
    }
}

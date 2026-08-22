using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// MED-severity regression: a structured Table filtered by a criterion FreeX cannot evaluate directly
/// (Top10/Above-Average/color/custom-condition -- preserved only as native XML) had its raw-hidden data
/// rows correctly reclassified into <see cref="Sheet.FilterHiddenRows"/> on load
/// (<see cref="XlsxStructuredTableModelMapper.MaterializeFilters"/>'s fallback branch), but registered
/// NO per-column ownership anywhere: not in <see cref="Sheet.ColumnFilterOwnedRows"/>, not in
/// <see cref="Sheet.ActiveValueFilterColumns"/>. The UI's "Clear Filter From &lt;Column&gt;" discovery
/// (MainWindow.DataFilterCommands.cs BuildClearAllValueFiltersCommand) walks the union of both
/// dictionaries' keys to find which columns have an active filter -- with neither populated, it never
/// finds this column, so "Clear Filter" silently never issues a <see cref="FilterCommand"/> for it and
/// the row stays hidden forever. Mirrors the analogous fix already shipped for a plain worksheet
/// AutoFilter (<see cref="XlsxWorksheetAutoFilterMaterializer"/>'s R98-io-autofilter-unsupported-
/// hiddenrows-1 fallback), which explicitly guards against this exact failure mode.
/// </summary>
public sealed class R118_TableUnsupportedFilterColumnOwnedRowsTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Workbook SaveAndReload(Workbook workbook)
    {
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        return adapter.Load(ms);
    }

    private static byte[] SaveToBytes(Workbook workbook)
    {
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        return ms.ToArray();
    }

    private static Workbook LoadFromBytes(byte[] package)
    {
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream(package);
        return adapter.Load(ms);
    }

    private static bool PackageHasHiddenRow(byte[] package, uint rowNumber)
    {
        using var ms = new MemoryStream(package);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var worksheet = XDocument.Parse(reader.ReadToEnd());
        var main = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        return worksheet
            .Descendants(main + "row")
            .Any(row => (string?)row.Attribute("r") == rowNumber.ToString() &&
                       (string?)row.Attribute("hidden") == "1");
    }

    private static Workbook BuildWorkbookWithIconFilteredTable(out GridRange tableRange)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(300));

        tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
            HasAutoFilter = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        // Column "Amount" (ColumnId 1, table-relative -> absolute col 2) filtered by an icon filter
        // FreeX cannot evaluate -- native XML only, exactly like Excel's Conditional Formatting icon
        // set AutoFilter ("Filter by Icon"). The xmlns is required here: XlsxStructuredTableWriter's
        // TryAddNativeTableElement only re-emits a NativeFilterXmls entry whose parsed
        // element.Name.Namespace matches the SpreadsheetML namespace, and XlsxStructuredTableNative-
        // MetadataReader.ReadFilterXmls (the real producer of this string on an actual load) always
        // includes it -- XElement.ToString() on a subtree re-declares the namespace it inherited from
        // its ancestor. A fixture without it would silently vanish on save (ROUND-TRIP FIXTURE RULE).
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(
            ColumnId: 1, Values: [], IncludeBlank: false,
            NativeFilterXml: "<iconFilter xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" iconSet=\"3TrafficLights1\" iconId=\"1\"/>"));
        sheet.StructuredTables.Add(table);

        // Simulates Excel having already evaluated that icon filter and hidden row 3 (West/200) the
        // last time the workbook was saved -- the source .xlsx's row 3 carries hidden="1" in its raw
        // row-layout XML, which XlsxFileAdapter.Save writes for any row in sheet.HiddenRows.
        sheet.HiddenRows.Add(3u);

        return wb;
    }

    /// <summary>
    /// Full round trip through the REAL product entry point (XlsxFileAdapter.Save/Load) followed by the
    /// REAL "Clear Filter From &lt;Column&gt;" command (FilterCommand with empty allowedValues at the
    /// column's offset), exactly as BuildClearAllValueFiltersCommand issues it once it finds the column
    /// via Sheet.ColumnFilterOwnedRows.Keys.
    /// </summary>
    [Fact]
    public void UnsupportedTableFilter_SingleColumn_RegistersColumnFilterOwnedRows_AndClearFilterRestoresRow()
    {
        var wb = BuildWorkbookWithIconFilteredTable(out var tableRange);
        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];

        // The row is still hidden after the load (Excel ground truth: it round-trips as hidden)...
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeTrue();
        // ...correctly reclassified as filter-hidden, not stranded as manually hidden...
        reloadedSheet.FilterHiddenRows.Should().Contain(3u);
        reloadedSheet.HiddenRows.Should().NotContain(3u);
        // ...and (the fix) owned by the Amount column (absolute col 2) so Clear Filter From <Column>
        // can find and release it.
        reloadedSheet.ColumnFilterOwnedRows.Should().ContainKey(2u);
        reloadedSheet.ColumnFilterOwnedRows[2u].Should().Contain(3u);

        var reloadedRange = new GridRange(
            new CellAddress(reloadedSheet.Id, tableRange.Start.Row, tableRange.Start.Col),
            new CellAddress(reloadedSheet.Id, tableRange.End.Row, tableRange.End.Col));
        var ctx = new TestCommandContext(reloaded);
        // Offset 1 = column 2 (Amount) within the table's range (Start.Col 1 + offset 1 = col 2),
        // exactly what BuildClearAllValueFiltersCommand computes from ColumnFilterOwnedRows.Keys.
        var clear = new FilterCommand(reloadedSheet.Id, reloadedRange, filterColOffset: 1, allowedValues: []);
        clear.Apply(ctx).Success.Should().BeTrue();

        reloadedSheet.FilterHiddenRows.Should().BeEmpty();
        reloadedSheet.HiddenRows.Should().BeEmpty();
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeFalse();
    }

    [Fact]
    public void UnsupportedTableFilter_SaveLoadSave_RetainsRawHiddenRow()
    {
        var wb = BuildWorkbookWithIconFilteredTable(out _);

        var firstPackage = SaveToBytes(wb);
        var firstReload = LoadFromBytes(firstPackage);
        firstReload.Sheets[0].FilterHiddenRows.Should().Contain(3u);
        firstReload.Sheets[0].HiddenRows.Should().NotContain(3u);

        var secondPackage = SaveToBytes(firstReload);
        PackageHasHiddenRow(secondPackage, 3u).Should().BeTrue(
            "a native-only structured-table filter still needs Excel's raw hidden visibility after the second save");

        var secondReload = LoadFromBytes(secondPackage);
        secondReload.Sheets[0].IsRowEffectivelyHidden(3).Should().BeTrue();
        secondReload.Sheets[0].FilterHiddenRows.Should().Contain(3u);
        secondReload.Sheets[0].HiddenRows.Should().NotContain(3u);
    }

    /// <summary>
    /// No-regression sibling: when TWO different table filter columns are unsupported, which one
    /// actually hid a given row is ambiguous, so no ownership should be guessed/registered for either
    /// -- mirroring XlsxWorksheetAutoFilterMaterializer's identical guard. The row must still be
    /// correctly reclassified into FilterHiddenRows (the pre-existing base fix), just without a
    /// per-column owner.
    /// </summary>
    [Fact]
    public void UnsupportedTableFilter_TwoUnsupportedColumns_DoesNotRegisterAmbiguousOwnership()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (uint r = 2; r <= 5; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r));
        sheet.HiddenRows.Add(3);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T",
            DisplayName = "T",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HasAutoFilter = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        // Column 1 (Region, absolute col 1): unsupported icon filter.
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(
            ColumnId: 0, Values: [], IncludeBlank: false,
            NativeFilterXml: "<iconFilter iconSet=\"3TrafficLights1\" iconId=\"1\"/>"));
        // Column 2 (Amount, absolute col 2): unsupported custom filter.
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(
            ColumnId: 1,
            Values: [],
            IncludeBlank: false,
            CustomFilters: [new StructuredTableCustomFilterModel("greaterThan", "100")],
            CustomFiltersAnd: false,
            NativeCustomFiltersAttributes: null,
            NativeFilterXmls: []));

        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        // Base fix still applies: the row is reclassified as filter-hidden.
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.HiddenRows.Should().NotContain(3u);
        // But with two unsupported columns, ownership is ambiguous -- neither column gets registered.
        sheet.ColumnFilterOwnedRows.Should().BeEmpty();
    }
}

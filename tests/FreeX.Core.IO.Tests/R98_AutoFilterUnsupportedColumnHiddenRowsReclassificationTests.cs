using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the HIGH-severity finding: rows hidden by an unsupported native AutoFilter
/// kind (Custom Number/Text Filter, the default date-grouped Year/Month/Day checklist, or a Cell/Font
/// Color filter) stayed stuck in <see cref="Sheet.HiddenRows"/> forever after a load, because
/// <see cref="XlsxWorksheetAutoFilterMaterializer.MaterializeFilters"/> skips columns it cannot
/// represent (CustomFilters/DateGroups/ColorFilter/IconFilter/unknown native filter attributes) and its
/// reclassification loop only ever moved a row out of HiddenRows when it failed a filter that WAS
/// built. Every filter-clearing path (<see cref="ToggleWorksheetAutoFilterCommand"/>,
/// <see cref="FilterCommand"/>'s Clear Filter path) only ever mutates FilterHiddenRows-adjacent state
/// and never touches HiddenRows, so such a row could never be surfaced again -- unlike real Excel,
/// where Clear Filter / Toggle AutoFilter off always restores every row the AutoFilter hid, regardless
/// of which filter mechanism hid it. Mirrors the fallback
/// <see cref="XlsxStructuredTableModelMapper.MaterializeFilters"/> already has for the structured-table
/// case (R95-io-autofilter-load-hiddenrows-1's sibling comment there).
/// </summary>
public sealed class R98_AutoFilterUnsupportedColumnHiddenRowsReclassificationTests
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

    /// <summary>
    /// Full round trip through the REAL product entry point (XlsxFileAdapter.Save/Load, which is what
    /// actually runs when a user opens/saves a workbook) followed by the REAL "Toggle AutoFilter off"
    /// command (Data &gt; Filter / Ctrl+Shift+L), exactly as a user would hit this: open a workbook
    /// whose AutoFilter used a Custom Number Filter Excel evaluated when it last saved (hiding row 3 via
    /// the raw &lt;row hidden="1"/&gt; bit FreeX cannot re-evaluate), then turn AutoFilter off.
    /// </summary>
    [Fact]
    public void WorksheetAutoFilter_SaveThenLoadThenToggleOff_CustomFilterHiddenRowBecomesVisibleAgain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(75));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        // A Custom Number Filter ("Amount > 100") -- FreeX cannot re-evaluate this on load, exactly
        // like the real "customFilters" element Excel writes for Data > Filter > Number Filters >
        // Greater Than.
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            [],
            IncludeBlank: false,
            CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThan", "100")],
            CustomFiltersAnd: false,
            NativeCustomFiltersAttributes: null,
            NativeFilterXmls: []));

        // Simulates Excel having already evaluated that Custom Filter and hidden row 3 (200 > 100) the
        // last time the workbook was saved -- the source .xlsx's row 3 carries hidden="1"' in its raw
        // row-layout XML, which XlsxFileAdapter.Save writes for any row in sheet.HiddenRows.
        sheet.HiddenRows.Add(3u);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];

        // The row is still hidden after the load (Excel ground truth: it round-trips as hidden)...
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeTrue();
        // ...but the fix requires it now be attributed to the AutoFilter (FilterHiddenRows), not
        // stranded as if manually hidden, so a filter-clearing command can restore it.
        reloadedSheet.FilterHiddenRows.Should().Contain(3u);
        reloadedSheet.HiddenRows.Should().NotContain(3u);

        var reloadedRange = new GridRange(
            new CellAddress(reloadedSheet.Id, range.Start.Row, range.Start.Col),
            new CellAddress(reloadedSheet.Id, range.End.Row, range.End.Col));
        var ctx = new TestCommandContext(reloaded);
        var toggleOff = new ToggleWorksheetAutoFilterCommand(reloadedSheet.Id, reloadedRange);
        toggleOff.Apply(ctx).Success.Should().BeTrue();

        // Real Excel: turning AutoFilter off always restores every row it hid, regardless of which
        // filter mechanism hid it.
        reloadedSheet.AutoFilter.Should().BeNull();
        reloadedSheet.FilterHiddenRows.Should().BeEmpty();
        reloadedSheet.HiddenRows.Should().BeEmpty();
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeFalse();
    }

    [Fact]
    public void WorksheetAutoFilter_SaveLoadSave_UnsupportedFilterRetainsRawHiddenRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(75));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            [],
            IncludeBlank: false,
            CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThan", "100")],
            CustomFiltersAnd: false,
            NativeCustomFiltersAttributes: null,
            NativeFilterXmls: []));
        sheet.HiddenRows.Add(3u);

        var firstPackage = SaveToBytes(wb);
        var firstReload = LoadFromBytes(firstPackage);
        firstReload.Sheets[0].FilterHiddenRows.Should().Contain(3u);
        firstReload.Sheets[0].HiddenRows.Should().NotContain(3u);

        var secondPackage = SaveToBytes(firstReload);
        PackageHasHiddenRow(secondPackage, 3u).Should().BeTrue(
            "a native-only worksheet filter still needs Excel's raw hidden visibility after the second save");

        var secondReload = LoadFromBytes(secondPackage);
        secondReload.Sheets[0].IsRowEffectivelyHidden(3).Should().BeTrue();
        secondReload.Sheets[0].FilterHiddenRows.Should().Contain(3u);
        secondReload.Sheets[0].HiddenRows.Should().NotContain(3u);
    }

    /// <summary>
    /// Same scenario but cleared via "Clear Filter From &lt;Column&gt;" (FilterCommand with an empty
    /// allowed-values list) instead of toggling the whole AutoFilter off.
    /// </summary>
    [Fact]
    public void WorksheetAutoFilter_SaveThenLoadThenClearFilter_CustomFilterHiddenRowBecomesVisibleAgain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(75));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            [],
            IncludeBlank: false,
            CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThan", "100")],
            CustomFiltersAnd: false,
            NativeCustomFiltersAttributes: null,
            NativeFilterXmls: []));
        sheet.HiddenRows.Add(3u);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeTrue();

        var reloadedRange = new GridRange(
            new CellAddress(reloadedSheet.Id, range.Start.Row, range.Start.Col),
            new CellAddress(reloadedSheet.Id, range.End.Row, range.End.Col));
        var ctx = new TestCommandContext(reloaded);
        var clear = new FilterCommand(reloadedSheet.Id, reloadedRange, filterColOffset: 0, allowedValues: []);
        clear.Apply(ctx).Success.Should().BeTrue();

        reloadedSheet.FilterHiddenRows.Should().BeEmpty();
        reloadedSheet.HiddenRows.Should().BeEmpty();
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeFalse();
    }

    /// <summary>
    /// No-regression sibling: the fallback added by this fix must stay scoped to the AutoFilter's own
    /// data range. A row hidden by Format &gt; Hide Row OUTSIDE that range (e.g. a completely unrelated
    /// row elsewhere on the sheet) must NOT be swept into FilterHiddenRows just because some column
    /// inside the AutoFilter happens to be unsupported -- that would let Clear Filter / Toggle AutoFilter
    /// off wrongly resurrect rows the AutoFilter never touched at all.
    /// </summary>
    [Fact]
    public void WorksheetAutoFilter_UnsupportedColumnPresent_ManuallyHiddenRowOutsideRangeStaysUntouched()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(300));
        // A row well below the AutoFilter's own range -- unrelated data, manually hidden.
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("Unrelated"));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        // Column A: a plain, fully-supported value-list filter ("East") -- row 3 (West) fails it.
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["East"]));
        // Column B: an unsupported customFilter -- makes unsupportedColumnCount > 0 for the sheet.
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            1,
            [],
            IncludeBlank: false,
            CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThan", "100")],
            CustomFiltersAnd: false,
            NativeCustomFiltersAttributes: null,
            NativeFilterXmls: []));

        // Row 10 is outside the AutoFilter's range entirely -- hidden purely via Format > Hide Row,
        // with nothing to do with this AutoFilter.
        sheet.HiddenRows.Add(10u);

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // Row 3 fails the supported value-list filter -- correctly filter-hidden, as before.
        sheet.FilterHiddenRows.Should().Contain(3u);
        // Row 10, outside the filter's row range, must be completely untouched by the fallback.
        sheet.FilterHiddenRows.Should().NotContain(10u);
        sheet.HiddenRows.Should().Contain(10u);

        var ctx = new TestCommandContext(workbook);
        var clear = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: []);
        clear.Apply(ctx).Success.Should().BeTrue();

        // Clear Filter over the AutoFilter's own range must not resurrect an out-of-range manually
        // hidden row, even though the sheet had an unsupported filter column elsewhere.
        sheet.HiddenRows.Should().Contain(10u);
        sheet.IsRowEffectivelyHidden(10).Should().BeTrue();
    }
}

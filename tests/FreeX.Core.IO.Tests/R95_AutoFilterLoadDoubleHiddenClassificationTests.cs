using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R95-io-autofilter-load-hiddenrows-1: a row hidden purely because it fails
/// an AutoFilter/structured-table filter's criteria must NOT also be loaded into
/// <see cref="Sheet.HiddenRows"/> on an .xlsx load, or every filter-clearing command
/// (<see cref="FilterCommand"/>, <see cref="ToggleWorksheetAutoFilterCommand"/>,
/// <see cref="StructuredTableFilterCommand"/>) -- which only ever mutate
/// <see cref="Sheet.FilterHiddenRows"/>/<see cref="Sheet.ValueFilterHiddenRows"/>/
/// <see cref="Sheet.ColumnFilterOwnedRows"/>, never <see cref="Sheet.HiddenRows"/> -- can never make
/// the row visible again after a save/reload round trip, exactly as a user would hit by closing and
/// reopening a workbook with an active AutoFilter and then choosing "Clear Filter".
/// </summary>
public sealed class R95_AutoFilterLoadDoubleHiddenClassificationTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Workbook BuildFilteredWorksheetWorkbook(out GridRange range)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));

        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        // Apply the filter through the REAL command entry point (matches the AutoFilter dropdown's
        // "Filter to East" action): this hides row 3 (West) purely via FilterHiddenRows/
        // ActiveValueFilterColumns, never touching HiddenRows -- the live-session baseline this load
        // path must reproduce after a save/reload round trip.
        var ctx = new TestCommandContext(wb);
        var apply = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["East"]);
        apply.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
        sheet.HiddenRows.Should().BeEmpty();

        return wb;
    }

    private static Workbook SaveAndReload(Workbook workbook)
    {
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        return adapter.Load(ms);
    }

    [Fact]
    public void WorksheetAutoFilter_SaveThenLoad_FilteredRowIsNotAlsoClassifiedAsManuallyHidden()
    {
        var wb = BuildFilteredWorksheetWorkbook(out _);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];

        // The row is still (correctly) hidden by the reloaded filter...
        reloadedSheet.FilterHiddenRows.Should().Contain(3u);
        // ...but must NOT also land in HiddenRows, or Clear Filter can never surface it again.
        reloadedSheet.HiddenRows.Should().NotContain(3u);
    }

    [Fact]
    public void WorksheetAutoFilter_Save_DoesNotSerializeFilterOwnedRowsAsRawHidden()
    {
        var wb = BuildFilteredWorksheetWorkbook(out _);

        var adapter = new XlsxFileAdapter();
        using var package = new MemoryStream();
        adapter.Save(wb, package);
        package.Position = 0;

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(
            archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var worksheet = XDocument.Parse(reader.ReadToEnd());
        var main = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var filteredRow = worksheet
            .Descendants(main + "row")
            .Single(row => (string?)row.Attribute("r") == "3");

        filteredRow.Attribute("hidden").Should().BeNull();
    }

    [Fact]
    public void WorksheetAutoFilter_SaveThenLoadThenClearFilter_RowBecomesVisibleAgain()
    {
        var wb = BuildFilteredWorksheetWorkbook(out var range);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeTrue();

        // Re-anchor the range/filterColOffset onto the reloaded sheet's id (SaveAndReload keeps the
        // same 1-based row/col layout, only the SheetId instance differs) and run the exact command
        // "Clear Filter From Region" dispatches -- FilterCommand with an empty allowed-values list.
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

    [Fact]
    public void WorksheetAutoFilter_NoRegression_RowManuallyHiddenOutsideFilterCriteria_SurvivesClearFilter()
    {
        // Sibling case: a row hidden by Format > Hide Row that the reloaded filter's OWN criteria
        // does NOT explain (it passes the "East" criterion) must still be recognized as genuinely
        // manually-hidden and must NOT be surfaced by Clear Filter.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        var ctx = new TestCommandContext(wb);
        var apply = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["East"]);
        apply.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEmpty(); // every row passes "East"

        // Manually hide row 3 (Format > Hide Row) -- this is independent of the filter.
        sheet.HiddenRows.Add(3);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];

        // Row 3 passes the reloaded filter's own criteria, so it is NOT reclassified as filter-hidden
        // -- it stays correctly attributed to HiddenRows (manual).
        reloadedSheet.FilterHiddenRows.Should().NotContain(3u);
        reloadedSheet.HiddenRows.Should().Contain(3u);

        var reloadedRange = new GridRange(
            new CellAddress(reloadedSheet.Id, range.Start.Row, range.Start.Col),
            new CellAddress(reloadedSheet.Id, range.End.Row, range.End.Col));
        var clearCtx = new TestCommandContext(reloaded);
        var clear = new FilterCommand(reloadedSheet.Id, reloadedRange, filterColOffset: 0, allowedValues: []);
        clear.Apply(clearCtx).Success.Should().BeTrue();

        // Clear Filter must NOT resurrect a genuinely manually-hidden row.
        reloadedSheet.HiddenRows.Should().Contain(3u);
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeTrue();
    }

    [Fact]
    public void StructuredTableFilter_SaveThenLoad_FilteredRowIsNotAlsoClassifiedAsManuallyHidden()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));

        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
            HasAutoFilter = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        sheet.StructuredTables.Add(table);

        // Apply the table's column filter through the real command entry point, exactly like the
        // table header's own filter dropdown does (FilterCommand.ApplyToStructuredTableIfMatched
        // keeps table.FilterColumns in sync when _range matches a table's range).
        var ctx = new TestCommandContext(wb);
        var apply = new FilterCommand(sheet.Id, tableRange, filterColOffset: 0, allowedValues: ["East"]);
        apply.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
        sheet.HiddenRows.Should().BeEmpty();

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];

        reloadedSheet.FilterHiddenRows.Should().Contain(3u);
        reloadedSheet.HiddenRows.Should().NotContain(3u);
    }
}

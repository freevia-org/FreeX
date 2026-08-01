using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R111-io-structured-table-colorfilter-roundtrip-1: a structured Table's own &lt;colorFilter&gt;
/// (Filter by Cell/Font Colour, or No Fill) never got parsed into the typed
/// StructuredTableFilterColumnModel.ColorFilter field -- it only ever landed in the generic
/// NativeFilterXmls raw-XML passthrough (XlsxStructuredTableMetadataReader.ReadFilterColumns via
/// XlsxStructuredTableNativeMetadataReader.ReadFilterXmls). But XlsxStructuredTableWriter's
/// ToFilterColumnXml unconditionally EXCLUDES "colorFilter" from that same passthrough (it only
/// re-emits the criterion from the typed ColorFilter field, which the reader never set). The net
/// effect: a Table AutoFilter colour-filter criterion that reaches the model via a real FILE LOAD --
/// as opposed to being freshly created by a live command in the current session -- was written out as
/// nothing at all on the very next save. This is a plain load -> save round trip, not an exotic path.
///
/// These tests drive the real product entry point end to end: build a table via a live command (the
/// same one R107's tests use), save it (first save, from a live-command-built model -- this always
/// worked), reload it (now it is "as if loaded from a file Excel or FreeX wrote"), and then save THAT
/// reloaded workbook again with no further edits (the second save -- this is exactly the step the
/// R107 tests never exercised, per this defect's evidence). Before the fix, the colorFilter element
/// vanishes from xl/tables/table1.xml on this second save; after the fix, it survives unchanged.
/// </summary>
public sealed class R111_StructuredTableColorFilterSecondSaveRoundTripTests
{
    private static XNamespace WorkbookNs => "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, GridRange Range) SetUpTableWithColoredCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Ready"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Blocked"));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T1",
            DisplayName = "T1",
            Range = range,
            HasAutoFilter = true,
            Columns = { new StructuredTableColumnModel(1, "Status") }
        };
        sheet.StructuredTables.Add(table);

        return (wb, sheet, ctx, range);
    }

    private static Workbook SaveAndReload(Workbook workbook, out MemoryStream saved)
    {
        saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var loaded = adapter.Load(saved);
        saved.Position = 0;
        return loaded;
    }

    [Fact]
    public void ColorFilter_SurvivesSecondSave_AfterLoadFromFile()
    {
        // Build + FIRST save via the real product entry point (Filter > By Cell Color on a Table
        // column), exactly like R107's coverage.
        var (wb, sheet, ctx, range) = SetUpTableWithColoredCell();
        var red = new CellColor(255, 0, 0);
        var redCellStyle = CellStyle.Default.Clone();
        redCellStyle.FillColor = red;
        var redStyle = wb.RegisterStyle(redCellStyle);
        sheet.GetCell(2, 1)!.StyleId = redStyle;

        var command = new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, red);
        command.Apply(ctx).Success.Should().BeTrue();

        var loadedOnce = SaveAndReload(wb, out var firstSave);

        using (var firstArchive = new ZipArchive(firstSave, ZipArchiveMode.Read, leaveOpen: true))
        {
            var firstTableXml = XlsxPackageTestFixtures.LoadPackageXml(firstArchive, "xl/tables/table1.xml", "the table part must exist after the first save");
            firstTableXml.Root!
                .Element(WorkbookNs + "autoFilter")!
                .Element(WorkbookNs + "filterColumn")!
                .Element(WorkbookNs + "colorFilter")
                .Should().NotBeNull("the first save (from the live command) must emit the colorFilter");
        }

        // The reloaded table's model must now carry the criterion in the typed ColorFilter field
        // (this is the actual fix under test: before it, the reader left this null and the criterion
        // only survived, precariously, in NativeFilterXmls).
        var reloadedTable = loadedOnce.Sheets[0].StructuredTables.Single();
        reloadedTable.FilterColumns.Should().ContainSingle();
        reloadedTable.FilterColumns[0].ColorFilter.Should().NotBeNull(
            "a loaded table colorFilter must be parsed into the typed ColorFilter field, not left for the (now-excluded) NativeFilterXmls passthrough");
        var dxfIdBeforeSecondSave = reloadedTable.FilterColumns[0].ColorFilter!.DifferentialFormatId;
        dxfIdBeforeSecondSave.Should().NotBeNull();

        // THE ACTUAL DEFECT: re-save the reloaded (i.e. "loaded from a file") workbook a SECOND time,
        // with no edits at all, and reload again.
        var loadedTwice = SaveAndReload(loadedOnce, out var secondSave);

        using var secondArchive = new ZipArchive(secondSave, ZipArchiveMode.Read, leaveOpen: true);
        var secondTableXml = XlsxPackageTestFixtures.LoadPackageXml(secondArchive, "xl/tables/table1.xml", "the table part must exist after the second save");
        var filterColumnXmlAfterSecondSave = secondTableXml.Root!
            .Element(WorkbookNs + "autoFilter")!
            .Element(WorkbookNs + "filterColumn");
        filterColumnXmlAfterSecondSave.Should().NotBeNull("the filterColumn element itself must survive the second save");
        var colorFilterXmlAfterSecondSave = filterColumnXmlAfterSecondSave!.Element(WorkbookNs + "colorFilter");
        colorFilterXmlAfterSecondSave.Should().NotBeNull(
            "BUG: the Table's own <colorFilter> criterion (and the dxfId naming its colour) must NOT vanish on a second save of a file that was merely loaded and re-saved");
        colorFilterXmlAfterSecondSave!.Attribute("dxfId")?.Value.Should().Be(
            dxfIdBeforeSecondSave!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "the second save must keep pointing at the exact same dxf (colour) as the first save, not renumber or drop it");

        var reloadedTwiceTable = loadedTwice.Sheets[0].StructuredTables.Single();
        reloadedTwiceTable.FilterColumns.Should().ContainSingle(
            "the filter column must still exist after a second load -- the criterion must not have disappeared entirely");
        reloadedTwiceTable.FilterColumns[0].ColorFilter.Should().NotBeNull(
            "the colorFilter criterion must keep round-tripping through any number of load/save cycles, exactly like Excel does");
    }

    /// <summary>
    /// No-regression sibling: a Table's &lt;top10&gt; filter criterion has no typed field at all (it
    /// is not part of this fix) and always flowed through the generic NativeFilterXmls passthrough --
    /// confirm it still survives a SECOND save unaffected by colorFilter's new exclusion from that
    /// same passthrough loop.
    /// </summary>
    [Fact]
    public void Top10Filter_StillSurvivesSecondSave_NoRegression()
    {
        var (wb, sheet, ctx, range) = SetUpTableWithColoredCell();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));

        var command = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 1, top: true);
        command.Apply(ctx).Success.Should().BeTrue();

        var loadedOnce = SaveAndReload(wb, out _);
        var reloadedTable = loadedOnce.Sheets[0].StructuredTables.Single();
        reloadedTable.FilterColumns.Should().ContainSingle();
        reloadedTable.FilterColumns[0].NativeFilterXmls.Should().ContainSingle(xml => xml.Contains("top10"),
            "top10 has no typed field on the table model and must keep flowing through NativeFilterXmls");

        var loadedTwice = SaveAndReload(loadedOnce, out var secondSave);

        using var secondArchive = new ZipArchive(secondSave, ZipArchiveMode.Read, leaveOpen: true);
        var secondTableXml = XlsxPackageTestFixtures.LoadPackageXml(secondArchive, "xl/tables/table1.xml", "the table part must exist after the second save");
        secondTableXml.Root!
            .Element(WorkbookNs + "autoFilter")!
            .Element(WorkbookNs + "filterColumn")!
            .Element(WorkbookNs + "top10")
            .Should().NotBeNull("top10 must still survive a second save, unaffected by colorFilter's new exclusion from the NativeFilterXmls passthrough");

        var reloadedTwiceTable = loadedTwice.Sheets[0].StructuredTables.Single();
        reloadedTwiceTable.FilterColumns.Should().ContainSingle();
        reloadedTwiceTable.FilterColumns[0].NativeFilterXmls.Should().ContainSingle(xml => xml.Contains("top10"));
    }
}

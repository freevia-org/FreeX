using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R107-commands-autofilter-table-color-sync-1: CellFillColorFilterCommand, CellNoFillColorFilterCommand
/// and CellFontColorFilterCommand only ever synced their picked colour into a plain worksheet-level
/// AutoFilter (WorksheetAutoFilterColumnSync, R87/R90) -- which is a documented no-op whenever the
/// filtered range is a structured table's own Range, since a table carries its own &lt;autoFilter&gt;
/// inside xl/tables/tableN.xml rather than a worksheet-level one (mirrors R106's identical fix for
/// TopBottomFilterCommand/FilterConditionCommand). These tests drive the real product entry points
/// (the commands themselves, never a hand-built model) through a full save+reload of an actual
/// structured table to confirm the criterion now lands in the table's own &lt;autoFilter&gt; XML --
/// including allocating a real dxfId into xl/styles.xml, exactly like the worksheet path already does
/// (R89/R90) -- instead of being silently dropped.
/// </summary>
public sealed class R107_StructuredTableColorFilterDxfRoundTripTests
{
    private static XNamespace WorksheetNs => "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

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

    private static (MemoryStream Saved, Workbook Loaded) SaveAndReload(Workbook workbook)
    {
        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var loaded = adapter.Load(saved);
        saved.Position = 0;
        return (saved, loaded);
    }

    [Fact]
    public void CellFillColorFilterCommand_StructuredTableRange_PersistsAcrossSaveReload()
    {
        var (wb, sheet, ctx, range) = SetUpTableWithColoredCell();
        var red = new CellColor(255, 0, 0);
        var redCellStyle = CellStyle.Default.Clone();
        redCellStyle.FillColor = red;
        var redStyle = wb.RegisterStyle(redCellStyle);
        sheet.GetCell(2, 1)!.StyleId = redStyle;

        // The real product entry point: Filter > By Cell Color on a Table column, picking red.
        var command = new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, red);
        command.Apply(ctx).Success.Should().BeTrue();

        var (saved, loaded) = SaveAndReload(wb);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var tableXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/tables/table1.xml", "the table part must exist");
        var colorFilterXml = tableXml.Root!
            .Element(WorksheetNs + "autoFilter")!
            .Element(WorksheetNs + "filterColumn")!
            .Element(WorksheetNs + "colorFilter")!;
        var dxfIdText = colorFilterXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty("the command-driven table colour filter must allocate a real dxfId, not omit it");

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "styles part must exist");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        var fgColor = dxfs[dxfIndex].Element(WorksheetNs + "fill")!.Element(WorksheetNs + "patternFill")!.Element(WorksheetNs + "fgColor")!;
        fgColor.Attribute("rgb")!.Value.Should().Be("FFFF0000", "the dxf allocated for a real command-driven table fill-colour filter must carry the exact picked colour");

        // No spurious worksheet-level <autoFilter> must have been created for the table's own range.
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "worksheet part must exist");
        worksheetXml.Root!.Element(WorksheetNs + "autoFilter").Should().BeNull();

        var reloadedTable = loaded.Sheets[0].StructuredTables.Single();
        reloadedTable.FilterColumns.Should().ContainSingle();
        // R111-io-structured-table-colorfilter-roundtrip-1: colorFilter is now parsed into the typed
        // ColorFilter field (no longer NativeFilterXmls passthrough), so it survives a SECOND
        // save+reload too -- see R111_StructuredTableColorFilter_SurvivesSecondSave below for the
        // regression this field enables catching.
        reloadedTable.FilterColumns[0].ColorFilter.Should().NotBeNull(
            "the saved colorFilter (with its resolved dxfId) must round-trip through the typed ColorFilter field on reload");
        reloadedTable.FilterColumns[0].ColorFilter!.DifferentialFormatId.Should().Be(dxfIndex);
        reloadedTable.FilterColumns[0].NativeFilterXmls.Should().BeEmpty(
            "colorFilter must no longer fall back to the generic NativeFilterXmls passthrough now that it has a typed field");

        command.Revert(ctx);
        sheet.StructuredTables[0].FilterColumns.Should().BeEmpty();
    }

    [Fact]
    public void CellFontColorFilterCommand_StructuredTableRange_PersistsAcrossSaveReload()
    {
        var (wb, sheet, ctx, range) = SetUpTableWithColoredCell();
        var blue = new CellColor(0, 0, 255);
        var blueCellStyle = CellStyle.Default.Clone();
        blueCellStyle.FontColor = blue;
        var blueStyle = wb.RegisterStyle(blueCellStyle);
        sheet.GetCell(2, 1)!.StyleId = blueStyle;

        var command = new CellFontColorFilterCommand(sheet.Id, range, filterColOffset: 0, blue);
        command.Apply(ctx).Success.Should().BeTrue();

        var (saved, loaded) = SaveAndReload(wb);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var tableXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/tables/table1.xml", "the table part must exist");
        var colorFilterXml = tableXml.Root!
            .Element(WorksheetNs + "autoFilter")!
            .Element(WorksheetNs + "filterColumn")!
            .Element(WorksheetNs + "colorFilter")!;
        colorFilterXml.Attribute("cellColor")!.Value.Should().Be("0", "font-colour filters must set cellColor=\"0\"");
        var dxfIdText = colorFilterXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty();

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "styles part must exist");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        var fontColorXml = dxfs[dxfIndex].Element(WorksheetNs + "font")!.Element(WorksheetNs + "color")!;
        fontColorXml.Attribute("rgb")!.Value.Should().Be("FF0000FF", "the dxf allocated for a real command-driven table font-colour filter must carry the exact picked colour");

        var reloadedTable = loaded.Sheets[0].StructuredTables.Single();
        reloadedTable.FilterColumns.Should().ContainSingle();
        reloadedTable.FilterColumns[0].ColorFilter.Should().NotBeNull(
            "a font-colour filter must round-trip through the typed ColorFilter field on reload");
        reloadedTable.FilterColumns[0].ColorFilter!.CellColor.Should().BeFalse("font-colour filters round-trip cellColor=\"0\" as CellColor=false");
        reloadedTable.FilterColumns[0].NativeFilterXmls.Should().BeEmpty(
            "colorFilter must no longer fall back to the generic NativeFilterXmls passthrough now that it has a typed field");

        command.Revert(ctx);
    }

    /// <summary>
    /// No-regression sibling: "No Fill" from a Table's own header dropdown must keep producing the
    /// empty-dxf "No Fill" form (mirrors R90_CellNoFillColorFilterCommand_StillAllocatesEmptyDxf_NoRegression
    /// for the worksheet-level path), unaffected by wiring the picked colour through the fill/font
    /// commands' new table-sync above.
    /// </summary>
    [Fact]
    public void CellNoFillColorFilterCommand_StructuredTableRange_StillAllocatesEmptyDxf_NoRegression()
    {
        var (wb, sheet, ctx, range) = SetUpTableWithColoredCell();

        var command = new CellNoFillColorFilterCommand(sheet.Id, range, filterColOffset: 0);
        command.Apply(ctx).Success.Should().BeTrue();

        var (saved, loaded) = SaveAndReload(wb);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var tableXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/tables/table1.xml", "the table part must exist");
        var colorFilterXml = tableXml.Root!
            .Element(WorksheetNs + "autoFilter")!
            .Element(WorksheetNs + "filterColumn")!
            .Element(WorksheetNs + "colorFilter")!;
        var dxfIdText = colorFilterXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty("dxfId is still required on colorFilter even for 'No Fill'");

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "styles part must exist");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        dxfs[dxfIndex].Elements().Should().BeEmpty("'No Fill' from the real command must still allocate an empty dxf, not a colour");

        var reloadedTable = loaded.Sheets[0].StructuredTables.Single();
        reloadedTable.FilterColumns.Should().ContainSingle();

        command.Revert(ctx);
    }
}

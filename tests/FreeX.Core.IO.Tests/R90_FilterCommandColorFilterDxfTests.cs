using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R90-meta-1: r89 added WorksheetAutoFilterColorFilterModel.Color (and the
/// XlsxDifferentialStyleAllocator/XlsxAutoFilterColorFilterDxfWriter machinery to persist it as a
/// real dxf), but CellFillColorFilterCommand and CellFontColorFilterCommand -- the actual product
/// entry points for Filter &gt; By Cell Color / By Font Color -- never passed their picked
/// _fillColor/_fontColor field into the model they build; they left Color at its default null.
/// That made the r89 fix inert for every real UI-driven colour filter: the dxf allocator saw a null
/// Color and wrote an empty &lt;dxf/&gt;, indistinguishable from "No Fill". These tests drive the
/// commands themselves (not a hand-built model) through a full save+reload to confirm the picked
/// colour now survives, plus a no-regression check that "No Fill" is untouched.
/// </summary>
public class R90_FilterCommandColorFilterDxfTests
{
    private static XNamespace WorksheetNs => "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
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
    public void R90_CellFillColorFilterCommand_RoundTripsPickedColorThroughRealCommand()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var red = new CellColor(255, 0, 0);
        var redCellStyle = CellStyle.Default.Clone();
        redCellStyle.FillColor = red;
        var redStyle = wb.RegisterStyle(redCellStyle);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Ready"));
        sheet.GetCell(2, 1)!.StyleId = redStyle;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        var ctx = new TestCommandContext(wb);

        // The real product entry point: Filter > By Cell Color, picking red.
        var command = new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, red);
        command.Apply(ctx).Success.Should().BeTrue();

        var (saved, loaded) = SaveAndReload(wb);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var colorFilterXml = worksheetXml.Root!
            .Element(WorksheetNs + "autoFilter")!
            .Element(WorksheetNs + "filterColumn")!
            .Element(WorksheetNs + "colorFilter")!;
        var dxfIdText = colorFilterXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty("the command-driven colour filter must allocate a real dxfId, not omit it like 'No Fill' does");

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        var fgColor = dxfs[dxfIndex].Element(WorksheetNs + "fill")!.Element(WorksheetNs + "patternFill")!.Element(WorksheetNs + "fgColor")!;
        fgColor.Attribute("rgb")!.Value.Should().Be("FFFF0000", "the dxf allocated for a real command-driven fill-colour filter must carry the exact picked colour, not an empty style");

        var loadedFilterColumn = loaded.GetSheetAt(0).AutoFilter!.FilterColumns.Should().ContainSingle().Subject;
        loadedFilterColumn.ColorFilter.Should().NotBeNull();
        loadedFilterColumn.ColorFilter!.CellColor.Should().BeTrue();
        loadedFilterColumn.ColorFilter.Color.Should().Be(red, "the colour picked via CellFillColorFilterCommand must survive save+reload, not just the internal model field used only to hide rows");
    }

    [Fact]
    public void R90_CellFontColorFilterCommand_RoundTripsPickedColorThroughRealCommand()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var blue = new CellColor(0, 0, 255);
        var blueCellStyle = CellStyle.Default.Clone();
        blueCellStyle.FontColor = blue;
        var blueStyle = wb.RegisterStyle(blueCellStyle);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Ready"));
        sheet.GetCell(2, 1)!.StyleId = blueStyle;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        var ctx = new TestCommandContext(wb);

        // The real product entry point: Filter > By Font Color, picking blue.
        var command = new CellFontColorFilterCommand(sheet.Id, range, filterColOffset: 0, blue);
        command.Apply(ctx).Success.Should().BeTrue();

        var (saved, loaded) = SaveAndReload(wb);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var colorFilterXml = worksheetXml.Root!
            .Element(WorksheetNs + "autoFilter")!
            .Element(WorksheetNs + "filterColumn")!
            .Element(WorksheetNs + "colorFilter")!;
        var dxfIdText = colorFilterXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty();

        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        var fontColorXml = dxfs[dxfIndex].Element(WorksheetNs + "font")!.Element(WorksheetNs + "color")!;
        fontColorXml.Attribute("rgb")!.Value.Should().Be("FF0000FF", "the dxf allocated for a real command-driven font-colour filter must carry the exact picked colour");

        var loadedFilterColumn = loaded.GetSheetAt(0).AutoFilter!.FilterColumns.Should().ContainSingle().Subject;
        loadedFilterColumn.ColorFilter!.CellColor.Should().BeFalse();
        loadedFilterColumn.ColorFilter.Color.Should().Be(blue, "the colour picked via CellFontColorFilterCommand must survive save+reload");
    }

    [Fact]
    public void R90_CellNoFillColorFilterCommand_StillAllocatesEmptyDxf_NoRegression()
    {
        // No-regression sibling: the "No Fill" command intentionally never has a colour to carry
        // (there is no fill to record), so it must keep producing the empty-dxf "No Fill" form,
        // unaffected by wiring the picked colour through the fill/font commands above.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Ready"));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        var ctx = new TestCommandContext(wb);

        var command = new CellNoFillColorFilterCommand(sheet.Id, range, filterColOffset: 0);
        command.Apply(ctx).Success.Should().BeTrue();

        var (saved, loaded) = SaveAndReload(wb);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var colorFilterXml = worksheetXml.Root!
            .Element(WorksheetNs + "autoFilter")!
            .Element(WorksheetNs + "filterColumn")!
            .Element(WorksheetNs + "colorFilter")!;
        var dxfIdText = colorFilterXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty("dxfId is still required on colorFilter even for 'No Fill'");

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        dxfs[dxfIndex].Elements().Should().BeEmpty("'No Fill' from the real command must still allocate an empty dxf, not a colour");

        var loadedFilterColumn = loaded.GetSheetAt(0).AutoFilter!.FilterColumns.Should().ContainSingle().Subject;
        loadedFilterColumn.ColorFilter!.Color.Should().BeNull("'No Fill' has no colour to resolve back");
    }
}

using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R170-freex-autofilter-sort-F2: SortCommand.BuildSortState (Sort On: Cell/Font Colour/Icon) built
/// a WorksheetSortConditionModel with Reference/Descending/SortBy/CustomList only -- DxfId/IconSet/
/// IconId were never set, so a saved &lt;sortCondition sortBy="cellColor|fontColor|icon"&gt; always
/// omitted them, leaving Excel's Data &gt; Sort dialog with no way to show which colour/icon a
/// reopened file was actually sorted on. Fixed by (1) carrying the resolved target colour on the
/// model (WorksheetSortConditionModel.TargetColor) so a new save-time allocator
/// (XlsxSortStateColorDxfWriter) can register it in the workbook's &lt;dxfs&gt; table and stamp the
/// index onto DxfId -- mirroring XlsxAutoFilterColorFilterDxfWriter (R89), which solved the
/// identical gap for AutoFilter's "Filter by Colour" -- and (2) setting IconSet/IconId directly for
/// an icon sort, which needs no dxf allocation at all.
///
/// These tests exercise the real production path end-to-end (SortCommand.Apply -&gt; full
/// XlsxFileAdapter.Save -&gt; raw saved XML), and where a dxf is involved, resolve the saved dxfId
/// back against the saved &lt;dxfs&gt; table to confirm it actually describes the chosen colour --
/// not merely that some attribute string was written.
/// </summary>
public sealed class R170_SortStateColorIconDxfTests
{
    private static XNamespace WorksheetNs => "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static (Workbook Workbook, Sheet Sheet) BuildSortableWorkbook()
    {
        var workbook = new Workbook("SortStateDxfTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(1));
        return (workbook, sheet);
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
    public void CellColorSort_SavedSortConditionDxfId_ResolvesToTheActualChosenColor()
    {
        var (workbook, sheet) = BuildSortableWorkbook();
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));
        var chosenColor = new CellColor(0, 200, 0);

        var command = new SortCommand(
            sheet.Id,
            range,
            [new SortKey(0, Ascending: true, SortOn.CellColor, TargetColor: chosenColor)]);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.SortState.Should().NotBeNull();
        sheet.SortState!.Conditions.Should().ContainSingle();
        sheet.SortState.Conditions[0].SortBy.Should().Be("cellColor");

        var (saved, _) = SaveAndReload(workbook);
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var conditionXml = worksheetXml.Root!
            .Element(WorksheetNs + "sortState")!
            .Element(WorksheetNs + "sortCondition")!;

        var dxfIdText = conditionXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty(
            "a saved Sort On: Cell Colour level must record which dxf/colour it was sorted on, not omit dxfId");

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        dxfIndex.Should().BeInRange(0, dxfs.Length - 1);
        var fgColor = dxfs[dxfIndex]
            .Element(WorksheetNs + "fill")!
            .Element(WorksheetNs + "patternFill")!
            .Element(WorksheetNs + "fgColor")!;
        fgColor.Attribute("rgb")!.Value.Should().Be("FF00C800",
            "the allocated dxf must describe the exact colour the sort was actually performed on");
    }

    [Fact]
    public void FontColorSort_SavedSortConditionDxfId_ResolvesToTheActualChosenFontColor()
    {
        var (workbook, sheet) = BuildSortableWorkbook();
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));
        var chosenColor = new CellColor(10, 20, 200);

        var command = new SortCommand(
            sheet.Id,
            range,
            [new SortKey(0, Ascending: true, SortOn.FontColor, TargetColor: chosenColor)]);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var (saved, _) = SaveAndReload(workbook);
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var conditionXml = worksheetXml.Root!
            .Element(WorksheetNs + "sortState")!
            .Element(WorksheetNs + "sortCondition")!;
        conditionXml.Attribute("sortBy")!.Value.Should().Be("fontColor");

        var dxfIdText = conditionXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty("a saved Sort On: Font Colour level must record dxfId too");

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        var fontColor = dxfs[dxfIndex].Element(WorksheetNs + "font")!.Element(WorksheetNs + "color")!;
        fontColor.Attribute("rgb")!.Value.Should().Be("FF0A14C8");
    }

    [Fact]
    public void IconSort_SavedSortConditionCarriesIconSetAndIconId_WithNoDxfNeeded()
    {
        var (workbook, sheet) = BuildSortableWorkbook();
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));
        var chosenIcon = new CfIconOverride("3Arrows", 2);

        var command = new SortCommand(
            sheet.Id,
            range,
            [new SortKey(0, Ascending: true, SortOn.CellIcon, TargetIcon: chosenIcon)]);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var (saved, _) = SaveAndReload(workbook);
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var conditionXml = worksheetXml.Root!
            .Element(WorksheetNs + "sortState")!
            .Element(WorksheetNs + "sortCondition")!;

        conditionXml.Attribute("sortBy")!.Value.Should().Be("icon");
        conditionXml.Attribute("iconSet").Should().NotBeNull(
            "a saved Sort On: Cell Icon level must record which icon set was actually sorted on");
        conditionXml.Attribute("iconSet")!.Value.Should().Be("3Arrows");
        conditionXml.Attribute("iconId").Should().NotBeNull(
            "a saved Sort On: Cell Icon level must record which icon within the set was actually sorted on");
        conditionXml.Attribute("iconId")!.Value.Should().Be("2");
    }

    [Fact]
    public void CellColorSort_WithNoTargetColorChosen_StillOmitsDxfId()
    {
        // Sibling/no-regression: the "no target colour chosen for this level" case (Excel's
        // null-vs-non-null grouping behaviour, unaffected by this fix) must still produce no
        // dxfId at all -- there is no colour to allocate a dxf for.
        var (workbook, sheet) = BuildSortableWorkbook();
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));

        var command = new SortCommand(
            sheet.Id,
            range,
            [new SortKey(0, Ascending: false, SortOn.CellColor)]);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.SortState!.Conditions[0].TargetColor.Should().BeNull();

        var (saved, _) = SaveAndReload(workbook);
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var conditionXml = worksheetXml.Root!
            .Element(WorksheetNs + "sortState")!
            .Element(WorksheetNs + "sortCondition")!;

        conditionXml.Attribute("dxfId").Should().BeNull("no target colour was chosen, so there is nothing to allocate a dxf for");
    }
}

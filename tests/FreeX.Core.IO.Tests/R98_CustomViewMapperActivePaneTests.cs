using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 98 finding "Custom View writer never tags pane/selection with
/// the active pane, unlike the primary sheetView writer" in XlsxCustomViewMapper.ToCustomSheetViewXml.
///
/// Before the fix, a Custom View captured while frozen/split panes were active and the cursor sat
/// outside the topLeft quadrant (e.g. header row frozen, user scrolled down and selected a cell
/// below the freeze line) wrote a &lt;pane&gt; with no @activePane and a &lt;selection&gt; with no
/// @pane -- both implicitly defaulting to "topLeft" per ECMA-376 -- recording the out-of-quadrant
/// cell as though it belonged to the topLeft pane. Real Excel always tags &lt;pane&gt;/@activePane
/// and the matching &lt;selection&gt;/@pane with the quadrant that actually holds the cursor,
/// exactly as XlsxWorksheetViewWriter.UpdateSheetView already does for the primary sheetView (see
/// XlsxWorksheetViewWriter.ComputeActivePaneName, now shared via its primitive-parameter overload).
///
/// Exercises the real product entry point (XlsxFileAdapter.Save), not the private
/// ToCustomSheetViewXml method directly, matching R19_CustomViewMapperTests' established pattern.
/// </summary>
public sealed class R98_CustomViewMapperActivePaneTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static Workbook CreateWorkbookWithCustomView(WorksheetCustomViewState state)
    {
        var workbook = new Workbook("CustomViewActivePaneFixture");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("header"));
        sheet.SetCell(new CellAddress(sheet.Id, 60, 30), new NumberValue(1));

        workbook.CustomViews.Add(new WorkbookCustomView(
            "Review",
            [state],
            Id: "{55555555-5555-5555-5555-555555555555}",
            ActiveSheetIndex: 0));
        return workbook;
    }

    private static XElement SaveAndReadCustomSheetView(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var entryStream = entry.Open();
        var worksheetXml = XDocument.Load(entryStream);
        return worksheetXml.Root!
            .Element(WorksheetNs + "customSheetViews")!
            .Element(WorksheetNs + "customSheetView")!;
    }

    [Fact]
    public void ToCustomSheetViewXml_FrozenPaneWithActiveCellBelowFreezeLine_TagsActivePaneAndSelectionPane()
    {
        // Header row (row 1) and header column (col 1) frozen; cursor at Z50 (row 50, col 26) is
        // in the bottomRight quadrant, not topLeft.
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            FrozenRows: 1,
            FrozenCols: 1,
            SplitRow: null,
            SplitColumn: null,
            ActiveRow: 50,
            ActiveCol: 26);
        var workbook = CreateWorkbookWithCustomView(state);

        var customSheetView = SaveAndReadCustomSheetView(workbook);
        var pane = customSheetView.Element(WorksheetNs + "pane")!;
        var selection = customSheetView.Element(WorksheetNs + "selection")!;

        pane.Attribute("activePane").Should().NotBeNull();
        pane.Attribute("activePane")!.Value.Should().Be("bottomRight");
        selection.Attribute("pane").Should().NotBeNull();
        selection.Attribute("pane")!.Value.Should().Be("bottomRight");
        selection.Attribute("activeCell")!.Value.Should().Be("Z50");
    }

    [Fact]
    public void CustomView_FrozenPaneWithActiveCellBelowFreezeLine_RoundTripsActiveCellThroughLoad()
    {
        // End-to-end: save through the real adapter, reload through the real adapter, and confirm
        // the active cell survives the round trip via the read-side pane/@activePane resolution
        // (R27) reading what this writer now emits.
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            FrozenRows: 1,
            FrozenCols: 1,
            SplitRow: null,
            SplitColumn: null,
            ActiveRow: 50,
            ActiveCol: 26);
        var workbook = CreateWorkbookWithCustomView(state);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        var reloadedState = reloaded.CustomViews.Should().ContainSingle().Subject
            .Sheets.Should().ContainSingle().Subject;
        reloadedState.ActiveRow.Should().Be(50);
        reloadedState.ActiveCol.Should().Be(26);
    }

    [Fact]
    public void ToCustomSheetViewXml_FrozenPaneWithActiveCellInTopLeft_OmitsActivePaneAndSelectionPane()
    {
        // No-regression sibling: when the cursor genuinely IS in the topLeft pane, the omitted
        // @activePane/@pane (implicit "topLeft" default per ECMA-376) must be preserved verbatim --
        // Excel itself does not write a redundant explicit "topLeft" in this case.
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            FrozenRows: 1,
            FrozenCols: 1,
            SplitRow: null,
            SplitColumn: null,
            ActiveRow: 1,
            ActiveCol: 1);
        var workbook = CreateWorkbookWithCustomView(state);

        var customSheetView = SaveAndReadCustomSheetView(workbook);
        var pane = customSheetView.Element(WorksheetNs + "pane")!;
        var selection = customSheetView.Element(WorksheetNs + "selection")!;

        pane.Attribute("activePane").Should().BeNull();
        selection.Attribute("pane").Should().BeNull();
        selection.Attribute("activeCell")!.Value.Should().Be("A1");
    }

    [Fact]
    public void ToCustomSheetViewXml_NoPanesWithActiveCell_OmitsSelectionPaneAttribute()
    {
        // No-regression sibling: no freeze/split at all -- the pane element itself is absent, and
        // the lone selection must not carry a @pane attribute (nothing to disambiguate).
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            FrozenRows: 0,
            FrozenCols: 0,
            SplitRow: null,
            SplitColumn: null,
            ActiveRow: 50,
            ActiveCol: 26);
        var workbook = CreateWorkbookWithCustomView(state);

        var customSheetView = SaveAndReadCustomSheetView(workbook);

        customSheetView.Element(WorksheetNs + "pane").Should().BeNull();
        var selection = customSheetView.Element(WorksheetNs + "selection")!;
        selection.Attribute("pane").Should().BeNull();
        selection.Attribute("activeCell")!.Value.Should().Be("Z50");
    }

    [Fact]
    public void ToCustomSheetViewXml_SplitPaneWithActiveCellInBottomLeft_TagsActivePaneAndSelectionPane()
    {
        // Sibling path: genuine (non-frozen) split, cursor below the split row but left of the
        // split column -> bottomLeft, not topLeft.
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            FrozenRows: 0,
            FrozenCols: 0,
            SplitRow: 5,
            SplitColumn: 3,
            ActiveRow: 20,
            ActiveCol: 1);
        var workbook = CreateWorkbookWithCustomView(state);

        var customSheetView = SaveAndReadCustomSheetView(workbook);
        var pane = customSheetView.Element(WorksheetNs + "pane")!;
        var selection = customSheetView.Element(WorksheetNs + "selection")!;

        pane.Attribute("activePane").Should().NotBeNull();
        pane.Attribute("activePane")!.Value.Should().Be("bottomLeft");
        selection.Attribute("pane").Should().NotBeNull();
        selection.Attribute("pane")!.Value.Should().Be("bottomLeft");
    }
}

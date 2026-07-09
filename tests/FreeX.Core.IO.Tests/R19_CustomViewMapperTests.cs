using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 19 findings in XlsxCustomViewMapper.cs:
///
/// R19-custom-view-sheetview-1: a custom view's split-pane (state="split", not frozen) xSplit/
/// ySplit must be written and read as the literal row/column index, exactly like the frozen case.
/// WorksheetCustomViewState.SplitRow/SplitColumn already model the index (mirroring
/// Sheet.SplitRow/SplitColumn -- see Sheet.cs -- and NativeJsonAdapter's identical treatment), and
/// FileAdapterSmokeTests.XlsxAdapter_FreshSave_WritesModeledCustomViewsToWorkbookAndWorksheets
/// pins this literal-index contract for a genuine (non-frozen) split pane. An earlier attempt at
/// this finding converted split panes to twentieths-of-a-point pane-bar twips (approximated from
/// Excel's default row/column pixel sizes, since this mapper has no access to the live sheet's
/// actual per-row/per-column overrides the way the primary sheetView writer does) -- that
/// approximation was reverted here because it is lossy/inexact and breaks the established
/// index-based contract other code (and tests) already depend on.
///
/// R19-custom-view-sheetview-2: customSheetView/@fitToPage must round-trip
/// WorksheetCustomViewState.FitToPage (a real CT_CustomSheetView attribute, distinct from the
/// worksheet-level sheetPr/pageSetUpPr/@fitToPage flag).
///
/// R19-custom-view-sheetview-3: customSheetView/@showAutoFilter must be written whenever the view
/// carries an AutoFilter (and therefore emits an &lt;autoFilter&gt; child), or Excel hides the
/// filter dropdown controls for that view even though the underlying autoFilter element is present.
/// </summary>
public sealed class R19_custom_view_Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static Workbook CreateWorkbookWithCustomView(WorksheetCustomViewState state)
    {
        var workbook = new Workbook("CustomViewMapperFixture");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("header"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));

        workbook.CustomViews.Add(new WorkbookCustomView(
            "Review",
            [state],
            Id: "{11111111-1111-1111-1111-111111111111}",
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
    public void ToCustomSheetViewXml_SplitPane_WritesRawRowColumnIndex()
    {
        // SplitRow=5/SplitColumn=3 with no frozen panes forces pane state="split". SplitRow/
        // SplitColumn already model the literal row/column index (mirroring Sheet.SplitRow/
        // SplitColumn), so the written xSplit/ySplit must be that same index verbatim -- matching
        // FileAdapterSmokeTests' equivalent genuine-split-pane contract.
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            FrozenRows: 0,
            FrozenCols: 0,
            SplitRow: 5,
            SplitColumn: 3);
        var workbook = CreateWorkbookWithCustomView(state);

        var customSheetView = SaveAndReadCustomSheetView(workbook);
        var pane = customSheetView.Element(WorksheetNs + "pane")!;

        pane.Attribute("state")!.Value.Should().Be("split");
        pane.Attribute("ySplit")!.Value.Should().Be("5");
        pane.Attribute("xSplit")!.Value.Should().Be("3");
    }

    [Fact]
    public void ReadWorksheetViews_SplitPaneRawIndex_RoundTrips()
    {
        // Simulates a pane state="split" element carrying the literal row/column index (as this
        // mapper writes it -- see ToCustomSheetViewXml_SplitPane_WritesRawRowColumnIndex above);
        // ReadWorksheetViews must read it back as that same index without any unit conversion.
        var worksheetXml = new XDocument(
            new XElement(
                WorksheetNs + "worksheet",
                new XElement(
                    WorksheetNs + "customSheetViews",
                    new XElement(
                        WorksheetNs + "customSheetView",
                        new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                        new XElement(
                            WorksheetNs + "pane",
                            new XAttribute("ySplit", "5"),
                            new XAttribute("xSplit", "3"),
                            new XAttribute("state", "split"))))));

        var views = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, WorksheetNs);

        var state = views.Should().ContainSingle().Subject.State;
        state.FrozenRows.Should().Be(0);
        state.FrozenCols.Should().Be(0);
        state.SplitRow.Should().Be(5);
        state.SplitColumn.Should().Be(3);
    }

    [Fact]
    public void CustomView_FitToPage_RoundTripsThroughSaveAndLoad()
    {
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            FrozenRows: 0,
            FrozenCols: 0,
            SplitRow: null,
            SplitColumn: null,
            FitToPage: true);
        var workbook = CreateWorkbookWithCustomView(state);

        var customSheetView = SaveAndReadCustomSheetView(workbook);
        customSheetView.Attribute("fitToPage").Should().NotBeNull();
        customSheetView.Attribute("fitToPage")!.Value.Should().Be("1");

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        var reloadedState = reloaded.CustomViews.Should().ContainSingle().Subject
            .Sheets.Should().ContainSingle().Subject;
        reloadedState.FitToPage.Should().BeTrue();
    }

    [Fact]
    public void CustomView_WithAutoFilter_WritesShowAutoFilterAttribute()
    {
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            FrozenRows: 0,
            FrozenCols: 0,
            SplitRow: null,
            SplitColumn: null,
            AutoFilter: new WorksheetAutoFilterModel("A1:B5", null));
        var workbook = CreateWorkbookWithCustomView(state);

        var customSheetView = SaveAndReadCustomSheetView(workbook);

        customSheetView.Element(WorksheetNs + "autoFilter").Should().NotBeNull();
        customSheetView.Attribute("showAutoFilter").Should().NotBeNull();
        customSheetView.Attribute("showAutoFilter")!.Value.Should().Be("1");
    }
}

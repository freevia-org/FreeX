using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-30 findings in the state="split" pane-rebuild logic of
/// XlsxWorksheetViewWriter:
///  - R30-sheet-view-freeze-window-deep-1: SplitRowToTwips/SplitColumnToTwips must skip hidden
///    rows/columns above the split when summing cumulative pixel offsets, since a hidden row/col
///    contributes 0px on screen.
///  - R30-sheet-view-freeze-window-deep-2: rebuilding the &lt;pane&gt; element must carry forward an
///    existing activePane attribute rather than silently resetting it to the OOXML default
///    "topLeft".
/// </summary>
public sealed class XlsxWorksheetViewWriterSplitPaneTests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XDocument CreateWorksheetXmlWithPane(XElement? pane)
    {
        XNamespace ns = WorksheetNs;
        var sheetView = new XElement(ns + "sheetView", new XAttribute("workbookViewId", "0"));
        if (pane is not null)
            sheetView.Add(pane);

        var root = new XElement(
            ns + "worksheet",
            new XElement(ns + "sheetViews", sheetView));

        return new XDocument(root);
    }

    private static XElement? GetPane(XDocument document)
    {
        XNamespace ns = WorksheetNs;
        return document.Root!
            .Element(ns + "sheetViews")!
            .Elements(ns + "sheetView")
            .Single()
            .Element(ns + "pane");
    }

    private static List<XElement> GetSelections(XDocument document)
    {
        XNamespace ns = WorksheetNs;
        return document.Root!
            .Element(ns + "sheetViews")!
            .Elements(ns + "sheetView")
            .Single()
            .Elements(ns + "selection")
            .ToList();
    }

    private static Sheet CreateSheetWithSplit(uint splitRow)
    {
        var sheet = new Sheet(SheetId.New(), "S")
        {
            DefaultRowHeight = 20.0,
            SplitRow = splitRow,
            FrozenRows = 0,
            FrozenCols = 0
        };
        sheet.RowHeights.Clear();
        return sheet;
    }

    [Fact]
    public void SplitRowToTwips_SkipsHiddenRowsAboveSplit()
    {
        // Rows 1-2 visible (20px each), row 3 hidden (must contribute 0px), split at row 4.
        // Visible pixel offset is 2 * 20 = 40px -> 40 * 15 = 600 twips.
        var sheet = CreateSheetWithSplit(splitRow: 4u);
        sheet.HiddenRows.Add(3u);

        var document = CreateWorksheetXmlWithPane(pane: null);
        var changed = XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        changed.Should().BeTrue();
        var pane = GetPane(document);
        pane.Should().NotBeNull();
        pane!.Attribute("ySplit")!.Value.Should().Be("600",
            "the hidden row above the split must not contribute to the cumulative pixel offset");
    }

    [Fact]
    public void SplitRowToTwips_SiblingCase_NoHiddenRows_SumsAllRowsAboveSplit()
    {
        // Sibling already-working case: no hidden rows, so all 3 rows above the split count.
        // 3 * 20 = 60px -> 60 * 15 = 900 twips.
        var sheet = CreateSheetWithSplit(splitRow: 4u);

        var document = CreateWorksheetXmlWithPane(pane: null);
        XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        var pane = GetPane(document);
        pane!.Attribute("ySplit")!.Value.Should().Be("900");
    }

    [Fact]
    public void SplitColumnToTwips_SkipsHiddenColumnsAboveSplit()
    {
        // Default column width 8.43 chars -> CharacterWidthToPixels(8.43) = round(8.43*7+5) = 64px.
        // Columns 1-2 visible, column 3 hidden, split at column 4.
        // Visible pixel offset is 2 * 64 = 128px -> 128 * 15 = 1920 twips.
        var sheet = new Sheet(SheetId.New(), "S")
        {
            DefaultColumnWidth = 8.43,
            SplitColumn = 4u,
            FrozenRows = 0,
            FrozenCols = 0
        };
        sheet.ColumnWidths.Clear();
        sheet.HiddenCols.Add(3u);

        var document = CreateWorksheetXmlWithPane(pane: null);
        XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        var pane = GetPane(document);
        pane!.Attribute("xSplit")!.Value.Should().Be("1920",
            "the hidden column above the split must not contribute to the cumulative pixel offset");
    }

    [Fact]
    public void UpdateSheetView_RebuildingSplitPane_PreservesExistingActivePane()
    {
        // A loaded Excel-authored split pane with the user's cursor in a non-default quadrant.
        XNamespace ns = WorksheetNs;
        var existingPane = new XElement(
            ns + "pane",
            new XAttribute("xSplit", "1"),
            new XAttribute("ySplit", "1"),
            new XAttribute("state", "split"),
            new XAttribute("activePane", "bottomRight"));

        var document = CreateWorksheetXmlWithPane(existingPane);

        // Changing the split geometry (so the rebuild path actually fires) must still preserve
        // activePane -- only the geometry changes, not which pane the cursor is in.
        var sheet = CreateSheetWithSplit(splitRow: 4u);

        var changed = XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        changed.Should().BeTrue();
        var pane = GetPane(document);
        pane!.Attribute("activePane")?.Value.Should().Be("bottomRight",
            "rebuilding the pane element must not silently reset the user's active split pane to topLeft");
    }

    [Fact]
    public void UpdateSheetView_SiblingCase_NoExistingActivePane_OmitsAttribute()
    {
        // Sibling already-working case: when there was no activePane to begin with (the OOXML
        // default, topLeft), the rebuilt pane must not gain a spurious activePane attribute.
        var document = CreateWorksheetXmlWithPane(pane: null);
        var sheet = CreateSheetWithSplit(splitRow: 4u);

        XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        var pane = GetPane(document);
        pane!.Attribute("activePane").Should().BeNull();
    }

    /// <summary>
    /// R68-io-workbook-view-6-2: the FIRST save that introduces a brand-new window split (no
    /// freeze, no pre-existing &lt;pane&gt;) must tag the active cell's pane/selection with the
    /// pane it is geometrically in, computed from the split geometry being introduced this same
    /// save, instead of hardcoding "topLeft".
    /// </summary>
    [Fact]
    public void UpdateSheetView_FirstSplitSave_ComputesActivePaneFromGeometry()
    {
        var sheet = new Sheet(SheetId.New(), "S")
        {
            DefaultRowHeight = 20.0,
            DefaultColumnWidth = 8.43,
            SplitRow = 10u,
            SplitColumn = 3u,
            FrozenRows = 0,
            FrozenCols = 0,
            ActiveRow = 30u,
            ActiveCol = 6u // F
        };
        sheet.RowHeights.Clear();
        sheet.ColumnWidths.Clear();

        var document = CreateWorksheetXmlWithPane(pane: null);
        var changed = XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        changed.Should().BeTrue();
        var pane = GetPane(document);
        pane.Should().NotBeNull();
        pane!.Attribute("activePane")?.Value.Should().Be("bottomRight",
            "the active cell F30 is below and right of the split boundary introduced by this save");

        var selections = GetSelections(document);
        var bottomRightSelection = selections.SingleOrDefault(s => s.Attribute("pane")?.Value == "bottomRight");
        bottomRightSelection.Should().NotBeNull("the active cell's selection must be keyed to the pane it actually falls in");
        bottomRightSelection!.Attribute("activeCell")!.Value.Should().Be("F30");
    }

    /// <summary>
    /// No-regression sibling for the fix above: once a &lt;pane&gt; already exists (a subsequent
    /// save, not the one introducing the split), the pre-existing "paneElement is not null" branch
    /// -- unchanged by this fix -- still computes the active pane correctly.
    /// </summary>
    [Fact]
    public void UpdateSheetView_SubsequentSplitSave_PaneAlreadyExists_ActivePaneStillComputed()
    {
        XNamespace ns = WorksheetNs;
        var existingPane = new XElement(
            ns + "pane",
            new XAttribute("xSplit", "1920"),
            new XAttribute("ySplit", "600"),
            new XAttribute("state", "split"));

        var document = CreateWorksheetXmlWithPane(existingPane);
        var sheet = new Sheet(SheetId.New(), "S")
        {
            DefaultRowHeight = 20.0,
            DefaultColumnWidth = 8.43,
            SplitRow = 10u,
            SplitColumn = 3u,
            FrozenRows = 0,
            FrozenCols = 0,
            ActiveRow = 30u,
            ActiveCol = 6u
        };
        sheet.RowHeights.Clear();
        sheet.ColumnWidths.Clear();

        XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        var pane = GetPane(document);
        pane!.Attribute("activePane")!.Value.Should().Be("bottomRight");
    }

    /// <summary>
    /// No-regression sibling: a FROZEN sheet (not split) with no pre-existing &lt;pane&gt; must
    /// still fall back to the OOXML default "topLeft" for the active-cell selection -- freeze
    /// panes are created by a different code path, so the new "introducing a split" branch must
    /// not fire for it.
    /// </summary>
    [Fact]
    public void UpdateSheetView_FreezeCase_NoExistingPane_StillDefaultsToTopLeft()
    {
        var sheet = new Sheet(SheetId.New(), "S")
        {
            FrozenRows = 10u,
            FrozenCols = 3u,
            ActiveRow = 30u,
            ActiveCol = 6u
        };

        var document = CreateWorksheetXmlWithPane(pane: null);
        XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        GetPane(document).Should().BeNull("freeze panes are written by a different code path, not this method");
        var selections = GetSelections(document);
        selections.Should().ContainSingle();
        selections[0].Attribute("pane").Should().BeNull();
        selections[0].Attribute("activeCell")!.Value.Should().Be("F30");
    }
}

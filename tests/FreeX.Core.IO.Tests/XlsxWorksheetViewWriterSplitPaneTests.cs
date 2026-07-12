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
}

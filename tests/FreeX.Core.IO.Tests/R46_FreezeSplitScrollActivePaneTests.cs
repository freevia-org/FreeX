using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R46-render-freeze-split-scroll-2-2: activePane must be recomputed from
/// the active cell's position relative to the freeze/split boundary, not blindly preserved from
/// whatever the pane element already carries, or moving the cursor across the boundary writes a
/// self-contradictory pane/selection pair (a stale activePane paired with a selection whose
/// activeCell can only ever be rendered in a different pane).
///
/// (R46-render-freeze-split-scroll-2-1, the sibling finding about which XML element's topLeftCell
/// is authoritative, was triaged as a live conflict with the existing, deliberately-authored
/// XlsxAdapter_Load_FreezePaneTopLeftDoesNotBecomeWorksheetViewport regression test in
/// FileAdapterSmokeTests.cs -- see that finding's skip rationale -- so no fix/tests were added for
/// it here.)
/// </summary>
public sealed class R46_FreezeSplitScrollActivePaneTests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XDocument CreateWorksheetXmlWithPane(XElement pane, XElement? selection)
    {
        XNamespace ns = WorksheetNs;
        var sheetView = new XElement(ns + "sheetView", new XAttribute("workbookViewId", "0"), pane);
        if (selection is not null)
            sheetView.Add(selection);

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

    private static IReadOnlyList<XElement> GetSelections(XDocument document)
    {
        XNamespace ns = WorksheetNs;
        return document.Root!
            .Element(ns + "sheetViews")!
            .Elements(ns + "sheetView")
            .Single()
            .Elements(ns + "selection")
            .ToList();
    }

    [Fact]
    public void UpdateSheetView_ActiveCellMovesIntoFrozenRegion_RecomputesActivePaneToTopLeft()
    {
        // Freeze rows 1-3; the pane/selection were previously left in "bottomLeft" (cursor at A4,
        // just below the freeze). The user then clicks A2, which is INSIDE the frozen rows -- only
        // ever rendered in the "topLeft" pane.
        XNamespace ns = WorksheetNs;
        var pane = new XElement(
            ns + "pane",
            new XAttribute("ySplit", "3"),
            new XAttribute("topLeftCell", "A4"),
            new XAttribute("activePane", "bottomLeft"),
            new XAttribute("state", "frozen"));
        var selection = new XElement(
            ns + "selection",
            new XAttribute("pane", "bottomLeft"),
            new XAttribute("activeCell", "A4"),
            new XAttribute("sqref", "A4"));
        var document = CreateWorksheetXmlWithPane(pane, selection);

        var sheet = new Sheet(SheetId.New(), "S")
        {
            FrozenRows = 3u,
            FrozenCols = 0u,
            ActiveRow = 2u,
            ActiveCol = 1u
        };

        var changed = XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        changed.Should().BeTrue();
        var updatedPane = GetPane(document);
        updatedPane!.Attribute("activePane").Should().BeNull(
            "topLeft is the OOXML default and the cursor is now in the frozen (topLeft) rows");

        var selections = GetSelections(document);
        var topLeftSelection = selections.SingleOrDefault(s => s.Attribute("pane") is null);
        topLeftSelection.Should().NotBeNull("a selection for the topLeft pane must be created for the new active cell");
        topLeftSelection!.Attribute("activeCell")!.Value.Should().Be("A2");

        // The stale bottomLeft selection is left untouched (Excel remembers each pane's own last
        // cursor position independently).
        var bottomLeftSelection = selections.Single(s => string.Equals(s.Attribute("pane")?.Value, "bottomLeft", StringComparison.Ordinal));
        bottomLeftSelection.Attribute("activeCell")!.Value.Should().Be("A4");
    }

    [Fact]
    public void UpdateSheetView_ActiveCellStaysInScrollablePane_SiblingCase_KeepsActivePaneBottomLeft()
    {
        // Sibling no-regression case: the cursor moves WITHIN the already-active bottomLeft pane
        // (from A4 to A10), so activePane must remain "bottomLeft" -- the fix must not flip it to
        // topLeft just because a recompute now happens.
        XNamespace ns = WorksheetNs;
        var pane = new XElement(
            ns + "pane",
            new XAttribute("ySplit", "3"),
            new XAttribute("topLeftCell", "A4"),
            new XAttribute("activePane", "bottomLeft"),
            new XAttribute("state", "frozen"));
        var selection = new XElement(
            ns + "selection",
            new XAttribute("pane", "bottomLeft"),
            new XAttribute("activeCell", "A4"),
            new XAttribute("sqref", "A4"));
        var document = CreateWorksheetXmlWithPane(pane, selection);

        var sheet = new Sheet(SheetId.New(), "S")
        {
            FrozenRows = 3u,
            FrozenCols = 0u,
            ActiveRow = 10u,
            ActiveCol = 1u
        };

        XlsxWorksheetViewWriter.UpdateSheetView(document, sheet);

        var updatedPane = GetPane(document);
        updatedPane!.Attribute("activePane")!.Value.Should().Be("bottomLeft");

        var selections = GetSelections(document);
        var bottomLeftSelection = selections.Single(s => string.Equals(s.Attribute("pane")?.Value, "bottomLeft", StringComparison.Ordinal));
        bottomLeftSelection.Attribute("activeCell")!.Value.Should().Be("A10");
    }
}

using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 27 finding R27-freeze-split-view-deep-3 in
/// XlsxCustomViewMapper.ReadWorksheetViews: a customSheetView's pane/@activePane must be resolved
/// to pick the correct &lt;selection&gt; when the frozen/split pane's cursor isn't in the first
/// &lt;selection&gt; element written by Excel. Mirrors the fix already applied to
/// XlsxFileAdapter.SheetXmlLayout.ReadActiveSelectionCell for the primary sheetView.
/// </summary>
public sealed class R27_CustomViewMapperActivePaneTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void ReadWorksheetViews_FrozenPaneWithNonFirstActivePane_ResolvesActiveCellFromActivePane()
    {
        // pane state="frozen" activePane="bottomRight" with 4 selections in document order
        // (topLeft->A1, topRight->C1, bottomLeft->A2, bottomRight->Z50). The true cursor is in
        // bottomRight (Z50), not the first-written topLeft selection (A1).
        var worksheetXml = new XDocument(
            new XElement(
                WorksheetNs + "worksheet",
                new XElement(
                    WorksheetNs + "customSheetViews",
                    new XElement(
                        WorksheetNs + "customSheetView",
                        new XAttribute("guid", "{33333333-3333-3333-3333-333333333333}"),
                        new XElement(
                            WorksheetNs + "pane",
                            new XAttribute("xSplit", "2"),
                            new XAttribute("ySplit", "1"),
                            new XAttribute("activePane", "bottomRight"),
                            new XAttribute("state", "frozen")),
                        new XElement(
                            WorksheetNs + "selection",
                            new XAttribute("pane", "topLeft"),
                            new XAttribute("activeCell", "A1"),
                            new XAttribute("sqref", "A1")),
                        new XElement(
                            WorksheetNs + "selection",
                            new XAttribute("pane", "topRight"),
                            new XAttribute("activeCell", "C1"),
                            new XAttribute("sqref", "C1")),
                        new XElement(
                            WorksheetNs + "selection",
                            new XAttribute("pane", "bottomLeft"),
                            new XAttribute("activeCell", "A2"),
                            new XAttribute("sqref", "A2")),
                        new XElement(
                            WorksheetNs + "selection",
                            new XAttribute("pane", "bottomRight"),
                            new XAttribute("activeCell", "Z50"),
                            new XAttribute("sqref", "Z50"))))));

        var views = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, WorksheetNs);

        var state = views.Should().ContainSingle().Subject.State;
        state.ActiveRow.Should().Be(50);
        state.ActiveCol.Should().Be(26);
    }

    [Fact]
    public void ReadWorksheetViews_UnfrozenSingleSelection_StillResolvesActiveCell()
    {
        // Sibling already-working case: no pane element at all (no freeze/split), a single
        // <selection> with no @pane attribute -- implicitly "topLeft" -- must still resolve.
        var worksheetXml = new XDocument(
            new XElement(
                WorksheetNs + "worksheet",
                new XElement(
                    WorksheetNs + "customSheetViews",
                    new XElement(
                        WorksheetNs + "customSheetView",
                        new XAttribute("guid", "{44444444-4444-4444-4444-444444444444}"),
                        new XElement(
                            WorksheetNs + "selection",
                            new XAttribute("activeCell", "D7"),
                            new XAttribute("sqref", "D7"))))));

        var views = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, WorksheetNs);

        var state = views.Should().ContainSingle().Subject.State;
        state.ActiveRow.Should().Be(7);
        state.ActiveCol.Should().Be(4);
    }
}

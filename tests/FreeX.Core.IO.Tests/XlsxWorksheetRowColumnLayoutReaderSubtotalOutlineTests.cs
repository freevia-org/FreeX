using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R24-subtotal-outline-2: a real Excel-authored nested Data &gt; Subtotal
/// layout marks the still-visible anchor row of a collapsed inner outline group with
/// <c>collapsed="1"</c> (and a non-zero <c>outlineLevel</c>) while leaving that row's own
/// <c>hidden</c> attribute absent/false; only the inner group's own detail rows carry
/// <c>hidden="1"</c>. The reader must not derive group-hidden state purely from <c>collapsed</c>.
/// </summary>
public sealed class XlsxWorksheetRowColumnLayoutReaderSubtotalOutlineTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Region > Product nested subtotal, collapsed down to the Product level:
    //  - Row 2 is a hidden line-item detail row nested two levels deep (outlineLevel="3"); it
    //    carries no "collapsed" attribute of its own (only anchor rows do).
    //  - Row 3 is the Product-subtotal row that anchors that now-collapsed inner group -- it
    //    stays visible in real Excel (own "+" toggle) but still carries outlineLevel="2" (nested
    //    under the Region level) and collapsed="1", with no "hidden" attribute.
    //  - Row 4 is the outer Region-subtotal anchor row: here the OUTER group has *also* been
    //    collapsed, so this row is both genuinely hidden ("hidden=1") and itself the anchor of a
    //    (now-hidden) inner group ("collapsed=1", outlineLevel="1"). This is the one case where
    //    collapsed+hidden legitimately co-locate on the same row, and it must still land in
    //    GroupHiddenRows.
    private static XDocument BuildNestedSubtotalWorksheet() =>
        new(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "sheetData",
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "2"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "3")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "3"),
                        new XAttribute("outlineLevel", "2"),
                        new XAttribute("collapsed", "1")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "4"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1"),
                        new XAttribute("collapsed", "1")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "5"),
                        new XAttribute("collapsed", "1")))));

    [Fact]
    public void ReadSheetDataLayout_XElementPath_KeepsVisibleCollapsedSubtotalAnchorRowVisible()
    {
        var worksheet = BuildNestedSubtotalWorksheet();

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        // The visible Product-subtotal anchor row (collapsed, but not itself hidden) must not be
        // reported as hidden by either mechanism.
        layout.RowColumnLayout.RowOutlineLevels.Should().Contain(3u, 2);
        layout.RowColumnLayout.HiddenRows.Should().NotContain(3u);
        layout.RowColumnLayout.GroupHiddenRows.Should().NotContain(3u);

        // Hidden outlined detail is classified as group-owned, not manually hidden.
        layout.RowColumnLayout.HiddenRows.Should().NotContain(2u);
        layout.RowColumnLayout.GroupHiddenRows.Should().Contain(2u);

        // A row that is both hidden AND the anchor of its own collapsed inner group still
        // participates in GroupHiddenRows.
        layout.RowColumnLayout.HiddenRows.Should().NotContain(4u);
        layout.RowColumnLayout.GroupHiddenRows.Should().Contain(4u);
        layout.RowColumnLayout.CollapsedAnchorRows.Should().Contain([3u, 4u]);
    }

    [Fact]
    public void ReadSheetDataLayout_XmlReaderPath_KeepsVisibleCollapsedSubtotalAnchorRowVisible()
    {
        var worksheet = BuildNestedSubtotalWorksheet();
        using var reader = worksheet.Root!.Element(WorksheetNs + "sheetData")!.CreateReader();

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(reader, WorksheetNs);

        layout.RowColumnLayout.RowOutlineLevels.Should().Contain(3u, 2);
        layout.RowColumnLayout.HiddenRows.Should().NotContain(3u);
        layout.RowColumnLayout.GroupHiddenRows.Should().NotContain(3u);

        layout.RowColumnLayout.HiddenRows.Should().NotContain(2u);
        layout.RowColumnLayout.GroupHiddenRows.Should().Contain(2u);

        layout.RowColumnLayout.HiddenRows.Should().NotContain(4u);
        layout.RowColumnLayout.GroupHiddenRows.Should().Contain(4u);
        layout.RowColumnLayout.CollapsedAnchorRows.Should().Contain([3u, 4u]);
    }
}

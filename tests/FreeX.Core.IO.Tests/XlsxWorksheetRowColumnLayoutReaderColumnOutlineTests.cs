using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R34-io-row-col-outline-2-1: a real Excel-authored nested column
/// grouping marks the still-visible anchor column of a collapsed inner outline group with
/// <c>collapsed="1"</c> (and a non-zero <c>outlineLevel</c>) while leaving that column's own
/// <c>hidden</c> attribute absent/false; only the inner group's own detail columns carry
/// <c>hidden="1"</c>. The reader must not derive group-hidden state purely from <c>collapsed</c>,
/// mirroring the row-side fix in <see cref="XlsxWorksheetRowColumnLayoutReaderSubtotalOutlineTests"/>.
/// </summary>
public sealed class XlsxWorksheetRowColumnLayoutReaderColumnOutlineTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Outer level-1 group B:F, inner level-2 subgroup C:D collapsed only (outer group stays
    // expanded):
    //  - Column B (min=2,max=2) is a plain member of the outer group: outlineLevel="1", no
    //    hidden/collapsed of its own.
    //  - Columns C:D (min=3,max=4) are the hidden detail columns of the now-collapsed inner
    //    group: outlineLevel="2", hidden="1", no "collapsed" of their own (only anchor columns
    //    carry that).
    //  - Column E (min=5,max=5) is the visible "+" summary/anchor column for the just-collapsed
    //    inner group -- itself only a member of the still-expanded outer group, so its own
    //    "hidden" attribute is absent/false: outlineLevel="1", collapsed="1", no "hidden".
    private static XDocument BuildNestedColumnOutlineWorksheet() =>
        new(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "cols",
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "2"),
                        new XAttribute("max", "2"),
                        new XAttribute("outlineLevel", "1")),
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "3"),
                        new XAttribute("max", "4"),
                        new XAttribute("outlineLevel", "2"),
                        new XAttribute("hidden", "1")),
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "5"),
                        new XAttribute("max", "5"),
                        new XAttribute("outlineLevel", "1"),
                        new XAttribute("collapsed", "1"))),
                new XElement(WorksheetNs + "sheetData")));

    [Fact]
    public void ReadSheetDataLayout_KeepsVisibleCollapsedColumnAnchorVisible()
    {
        var worksheet = BuildNestedColumnOutlineWorksheet();

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        // Column E: the visible "+" anchor column (collapsed, but not itself hidden) must not be
        // reported as hidden by either mechanism.
        layout.RowColumnLayout.ColOutlineLevels.Should().Contain(5u, 1);
        layout.RowColumnLayout.HiddenCols.Should().NotContain(5u);
        layout.RowColumnLayout.GroupHiddenCols.Should().NotContain(5u);

        // Columns C and D: genuinely hidden detail columns with no "collapsed" of their own are
        // unaffected -- still hidden, but not via GroupHiddenCols.
        layout.RowColumnLayout.HiddenCols.Should().Contain(3u);
        layout.RowColumnLayout.HiddenCols.Should().Contain(4u);
        layout.RowColumnLayout.GroupHiddenCols.Should().NotContain(3u);
        layout.RowColumnLayout.GroupHiddenCols.Should().NotContain(4u);

        // Column B: a plain outer-group member (no hidden/collapsed) stays fully unaffected.
        layout.RowColumnLayout.ColOutlineLevels.Should().Contain(2u, 1);
        layout.RowColumnLayout.HiddenCols.Should().NotContain(2u);
        layout.RowColumnLayout.GroupHiddenCols.Should().NotContain(2u);
    }

    [Fact]
    public void ReadSheetDataLayout_HiddenAndCollapsedColumnAnchorStillReportsGroupHidden()
    {
        // Sibling already-working case: a column that is both genuinely hidden AND the anchor of
        // its own collapsed group (outer group also collapsed) must still land in GroupHiddenCols,
        // exactly like the row-side equivalent.
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "cols",
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "5"),
                        new XAttribute("max", "5"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1"),
                        new XAttribute("collapsed", "1"))),
                new XElement(WorksheetNs + "sheetData")));

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        layout.RowColumnLayout.HiddenCols.Should().Contain(5u);
        layout.RowColumnLayout.GroupHiddenCols.Should().Contain(5u);
    }
}

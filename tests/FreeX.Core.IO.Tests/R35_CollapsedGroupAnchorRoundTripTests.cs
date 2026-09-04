using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R35-deferred-collapse-anchor-1: on a full-rebuild XLSX save,
/// ClosedXML's <c>IXLRow.Collapse()</c>/<c>IXLColumn.Collapse()</c> set BOTH <c>hidden="1"</c> AND
/// <c>collapsed="1"</c> on the same row/column. XlsxFileAdapter.Save previously called
/// <c>.Collapse()</c> on every entry of <see cref="Sheet.GroupHiddenRows"/>/
/// <see cref="Sheet.GroupHiddenCols"/> (the HIDDEN DETAIL rows/columns a group summarizes), which
/// stamped a semantically wrong <c>collapsed="1"</c> onto those interior detail rows while the
/// group's actual visible outline-toggle anchor (a still-visible subtotal/summary row/column) never
/// received any marker at all -- <see cref="Sheet.GroupHiddenRows"/>/<see cref="Sheet.GroupHiddenCols"/>
/// had no way to represent "collapsed but not hidden".
///
/// The fix adds <see cref="Sheet.CollapsedAnchorRows"/>/<see cref="Sheet.CollapsedAnchorCols"/>,
/// changes the writer to <c>Hide()</c> (not <c>Collapse()</c>) detail rows/columns, and marks the
/// anchor via <c>Collapse()</c> followed by <c>Unhide()</c> (unless the same row/column is also a
/// genuinely hidden detail row/column of an outer group, in which case both flags legitimately
/// co-locate). <c>CollapseRowGroupCommand</c>/<c>CollapseColGroupCommand</c> (and their Expand
/// counterparts) populate/clear these new sets when a group is collapsed/expanded inside FreeX
/// itself. <c>XlsxWorksheetRowColumnLayoutReader</c> now also captures a real Excel-authored file's
/// visible collapsed anchor row/column (collapsed="1", hidden absent/false) into the reader-level
/// layout, independent of <c>outlineLevel &gt; 0</c> since the outermost group's anchor commonly
/// carries no outline level of its own.
/// </summary>
public sealed class R35_CollapsedGroupAnchorRoundTripTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XElement GetRow(XDocument worksheetXml, int rowNumber) =>
        worksheetXml.Root!
            .Element(WorksheetNs + "sheetData")!
            .Elements(WorksheetNs + "row")
            .Single(r => (string?)r.Attribute("r") == rowNumber.ToString());

    // ClosedXML merges adjacent <col> spans that share identical attributes (e.g. two consecutively
    // hidden+outlined columns become one min="1" max="2" element), so match by containment rather
    // than an exact min="n" attribute.
    private static XElement GetCol(XDocument worksheetXml, int colNumber) =>
        worksheetXml.Root!
            .Element(WorksheetNs + "cols")!
            .Elements(WorksheetNs + "col")
            .Single(c =>
                int.Parse((string)c.Attribute("min")!) <= colNumber &&
                colNumber <= int.Parse((string)c.Attribute("max")!));

    [Fact]
    public void Save_CollapsedRowGroup_WritesCollapsedOnAnchorOnly_NotOnHiddenDetailRows()
    {
        // Simulates the state CollapseRowGroupCommand leaves behind: rows 1-2 are the hidden detail
        // rows of an outline group, row 3 is the still-visible subtotal/summary anchor row Excel
        // uses to host the group's "+/-" toggle.
        var workbook = new Workbook("CollapsedRowAnchorTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("detail1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("detail2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("subtotal"));

        sheet.RowOutlineLevels[1] = 1;
        sheet.RowOutlineLevels[2] = 1;
        sheet.GroupHiddenRows.Add(1);
        sheet.GroupHiddenRows.Add(2);
        sheet.CollapsedAnchorRows.Add(3);

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        var row1 = GetRow(worksheetXml, 1);
        var row2 = GetRow(worksheetXml, 2);
        var row3 = GetRow(worksheetXml, 3);

        // Hidden detail rows: hidden="1", but no spurious collapsed="1".
        row1.Attribute("hidden")!.Value.Should().Be("1");
        row1.Attribute("collapsed").Should().BeNull();
        row2.Attribute("hidden")!.Value.Should().Be("1");
        row2.Attribute("collapsed").Should().BeNull();

        // Visible anchor row: collapsed="1", but NOT hidden.
        row3.Attribute("collapsed")!.Value.Should().Be("1");
        row3.Attribute("hidden").Should().BeNull();
    }

    [Fact]
    public void Save_CollapsedColumnGroup_WritesCollapsedOnAnchorOnly_NotOnHiddenDetailColumns()
    {
        var workbook = new Workbook("CollapsedColAnchorTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("detail1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("detail2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("subtotal"));

        sheet.ColOutlineLevels[1] = 1;
        sheet.ColOutlineLevels[2] = 1;
        sheet.GroupHiddenCols.Add(1);
        sheet.GroupHiddenCols.Add(2);
        sheet.CollapsedAnchorCols.Add(3);

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        var col1 = GetCol(worksheetXml, 1);
        var col2 = GetCol(worksheetXml, 2);
        var col3 = GetCol(worksheetXml, 3);

        col1.Attribute("hidden")!.Value.Should().Be("1");
        col1.Attribute("collapsed").Should().BeNull();
        col2.Attribute("hidden")!.Value.Should().Be("1");
        col2.Attribute("collapsed").Should().BeNull();

        col3.Attribute("collapsed")!.Value.Should().Be("1");
        col3.Attribute("hidden").Should().BeNull();
    }

    [Fact]
    public void Save_RowThatIsBothHiddenDetailAndItsOwnInnerAnchor_KeepsBothFlags()
    {
        // Mirrors XlsxWorksheetRowColumnLayoutReaderSubtotalOutlineTests' nested-subtotal scenario:
        // a row can legitimately be hidden as a nested detail row of an OUTER group while also
        // anchoring its own (now-hidden) INNER collapsed group -- Excel writes both hidden="1" and
        // collapsed="1" together on that one row.
        var workbook = new Workbook("NestedAnchorTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("outer subtotal, also hidden"));

        sheet.RowOutlineLevels[4] = 1;
        sheet.GroupHiddenRows.Add(4);
        sheet.CollapsedAnchorRows.Add(4);

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var row4 = GetRow(worksheetXml, 4);

        row4.Attribute("hidden")!.Value.Should().Be("1");
        row4.Attribute("collapsed")!.Value.Should().Be("1");
    }

    [Fact]
    public void Save_PlainHiddenGroupRows_WithNoTrackedAnchor_WritesOnlyHidden_NoRegression()
    {
        // Sibling no-regression case: a sheet with hidden group rows but no CollapsedAnchorRows
        // entries at all must still save with plain hidden="1" -- never collapsed="1" -- exactly the
        // universal bug this fix removes (Collapse() was previously called unconditionally on every
        // GroupHiddenRows entry).
        var workbook = new Workbook("PlainGroupHiddenTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("detail1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("detail2"));

        sheet.RowOutlineLevels[1] = 1;
        sheet.RowOutlineLevels[2] = 1;
        sheet.GroupHiddenRows.Add(1);
        sheet.GroupHiddenRows.Add(2);

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        var row1 = GetRow(worksheetXml, 1);
        var row2 = GetRow(worksheetXml, 2);

        row1.Attribute("hidden")!.Value.Should().Be("1");
        row1.Attribute("collapsed").Should().BeNull();
        row2.Attribute("hidden")!.Value.Should().Be("1");
        row2.Attribute("collapsed").Should().BeNull();
    }

    [Fact]
    public void ReadSheetDataLayout_VisibleCollapsedAnchorRow_WithNoOutlineLevelOfItsOwn_IsCapturedAsAnchor()
    {
        // A real Excel-authored outermost collapsed group's anchor row commonly carries no
        // outlineLevel of its own (only the detail rows it summarizes are nested), yet Excel still
        // writes collapsed="1" on it. The reader must capture this independent of outlineLevel>0.
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "sheetData",
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "1"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "2"),
                        new XAttribute("collapsed", "1")))));

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        // Row 1 is outline-owned hidden detail, not a manually hidden row or an anchor.
        layout.RowColumnLayout.HiddenRows.Should().NotContain(1u);
        layout.RowColumnLayout.GroupHiddenRows.Should().Contain(1u);

        // Row 2 is the visible anchor: collapsed="1", no "hidden" and no "outlineLevel" of its own.
        layout.RowColumnLayout.CollapsedAnchorRows.Should().NotBeNull();
        layout.RowColumnLayout.CollapsedAnchorRows!.Should().Contain(2u);
        layout.RowColumnLayout.CollapsedAnchorRows!.Should().NotContain(1u);
        layout.RowColumnLayout.HiddenRows.Should().NotContain(2u);
        layout.RowColumnLayout.GroupHiddenRows.Should().NotContain(2u);
    }

    [Fact]
    public void ReadSheetDataLayout_HiddenCollapsedInnerAnchor_IsBothGroupHiddenAndAnchor()
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "sheetData",
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

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        layout.RowColumnLayout.HiddenRows.Should().NotContain(4u);
        layout.RowColumnLayout.GroupHiddenRows.Should().Contain(4u);
        layout.RowColumnLayout.CollapsedAnchorRows.Should().Contain(4u);
    }

    [Fact]
    public void ReadSheetDataLayout_ManualHiddenDimensions_StaySeparateFromOutlineHiddenState()
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "cols",
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "2"),
                        new XAttribute("max", "2"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1"))),
                new XElement(WorksheetNs + "sheetData",
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "3"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1")))));

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        layout.RowColumnLayout.HiddenRows.Should().Contain(3u);
        layout.RowColumnLayout.GroupHiddenRows.Should().NotContain(3u);
        layout.RowColumnLayout.HiddenCols.Should().Contain(2u);
        layout.RowColumnLayout.GroupHiddenCols.Should().NotContain(2u);
    }

    [Fact]
    public void ReadSheetDataLayout_SummaryBefore_ClassifiesHiddenRunsAfterTheirAnchors()
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "sheetPr",
                    new XElement(
                        WorksheetNs + "outlinePr",
                        new XAttribute("summaryBelow", "0"),
                        new XAttribute("summaryRight", "0"))),
                new XElement(WorksheetNs + "cols",
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "2"),
                        new XAttribute("max", "2"),
                        new XAttribute("collapsed", "1")),
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "3"),
                        new XAttribute("max", "4"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1"))),
                new XElement(WorksheetNs + "sheetData",
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "2"),
                        new XAttribute("collapsed", "1")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "3"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "4"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1")))));

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        layout.RowColumnLayout.GroupHiddenRows.Should().Contain([3u, 4u]);
        layout.RowColumnLayout.HiddenRows.Should().NotContain([3u, 4u]);
        layout.RowColumnLayout.GroupHiddenCols.Should().Contain([3u, 4u]);
        layout.RowColumnLayout.HiddenCols.Should().NotContain([3u, 4u]);
    }

    [Fact]
    public void ReadThenSaveRoundTrip_CollapsedAnchorRow_StaysCollapsedAndVisible_DetailRowsStayHiddenOnly()
    {
        // End-to-end: parse a real-Excel-shaped worksheet (detail rows hidden+outlined, anchor row
        // visible+collapsed), apply the reader's layout onto a fresh Sheet the way a loader would,
        // then save and confirm the anchor keeps its collapsed marker while the detail rows are
        // hidden without picking up a spurious one.
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "sheetData",
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "1"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "2"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "3"),
                        new XAttribute("collapsed", "1")))));

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        var workbook = new Workbook("ReadThenSaveAnchorTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("detail1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("detail2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("subtotal"));
        foreach (var (row, level) in layout.RowColumnLayout.RowOutlineLevels)
            sheet.RowOutlineLevels[row] = level;
        sheet.HiddenRows.UnionWith(layout.RowColumnLayout.HiddenRows);
        sheet.GroupHiddenRows.UnionWith(layout.RowColumnLayout.GroupHiddenRows);
        sheet.CollapsedAnchorRows.UnionWith(layout.RowColumnLayout.CollapsedAnchorRows!);

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        var row1 = GetRow(worksheetXml, 1);
        var row2 = GetRow(worksheetXml, 2);
        var row3 = GetRow(worksheetXml, 3);

        row1.Attribute("hidden")!.Value.Should().Be("1");
        row1.Attribute("collapsed").Should().BeNull();
        row2.Attribute("hidden")!.Value.Should().Be("1");
        row2.Attribute("collapsed").Should().BeNull();
        row3.Attribute("collapsed")!.Value.Should().Be("1");
        row3.Attribute("hidden").Should().BeNull();
    }
}

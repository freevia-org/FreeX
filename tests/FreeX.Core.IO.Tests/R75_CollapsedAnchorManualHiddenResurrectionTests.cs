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
/// R75-commands-outline-group-4-1 regression coverage: <c>XlsxFileAdapter.Save</c>'s
/// <c>CollapsedAnchorRows</c>/<c>CollapsedAnchorCols</c> loop calls <c>Collapse()</c> (which also
/// hides) and then only <c>Unhide()</c>s when the row/column is NOT in
/// <see cref="Sheet.GroupHiddenRows"/>/<see cref="Sheet.GroupHiddenCols"/> -- but a collapsed
/// group's anchor row/column can ALSO be hidden for an unrelated reason (the user manually hid it,
/// or a filter hides it), and the old guard resurrected it anyway since it only ever consulted
/// <c>GroupHiddenRows</c>/<c>GroupHiddenCols</c>. The fix extends the guard to also check
/// <see cref="Sheet.HiddenRows"/>/<see cref="Sheet.FilterHiddenRows"/> (rows) and
/// <see cref="Sheet.HiddenCols"/> (columns) before calling <c>Unhide()</c>.
/// </summary>
public sealed class R75_CollapsedAnchorManualHiddenResurrectionTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XElement GetRow(XDocument worksheetXml, int rowNumber) =>
        worksheetXml.Root!
            .Element(WorksheetNs + "sheetData")!
            .Elements(WorksheetNs + "row")
            .Single(r => (string?)r.Attribute("r") == rowNumber.ToString());

    private static XElement GetCol(XDocument worksheetXml, int colNumber) =>
        worksheetXml.Root!
            .Element(WorksheetNs + "cols")!
            .Elements(WorksheetNs + "col")
            .Single(c =>
                int.Parse((string)c.Attribute("min")!) <= colNumber &&
                colNumber <= int.Parse((string)c.Attribute("max")!));

    [Fact]
    public void Save_CollapsedAnchorRow_AlsoManuallyHidden_StaysHidden_NotResurrected()
    {
        // Rows 2-9 form a collapsed group whose anchor is row 10; the user then ALSO manually hides
        // row 10 itself (e.g. via a plain hide, unrelated to the outline group).
        var workbook = new Workbook("CollapsedAnchorManualHideRowTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("detail"));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("subtotal"));

        for (uint r = 2; r <= 9; r++)
        {
            sheet.RowOutlineLevels[r] = 1;
            sheet.GroupHiddenRows.Add(r);
        }
        sheet.CollapsedAnchorRows.Add(10);
        sheet.HiddenRows.Add(10); // manually hidden, independent of the outline group

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var row10 = GetRow(worksheetXml, 10);

        row10.Attribute("collapsed")?.Value.Should().Be("1");
        row10.Attribute("hidden")?.Value.Should().Be("1",
            "the anchor row is ALSO manually hidden, so it must stay hidden -- not be wrongly " +
            "resurrected because the guard only checked GroupHiddenRows");
    }

    [Fact]
    public void Save_CollapsedAnchorRow_NotManuallyHidden_StaysVisible_NoRegression()
    {
        // Sibling no-regression case: a collapsed anchor row that is NOT manually hidden (and not a
        // hidden detail row of an outer group either) must stay visible, exactly as before the fix.
        var workbook = new Workbook("CollapsedAnchorPlainVisibleRowTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("detail"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("subtotal"));

        sheet.RowOutlineLevels[2] = 1;
        sheet.GroupHiddenRows.Add(2);
        sheet.CollapsedAnchorRows.Add(3);

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var row3 = GetRow(worksheetXml, 3);

        row3.Attribute("collapsed")?.Value.Should().Be("1");
        row3.Attribute("hidden").Should().BeNull(
            "a collapsed anchor row that is not otherwise hidden must remain visible");
    }

    [Fact]
    public void Save_CollapsedAnchorColumn_AlsoManuallyHidden_StaysHidden_NotResurrected()
    {
        var workbook = new Workbook("CollapsedAnchorManualHideColTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("detail"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 10), new TextValue("subtotal"));

        for (uint c = 2; c <= 9; c++)
        {
            sheet.ColOutlineLevels[c] = 1;
            sheet.GroupHiddenCols.Add(c);
        }
        sheet.CollapsedAnchorCols.Add(10);
        sheet.HiddenCols.Add(10); // manually hidden, independent of the outline group

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var col10 = GetCol(worksheetXml, 10);

        col10.Attribute("collapsed")?.Value.Should().Be("1");
        col10.Attribute("hidden")?.Value.Should().Be("1",
            "the anchor column is ALSO manually hidden, so it must stay hidden -- not be wrongly " +
            "resurrected because the guard only checked GroupHiddenCols");
    }

    [Fact]
    public void Save_CollapsedAnchorColumn_NotManuallyHidden_StaysVisible_NoRegression()
    {
        var workbook = new Workbook("CollapsedAnchorPlainVisibleColTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("detail"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("subtotal"));

        sheet.ColOutlineLevels[1] = 1;
        sheet.GroupHiddenCols.Add(1);
        sheet.CollapsedAnchorCols.Add(2);

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var col2 = GetCol(worksheetXml, 2);

        col2.Attribute("collapsed")?.Value.Should().Be("1");
        col2.Attribute("hidden").Should().BeNull(
            "a collapsed anchor column that is not otherwise hidden must remain visible");
    }
}

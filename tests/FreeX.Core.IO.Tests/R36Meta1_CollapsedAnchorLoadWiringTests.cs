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
/// Regression coverage for R36-meta-1: the r35 collapse-anchor fix taught
/// <see cref="XlsxWorksheetRowColumnLayoutReader"/> to compute
/// <c>CollapsedAnchorRows</c>/<c>CollapsedAnchorCols</c> into its returned
/// <c>XlsxWorksheetRowColumnLayout</c>, but the internal <c>SheetXmlLayout</c> record used by the
/// real file-load path (<c>XlsxFileAdapter.LoadSheetXmlLayout</c>) had no matching fields, and
/// <c>ApplySheetXmlLayout</c> never unioned them into the loaded <see cref="Sheet"/>. Net effect: on
/// any FILE-LOAD path, <see cref="Sheet.CollapsedAnchorRows"/>/<see cref="Sheet.CollapsedAnchorCols"/>
/// stayed empty, so loading a workbook with a collapsed outline group and resaving it (without
/// touching the grouping) silently dropped the "+/-" outline toggle -- strictly worse than the
/// pre-r35 behavior. These tests exercise the REAL loader (<c>XlsxFileAdapter.Load</c>), not the
/// reader/writer in isolation, to prove the wiring end to end with no manual UnionWith.
/// </summary>
public sealed class R36Meta1_CollapsedAnchorLoadWiringTests
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
    public void Load_CollapsedRowGroup_PopulatesSheetCollapsedAnchorRows_AndResaveKeepsAnchorOnly()
    {
        // Build a source workbook the way CollapseRowGroupCommand would leave it: rows 1-2 are
        // hidden detail rows, row 3 is the visible collapsed anchor. Save it with the (already
        // correct, per r35) writer to get a real Excel-shaped sheet1.xml.
        var sourceWorkbook = new Workbook("CollapsedRowAnchorSource");
        var sourceSheet = sourceWorkbook.AddSheet("Sheet1");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new TextValue("detail1"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 1), new TextValue("detail2"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 3, 1), new TextValue("subtotal"));
        sourceSheet.RowOutlineLevels[1] = 1;
        sourceSheet.RowOutlineLevels[2] = 1;
        sourceSheet.GroupHiddenRows.Add(1);
        sourceSheet.GroupHiddenRows.Add(2);
        sourceSheet.CollapsedAnchorRows.Add(3);

        var sourceMs = new MemoryStream();
        new XlsxFileAdapter().Save(sourceWorkbook, sourceMs);
        sourceMs.Position = 0;

        // This is the crux of R36-meta-1: go through the REAL loader (not manual reader plumbing).
        var loadedWorkbook = new XlsxFileAdapter().Load(sourceMs);
        var loadedSheet = loadedWorkbook.Sheets.Single(s => s.Name == "Sheet1");

        loadedSheet.CollapsedAnchorRows.Should().Contain(3u);
        loadedSheet.CollapsedAnchorRows.Should().NotContain(1u);
        loadedSheet.CollapsedAnchorRows.Should().NotContain(2u);

        // And resaving the loaded model must keep writing collapsed="1" on the anchor only.
        var resaveMs = new MemoryStream();
        new XlsxFileAdapter().Save(loadedWorkbook, resaveMs);
        resaveMs.Position = 0;

        using var archive = new ZipArchive(resaveMs, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        var row1 = GetRow(worksheetXml, 1);
        var row2 = GetRow(worksheetXml, 2);
        var row3 = GetRow(worksheetXml, 3);

        row1.Attribute("hidden")?.Value.Should().Be("1");
        row1.Attribute("collapsed").Should().BeNull();
        row2.Attribute("hidden")?.Value.Should().Be("1");
        row2.Attribute("collapsed").Should().BeNull();
        row3.Attribute("collapsed")?.Value.Should().Be("1");
        row3.Attribute("hidden").Should().BeNull();
    }

    [Fact]
    public void Load_CollapsedColumnGroup_PopulatesSheetCollapsedAnchorCols_AndResaveKeepsAnchorOnly()
    {
        var sourceWorkbook = new Workbook("CollapsedColAnchorSource");
        var sourceSheet = sourceWorkbook.AddSheet("Sheet1");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new TextValue("detail1"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 2), new TextValue("detail2"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 3), new TextValue("subtotal"));
        sourceSheet.ColOutlineLevels[1] = 1;
        sourceSheet.ColOutlineLevels[2] = 1;
        sourceSheet.GroupHiddenCols.Add(1);
        sourceSheet.GroupHiddenCols.Add(2);
        sourceSheet.CollapsedAnchorCols.Add(3);

        var sourceMs = new MemoryStream();
        new XlsxFileAdapter().Save(sourceWorkbook, sourceMs);
        sourceMs.Position = 0;

        var loadedWorkbook = new XlsxFileAdapter().Load(sourceMs);
        var loadedSheet = loadedWorkbook.Sheets.Single(s => s.Name == "Sheet1");

        loadedSheet.CollapsedAnchorCols.Should().Contain(3u);
        loadedSheet.CollapsedAnchorCols.Should().NotContain(1u);
        loadedSheet.CollapsedAnchorCols.Should().NotContain(2u);

        var resaveMs = new MemoryStream();
        new XlsxFileAdapter().Save(loadedWorkbook, resaveMs);
        resaveMs.Position = 0;

        using var archive = new ZipArchive(resaveMs, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        var col1 = GetCol(worksheetXml, 1);
        var col2 = GetCol(worksheetXml, 2);
        var col3 = GetCol(worksheetXml, 3);

        col1.Attribute("hidden")?.Value.Should().Be("1");
        col1.Attribute("collapsed").Should().BeNull();
        col2.Attribute("hidden")?.Value.Should().Be("1");
        col2.Attribute("collapsed").Should().BeNull();
        col3.Attribute("collapsed")?.Value.Should().Be("1");
        col3.Attribute("hidden").Should().BeNull();
    }

    [Fact]
    public void Load_PlainHiddenGroupRows_WithNoAnchor_LeavesCollapsedAnchorRowsEmpty_NoRegression()
    {
        // Sibling no-regression case: a loaded sheet with plain hidden group rows and no collapsed
        // anchor in the source file must not spuriously populate CollapsedAnchorRows.
        var sourceWorkbook = new Workbook("PlainGroupHiddenSource");
        var sourceSheet = sourceWorkbook.AddSheet("Sheet1");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new TextValue("detail1"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 1), new TextValue("detail2"));
        sourceSheet.RowOutlineLevels[1] = 1;
        sourceSheet.RowOutlineLevels[2] = 1;
        sourceSheet.GroupHiddenRows.Add(1);
        sourceSheet.GroupHiddenRows.Add(2);

        var sourceMs = new MemoryStream();
        new XlsxFileAdapter().Save(sourceWorkbook, sourceMs);
        sourceMs.Position = 0;

        var loadedWorkbook = new XlsxFileAdapter().Load(sourceMs);
        var loadedSheet = loadedWorkbook.Sheets.Single(s => s.Name == "Sheet1");

        loadedSheet.CollapsedAnchorRows.Should().BeEmpty();
        loadedSheet.HiddenRows.Should().Contain([1u, 2u]);
    }
}

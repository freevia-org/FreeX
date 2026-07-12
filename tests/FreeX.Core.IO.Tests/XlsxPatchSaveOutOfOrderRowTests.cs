using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for patch-save's <c>FindOrCreateRow</c> helper (XlsxFileAdapter.SourcePackageSnapshot.cs),
/// which locates the &lt;row r="N"&gt; element to insert a brand-new cell into. The lookup must not assume the
/// source worksheet's &lt;row&gt; elements appear in ascending document order: a schema-valid but non-ascending
/// source (e.g. produced by a non-Excel writer) previously made the lookup break on the first row whose r
/// exceeds the target before ever reaching the true match later in the document, fabricating a duplicate
/// &lt;row r="N"&gt; instead of reusing the existing one.
/// </summary>
public sealed class XlsxPatchSaveOutOfOrderRowTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void PatchSave_InsertingCellIntoExistingRow_PrecededInDocumentByHigherNumberedRow_DoesNotDuplicateRow()
    {
        // Document order is row 1, row 5, row 2 (row 2's r="2" is numerically smaller than the
        // preceding row 5, so a lookup that stops scanning at the first r > target would never
        // reach the real row 2 and would fabricate a duplicate <row r="2">).
        using var source = CreateWorkbookPackage(rowNumbers: [1, 5, 2]);
        ReorderRowsInDocumentOrder(source, documentOrder: ["1", "5", "2"]);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        // Column 2 (B2) does not exist yet on row 2 -- inserting it exercises InsertLiteralCell ->
        // FindOrCreateRow's existing-row lookup rather than RewriteStyleOnlyCellAsLiteral/FindCell.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("inserted"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

        var sheetData = ReadSheetData(saved);
        var rowsNumberedTwo = sheetData.Elements(WorksheetNs + "row")
            .Where(row => row.Attribute("r")?.Value == "2")
            .ToList();
        rowsNumberedTwo.Should().ContainSingle("the existing row 2 must be reused, not duplicated");

        var row2Cells = rowsNumberedTwo[0].Elements(WorksheetNs + "c").ToList();
        row2Cells.Select(c => c.Attribute("r")!.Value).Should().Equal("A2", "B2");
        row2Cells.Single(c => c.Attribute("r")!.Value == "B2")
            .Element(WorksheetNs + "is")!
            .Value
            .Should()
            .Be("inserted");

        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheetAt(0);
        reloadedSheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("row2"));
        reloadedSheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("inserted"));
        reloadedSheet.GetCell(5, 1)!.Value.Should().Be(new TextValue("row5"));
    }

    [Fact]
    public void PatchSave_InsertingBrandNewRow_IntoAscendingDocumentOrder_InsertsAtSortedPosition()
    {
        // Sibling already-working case: rows already in ascending document order (the common,
        // Excel-authored shape) and the new cell's row (3) does not exist yet, so FindOrCreateRow
        // must fabricate <row r="3"> and place it between the existing row 2 and row 5 elements.
        using var source = CreateWorkbookPackage(rowNumbers: [1, 2, 5]);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("inserted-row3"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

        var sheetData = ReadSheetData(saved);
        sheetData.Elements(WorksheetNs + "row")
            .Select(row => row.Attribute("r")!.Value)
            .Should()
            .Equal("1", "2", "3", "5");

        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheetAt(0);
        reloadedSheet.GetCell(3, 1)!.Value.Should().Be(new TextValue("inserted-row3"));
        reloadedSheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("row2"));
        reloadedSheet.GetCell(5, 1)!.Value.Should().Be(new TextValue("row5"));
    }

    private static MemoryStream CreateWorkbookPackage(int[] rowNumbers)
    {
        var workbook = new Workbook("OutOfOrderRowsPatchSave");
        var sheet = workbook.AddSheet("Data");
        foreach (var rowNumber in rowNumbers)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)rowNumber, 1), new TextValue($"row{rowNumber}"));
        }

        return XlsxPackageTestHelper.SaveWorkbook(workbook);
    }

    /// <summary>
    /// Physically reorders the &lt;row&gt; children of sheetData in <paramref name="source"/> to match
    /// <paramref name="documentOrder"/> (a list of "r" attribute values), without changing any row's r
    /// attribute. This simulates a schema-valid, non-Excel-authored source whose rows are not written in
    /// ascending numeric order.
    /// </summary>
    private static void ReorderRowsInDocumentOrder(MemoryStream source, string[] documentOrder)
    {
        XlsxPackageTestHelper.PatchWorksheetXml(source, document =>
        {
            var sheetData = document.Root!.Element(WorksheetNs + "sheetData")!;
            var rowsByNumber = sheetData.Elements(WorksheetNs + "row")
                .ToDictionary(row => row.Attribute("r")!.Value);

            foreach (var row in rowsByNumber.Values)
            {
                row.Remove();
            }

            foreach (var rowNumber in documentOrder)
            {
                sheetData.Add(rowsByNumber[rowNumber]);
            }
        });
    }

    private static XElement ReadSheetData(MemoryStream saved)
    {
        var worksheetXml = XlsxPackageTestHelper.ReadWorksheetXml(saved);
        return worksheetXml.Root!.Element(WorksheetNs + "sheetData")!;
    }
}

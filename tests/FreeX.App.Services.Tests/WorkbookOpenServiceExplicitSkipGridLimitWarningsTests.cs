using System.Text;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R156-remediation-p4: WorkbookOpenServiceGridLimitWarningsTests exercises the used-range-boundary
/// HEURISTIC in WorkbookOpenService.DetectGridLimitTruncationWarnings via a fake IFileAdapter that
/// writes a cell exactly at CellAddress.MaxRow/MaxCol. That heuristic is blind to a real adapter that
/// SKIPS an explicitly out-of-range record instead of landing on the boundary: SlkFileAdapter's
/// HandleCellRecord and SpreadsheetXmlFileAdapter's ReadWorksheet both do exactly that for a sparse
/// source file, and NativeJsonAdapter's TryGetCellAddress rejects an out-of-range address before it
/// ever becomes a CellAddress. These tests drive WorkbookOpenService.LoadAsync -- the real production
/// call site -- with the REAL adapters and hand-authored files reproducing each skip, plus a sibling
/// no-regression case per format proving an ordinary small file still opens silently.
/// </summary>
public sealed class WorkbookOpenServiceExplicitSkipGridLimitWarningsTests
{
    private static async Task<string> WriteFileAsync(TestTemporaryDirectory temp, string fileName, string content)
    {
        var path = Path.Combine(temp.Path, fileName);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    // ---- SlkFileAdapter -----------------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_SlkSparseFileWithOutOfRangeRecord_YieldsGridLimitWarning()
    {
        using var temp = new TestTemporaryDirectory();
        // Two C (cell) records: one in range, one explicitly addressed one row past MaxRow. Real
        // Excel's own SYLK writer emits one record per occupied cell, so a sparse source workbook
        // never touches the rows in between -- the loaded sheet's used range therefore stops at
        // row 1, nowhere near the boundary, and the old heuristic saw nothing to warn about.
        var path = await WriteFileAsync(temp, "sparse.slk", "ID;PFreeX\r\nC;Y1;X1;K1\r\nC;Y1048577;X1;K2\r\nE\r\n");

        var result = await new WorkbookOpenService().LoadAsync(
            path,
            new SlkFileAdapter(),
            ".slk",
            new FileFormatDescriptor(".slk", "SYLK (Symbolic Link)"));

        result.LoadWarnings.Should().ContainSingle(
            w => w.Contains("grid-limit", StringComparison.OrdinalIgnoreCase) &&
                 w.Contains("Sheet1", StringComparison.Ordinal));
        result.Workbook.GetSheetAt(0).GetCell(1, 1).Should().NotBeNull();
    }

    [Fact]
    public async Task LoadAsync_SlkOrdinaryFile_NoRegressionNoWarnings()
    {
        using var temp = new TestTemporaryDirectory();
        var path = await WriteFileAsync(temp, "normal.slk", "ID;PFreeX\r\nC;Y1;X1;K1\r\nC;Y2;X2;K2\r\nE\r\n");

        var result = await new WorkbookOpenService().LoadAsync(
            path,
            new SlkFileAdapter(),
            ".slk",
            new FileFormatDescriptor(".slk", "SYLK (Symbolic Link)"));

        result.LoadWarnings.Should().BeEmpty();
    }

    // ---- SpreadsheetXmlFileAdapter -------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_SpreadsheetXmlSparseFileWithIndexJumpPastLimit_YieldsGridLimitWarning()
    {
        using var temp = new TestTemporaryDirectory();
        // A Row ss:Index jump from 5 straight to 2,000,000 -- exactly how Excel's own SpreadsheetML
        // writer represents sparse rows -- crosses CellAddress.MaxRow (1,048,576) without ReadWorksheet
        // ever writing a cell at the boundary.
        var path = await WriteFileAsync(temp, "sparse.xml", @"<?xml version=""1.0""?>
<ss:Workbook xmlns:ss=""urn:schemas-microsoft-com:office:spreadsheet"">
  <ss:Worksheet ss:Name=""Sheet1"">
    <ss:Table>
      <ss:Row ss:Index=""5"">
        <ss:Cell><ss:Data ss:Type=""String"">Five</ss:Data></ss:Cell>
      </ss:Row>
      <ss:Row ss:Index=""2000000"">
        <ss:Cell><ss:Data ss:Type=""String"">TooFar</ss:Data></ss:Cell>
      </ss:Row>
    </ss:Table>
  </ss:Worksheet>
</ss:Workbook>
");

        var result = await new WorkbookOpenService().LoadAsync(
            path,
            new SpreadsheetXmlFileAdapter(),
            ".xml",
            new FileFormatDescriptor(".xml", "XML Spreadsheet 2003"));

        result.LoadWarnings.Should().ContainSingle(
            w => w.Contains("grid-limit", StringComparison.OrdinalIgnoreCase) &&
                 w.Contains("Sheet1", StringComparison.Ordinal));
        result.Workbook.GetSheetAt(0).GetCell(5, 1).Should().NotBeNull();
    }

    [Fact]
    public async Task LoadAsync_SpreadsheetXmlOrdinaryFile_NoRegressionNoWarnings()
    {
        using var temp = new TestTemporaryDirectory();
        var path = await WriteFileAsync(temp, "normal.xml", @"<?xml version=""1.0""?>
<ss:Workbook xmlns:ss=""urn:schemas-microsoft-com:office:spreadsheet"">
  <ss:Worksheet ss:Name=""Sheet1"">
    <ss:Table>
      <ss:Row ss:Index=""1"">
        <ss:Cell><ss:Data ss:Type=""String"">One</ss:Data></ss:Cell>
      </ss:Row>
      <ss:Row ss:Index=""2"">
        <ss:Cell><ss:Data ss:Type=""String"">Two</ss:Data></ss:Cell>
      </ss:Row>
    </ss:Table>
  </ss:Worksheet>
</ss:Workbook>
");

        var result = await new WorkbookOpenService().LoadAsync(
            path,
            new SpreadsheetXmlFileAdapter(),
            ".xml",
            new FileFormatDescriptor(".xml", "XML Spreadsheet 2003"));

        result.LoadWarnings.Should().BeEmpty();
    }

    // ---- NativeJsonAdapter ----------------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_NativeJsonSparseFileWithOutOfRangeAddress_YieldsGridLimitWarning()
    {
        using var temp = new TestTemporaryDirectory();
        // "A1048577" is well-formed COL+ROW shape but one row past CellAddress.MaxRow -- TryGetCellAddress
        // drops it before a CellAddress is ever constructed, so no cell lands at the boundary either.
        var path = await WriteFileAsync(temp, "sparse.fxl", @"{
  ""Name"": ""Sparse"",
  ""Sheets"": [
    {
      ""Name"": ""Sheet1"",
      ""Cells"": [
        { ""Address"": ""A1"", ""Value"": ""kept"", ""ValueType"": ""t"" },
        { ""Address"": ""A1048577"", ""Value"": ""dropped"", ""ValueType"": ""t"" }
      ]
    }
  ]
}
");

        var result = await new WorkbookOpenService().LoadAsync(
            path,
            new NativeJsonAdapter(),
            ".fxl",
            new FileFormatDescriptor(".fxl", "FreeX Workbook"));

        result.LoadWarnings.Should().ContainSingle(
            w => w.Contains("grid-limit", StringComparison.OrdinalIgnoreCase) &&
                 w.Contains("Sheet1", StringComparison.Ordinal));
        result.Workbook.GetSheetAt(0).GetCell(1, 1).Should().NotBeNull();
    }

    [Fact]
    public async Task LoadAsync_NativeJsonOrdinaryFile_NoRegressionNoWarnings()
    {
        using var temp = new TestTemporaryDirectory();
        var path = await WriteFileAsync(temp, "normal.fxl", @"{
  ""Name"": ""Ordinary"",
  ""Sheets"": [
    {
      ""Name"": ""Sheet1"",
      ""Cells"": [
        { ""Address"": ""A1"", ""Value"": ""kept"", ""ValueType"": ""t"" },
        { ""Address"": ""B2"", ""Value"": ""also kept"", ""ValueType"": ""t"" }
      ]
    }
  ]
}
");

        var result = await new WorkbookOpenService().LoadAsync(
            path,
            new NativeJsonAdapter(),
            ".fxl",
            new FileFormatDescriptor(".fxl", "FreeX Workbook"));

        result.LoadWarnings.Should().BeEmpty();
    }
}

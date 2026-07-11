using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using NPOI.HSSF.UserModel;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-26 findings R26-calc-chain-dependency-order-1 and -3: <c>ShouldRecalculateLoadedFormulas</c>
/// must match real Excel's "trust the file's cached values unless told otherwise" behavior for BOTH
/// container formats it can see (OOXML .xlsx and legacy BIFF .xls), while still always recalculating
/// volatile functions (NOW/TODAY/RAND/...) on open as long as calculation is not set to Manual.
/// </summary>
public sealed class WorkbookOpenServiceCalcTrustTests
{
    [Fact]
    public async Task LoadAsync_XlsxWithVolatileFormulaRecalculatesEvenWithoutFullCalcOnLoad()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "volatile.xlsx");
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("TODAY()", calcMode: "auto"));
        var recalculateCalled = false;
        var service = new WorkbookOpenService(_ => recalculateCalled = true);

        await service.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        recalculateCalled.Should().BeTrue(
            "real Excel recalculates volatile functions on every open in Automatic mode, even when the file has no fullCalcOnLoad flag");
    }

    [Fact]
    public async Task LoadAsync_XlsxWithNonVolatileFormulaStillTrustsCachedValues()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "non-volatile.xlsx");
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("SUM(A1:B1)", calcMode: "auto"));
        var recalculateCalled = false;
        var service = new WorkbookOpenService(_ => recalculateCalled = true);

        await service.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        recalculateCalled.Should().BeFalse(
            "a workbook with only non-volatile formulas and no fullCalcOnLoad flag should keep trusting its cached values");
    }

    [Fact]
    public async Task LoadAsync_XlsxWithVolatileFormulaInManualCalcModeDoesNotRecalculate()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "volatile-manual.xlsx");
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("TODAY()", calcMode: "manual"));
        var recalculateCalled = false;
        var service = new WorkbookOpenService(_ => recalculateCalled = true);

        await service.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        recalculateCalled.Should().BeFalse(
            "real Excel does not auto-recalculate anything -- including volatile functions -- on open when calculation is set to Manual");
    }

    [Fact]
    public async Task LoadAsync_LegacyXlsWithCachedFormulaTrustsCachedValuesWhenNotFlaggedForRecalculation()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "cached.xls");
        await File.WriteAllBytesAsync(tempPath, CreateLegacyXlsWithCachedFormula(forceFormulaRecalculation: false));
        var recalculateCalled = false;
        var service = new WorkbookOpenService(_ => recalculateCalled = true);

        await service.LoadAsync(
            tempPath,
            new LegacyXlsFileAdapter(),
            ".xls",
            new FileFormatDescriptor(".xls", "Excel 97-2003 Workbook", CanOpen: true, CanSave: false),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        recalculateCalled.Should().BeFalse(
            "a legacy .xls file whose own sheet recalc flag says recalculation is not required should keep its cached formula values, exactly like the equivalent .xlsx case");
    }

    [Fact]
    public async Task LoadAsync_LegacyXlsRecalculatesWhenFileRequestsForcedRecalculation()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "force-recalc.xls");
        await File.WriteAllBytesAsync(tempPath, CreateLegacyXlsWithCachedFormula(forceFormulaRecalculation: true));
        var recalculateCalled = false;
        var service = new WorkbookOpenService(_ => recalculateCalled = true);

        await service.LoadAsync(
            tempPath,
            new LegacyXlsFileAdapter(),
            ".xls",
            new FileFormatDescriptor(".xls", "Excel 97-2003 Workbook", CanOpen: true, CanSave: false),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        recalculateCalled.Should().BeTrue(
            "the file's own BIFF sheet recalc flag asked for a forced recalculation on open");
    }

    private static byte[] CreateXlsxWithFormula(string formula, string calcMode)
    {
        var parts = new (string Name, string Content)[]
        {
            ("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            ("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            ("xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """),
            ("xl/workbook.xml",
                $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="FormulaCases" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcMode="{{calcMode}}"/>
                </workbook>
                """),
            ("xl/worksheets/sheet1.xml",
                $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1">
                      <c r="A1"><v>2</v></c>
                      <c r="B1"><v>3</v></c>
                      <c r="C1"><f>{{formula}}</f><v>5</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """),
        };

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        return buffer.ToArray();
    }

    private static byte[] CreateLegacyXlsWithCachedFormula(bool forceFormulaRecalculation)
    {
        var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Sheet1");
        var row = sheet.CreateRow(0);
        row.CreateCell(0).SetCellValue(2);
        row.CreateCell(1).SetCellValue(3);
        var formulaCell = row.CreateCell(2);
        formulaCell.SetCellFormula("SUM(A1:B1)");
        hssf.GetCreationHelper().CreateFormulaEvaluator().EvaluateFormulaCell(formulaCell);
        // The per-sheet BIFF "recalc on load" flag (mirrors the .xlsx sheetCalcPr fullCalcOnLoad
        // flag) -- LegacyXlsFileAdapter maps ISheet.ForceFormulaRecalculation into
        // Sheet.FullCalculationOnLoad (LegacyXlsFileAdapter.cs:621), which is exactly what
        // ShouldRecalculateLoadedFormulas now also honors for legacy .xls opens.
        sheet.ForceFormulaRecalculation = forceFormulaRecalculation;

        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }
}

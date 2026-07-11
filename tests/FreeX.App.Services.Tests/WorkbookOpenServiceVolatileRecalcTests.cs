using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-28 finding R28-meta-1: round-27's fix for the round-26 over-broad "recalc the whole
/// workbook whenever any volatile function exists" bug swung too far the other way and stopped
/// recalculating volatile cells (NOW/RAND/TODAY/OFFSET/INDIRECT/...) on open entirely, so they kept
/// showing the file's stale cached value indefinitely. Real Excel selectively refreshes ONLY the
/// volatile cells (and their dependents) on an Automatic-mode open, while every other formula keeps
/// trusting its cached value. These tests pin that selective behavior for both sides: the volatile
/// cell that must change, and a sibling non-volatile cell (with a deliberately wrong cached value)
/// that must NOT change -- plus the still-full-recalc path when the file itself demands it.
/// </summary>
public sealed class WorkbookOpenServiceVolatileRecalcTests
{
    [Fact]
    public async Task LoadAsync_XlsxWithNowFormulaReevaluatesStaleVolatileValueOnOpen()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "now.xlsx");
        // A long-stale cached serial date (year ~2009); real Excel would never still show this
        // after opening the file today, because NOW() re-evaluates on every open.
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("NOW()", cachedValue: "40000"));
        var fullRecalculateCalled = false;
        var service = new WorkbookOpenService(_ => fullRecalculateCalled = true);

        var result = await service.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        fullRecalculateCalled.Should().BeFalse(
            "the selective volatile-only recalc must not go through the full-workbook recalc callback");
        var sheet = result.Workbook.GetSheet("FormulaCases");
        // NOW() evaluates to a DateTimeValue (an OLE-Automation serial date, same underlying double
        // representation as a plain number), not a NumberValue.
        var value = sheet!.GetCell(1, 3)!.Value.Should().BeOfType<DateTimeValue>().Subject;
        value.Value.Should().BeGreaterThan(45000,
            "NOW() must re-evaluate to today's serial date on open instead of keeping the file's " +
            "stale cached value from years ago");
    }

    [Fact]
    public async Task LoadAsync_XlsxWithRandFormulaReevaluatesStaleVolatileValueOnOpen()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "rand.xlsx");
        // A cached value outside RAND()'s [0, 1) range proves the loaded value could not have come
        // from a real RAND() evaluation -- it must be the stale value from the last save.
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("RAND()", cachedValue: "5"));
        var service = new WorkbookOpenService();

        var result = await service.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        var sheet = result.Workbook.GetSheet("FormulaCases");
        var value = sheet!.GetCell(1, 3)!.Value.Should().BeOfType<NumberValue>().Subject;
        value.Value.Should().BeInRange(0d, 1d,
            "RAND() must re-evaluate on open; the stale cached 5 is outside RAND()'s [0, 1) range");
    }

    [Fact]
    public async Task LoadAsync_XlsxWithNonVolatileFormulaAndTrustedCacheKeepsStaleCachedValueUnrecalculated()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "non-volatile.xlsx");
        // SUM(A1:B1) with A1=2,B1=3 truly evaluates to 5; cache it as a deliberately WRONG 999 so a
        // survived 999 can only mean the trusted cache was left alone, not "recomputed to the same
        // answer by coincidence".
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("SUM(A1:B1)", cachedValue: "999"));
        var fullRecalculateCalled = false;
        var service = new WorkbookOpenService(_ => fullRecalculateCalled = true);

        var result = await service.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        fullRecalculateCalled.Should().BeFalse(
            "a workbook with only non-volatile formulas and no fullCalcOnLoad flag should keep " +
            "trusting its cached values and never reach the full-recalc callback");
        var sheet = result.Workbook.GetSheet("FormulaCases");
        sheet!.GetCell(1, 3)!.Value.Should().Be(new NumberValue(999),
            "the selective volatile-only recalc must leave a non-volatile formula's trusted cached " +
            "value completely untouched, even though it happens to be stale/wrong");
    }

    [Fact]
    public async Task LoadAsync_XlsxWithFullCalcOnLoadFlagStillFullyRecalculates()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "full-calc-on-load.xlsx");
        await File.WriteAllBytesAsync(
            tempPath,
            CreateXlsxWithFormula("SUM(A1:B1)", cachedValue: "999", fullCalcOnLoad: true));
        var fullRecalculateCalled = false;
        var service = new WorkbookOpenService(_ => fullRecalculateCalled = true);

        await service.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        fullRecalculateCalled.Should().BeTrue(
            "the file's own calcPr fullCalcOnLoad flag asks Excel (and FreeX) to fully recalculate " +
            "the workbook on open, unconditionally, not just its volatile cells");
    }

    [Fact]
    public async Task LoadAsync_XlsxWithManualCalculationModeLeavesStaleVolatileValueUntouched()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "manual-now.xlsx");
        await File.WriteAllBytesAsync(
            tempPath,
            CreateXlsxWithFormula("NOW()", cachedValue: "40000", calcMode: "manual"));
        var service = new WorkbookOpenService();

        var result = await service.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        var sheet = result.Workbook.GetSheet("FormulaCases");
        // Nothing recalculates in Manual mode, so the cell keeps whatever the loader read straight
        // from the file's cached <v> element (a plain number -- this minimal test file has no
        // number format attached, so it was never reclassified as a date/time value either).
        var value = sheet!.GetCell(1, 3)!.Value.Should().BeOfType<NumberValue>().Subject;
        value.Value.Should().Be(40000,
            "real Excel does not recalculate anything -- including volatile functions -- on open " +
            "when the workbook's own calculation mode is Manual; only an explicit F9/edit should");
    }

    private static byte[] CreateXlsxWithFormula(
        string formula,
        string cachedValue,
        bool fullCalcOnLoad = false,
        string calcMode = "auto")
    {
        var calcPrAttributes = fullCalcOnLoad
            ? $""" calcMode="{calcMode}" fullCalcOnLoad="1" """
            : $""" calcMode="{calcMode}" """;
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
                  <calcPr{{calcPrAttributes}}/>
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
                      <c r="C1"><f>{{formula}}</f><v>{{cachedValue}}</v></c>
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
}

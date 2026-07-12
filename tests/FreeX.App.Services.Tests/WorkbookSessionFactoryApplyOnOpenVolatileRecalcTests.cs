using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-30 finding R30-meta-1: round-29's fix moved the on-open selective volatile recalc
/// (NOW/RAND/TODAY/OFFSET/INDIRECT/...) out of <see cref="WorkbookOpenService"/> and into
/// <see cref="WorkbookSessionFactory.Create"/> -- but the WPF host's File&gt;Open path never calls
/// <c>Create</c>; it only rebuilds the dependency graph via
/// <see cref="RecalcEngine.RebuildFormulaDependencies"/> and stops there, so volatile cells went
/// stale again on Windows opens (a regression from the round-29 fix).
///
/// The fix extracts the selective recalc into a public static helper,
/// <see cref="WorkbookSessionFactory.ApplyOnOpenVolatileRecalc"/>, so it can be called both from
/// <c>Create</c> (Avalonia host) and directly by the WPF host after its own
/// <c>RebuildFormulaDependencies</c> call. These tests exercise the helper exactly the way the WPF
/// host now does: <see cref="WorkbookOpenService.LoadAsync"/>, then a fresh
/// <see cref="RecalcEngine"/> whose dependency graph is rebuilt, then the helper -- without ever
/// going through <see cref="WorkbookSessionFactory.Create"/>/<c>CreateOpened</c>.
/// </summary>
public sealed class WorkbookSessionFactoryApplyOnOpenVolatileRecalcTests
{
    [Fact]
    public async Task ApplyOnOpenVolatileRecalc_VolatilityHiddenBehindDefinedNameRefreshesOnOpen()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "named-volatile.xlsx");
        // The cell's own formula text is "SUM(SalesRange)" -- it never mentions OFFSET itself; the
        // volatility is hidden behind the defined name SalesRange, whose refers-to formula is
        // OFFSET(FormulaCases!$A$1,0,0). Only resolving the name and recursing into its formula
        // text (RecalcEngine.CollectReferences' NamedRangeNode case) can see this.
        await File.WriteAllBytesAsync(
            tempPath,
            CreateXlsxWithNamedRangeFormula(
                cellFormula: "SUM(SalesRange)",
                namedFormulaText: "OFFSET(FormulaCases!$A$1,0,0)",
                cachedValue: "999"));

        var workbook = await LoadAndApplyOnOpenAsync(tempPath);

        var sheet = workbook.GetSheet("FormulaCases");
        sheet!.GetCell(1, 3)!.Value.Should().Be(new NumberValue(2),
            "SalesRange's own definition (OFFSET(...)) is volatile, so =SUM(SalesRange) must " +
            "refresh via the extracted helper exactly as it did through WorkbookSessionFactory.Create " +
            "-- the stale cached 999 must not survive");
    }

    [Fact]
    public async Task ApplyOnOpenVolatileRecalc_NonVolatileFormulaWithTrustedCacheStaysUntouched()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "non-volatile.xlsx");
        // SUM(A1:B1) with A1=2,B1=3 truly evaluates to 5; cache it as a deliberately WRONG 999 so a
        // survived 999 can only mean the selective recalc left it alone.
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("SUM(A1:B1)", cachedValue: "999"));

        var workbook = await LoadAndApplyOnOpenAsync(tempPath);

        var sheet = workbook.GetSheet("FormulaCases");
        sheet!.GetCell(1, 3)!.Value.Should().Be(new NumberValue(999),
            "the helper must leave a non-volatile formula's trusted cached value untouched, " +
            "even though it happens to be stale/wrong");
    }

    [Fact]
    public async Task ApplyOnOpenVolatileRecalc_ManualCalculationModeLeavesStaleVolatileValueUntouched()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "manual-now.xlsx");
        await File.WriteAllBytesAsync(
            tempPath,
            CreateXlsxWithFormula("NOW()", cachedValue: "40000", calcMode: "manual"));

        var workbook = await LoadAndApplyOnOpenAsync(tempPath);

        var sheet = workbook.GetSheet("FormulaCases");
        var value = sheet!.GetCell(1, 3)!.Value.Should().BeOfType<NumberValue>().Subject;
        value.Value.Should().Be(40000,
            "the helper must not recalculate anything -- including volatile functions -- once a " +
            "workbook's own calculation mode is Manual");
    }

    private static async Task<Workbook> LoadAndApplyOnOpenAsync(string path)
    {
        var adapter = new XlsxFileAdapter();
        var format = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);
        var service = new WorkbookOpenService();
        var result = await service.LoadAsync(
            path,
            adapter,
            ".xlsx",
            format,
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        // Mirrors exactly what the WPF host's File > Open now does: rebuild the dependency graph,
        // then call the extracted helper directly -- never going through
        // WorkbookSessionFactory.Create/CreateOpened.
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        recalcEngine.RebuildFormulaDependencies(result.Workbook);
        WorkbookSessionFactory.ApplyOnOpenVolatileRecalc(recalcEngine, result.Workbook, new[] { adapter });

        return result.Workbook;
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

        return CreateXlsxPackage(parts);
    }

    private static byte[] CreateXlsxWithNamedRangeFormula(
        string cellFormula,
        string namedFormulaText,
        string cachedValue)
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
                  <definedNames>
                    <definedName name="SalesRange">{{namedFormulaText}}</definedName>
                  </definedNames>
                  <calcPr calcMode="auto"/>
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
                      <c r="C1"><f>{{cellFormula}}</f><v>{{cachedValue}}</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """),
        };

        return CreateXlsxPackage(parts);
    }

    private static byte[] CreateXlsxPackage((string Name, string Content)[] parts)
    {
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

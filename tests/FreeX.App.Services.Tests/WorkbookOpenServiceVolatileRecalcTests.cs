using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-29 findings R29-meta-1/R29-meta-2: round-28's fix for the selective on-open volatile
/// recalc (NOW/RAND/TODAY/OFFSET/INDIRECT/...) built its own throwaway RecalcEngine +
/// DependencyGraph inside <see cref="WorkbookOpenService"/> and pre-screened candidates with a
/// shallow regex over each cell's own FormulaText -- so a volatile function hidden behind a
/// defined name (e.g. <c>=SUM(SalesRange)</c> where SalesRange is itself defined as
/// <c>OFFSET(...)</c>) was invisible to that regex and never refreshed, and the dependency graph
/// was built TWICE per open (once throwaway here, once again by the session's own RecalcEngine in
/// <see cref="WorkbookSessionFactory.Create"/>).
///
/// The fix moves the selective recalc entirely into <see cref="WorkbookSessionFactory.Create"/>,
/// which runs it against the SAME dependency graph its own RecalcEngine.RebuildFormulaDependencies
/// just built -- that engine's CollectReferences recurses into a defined name's own formula text
/// (the NamedRangeNode case) and correctly propagates volatility from there, and there is no
/// second graph build. These tests therefore exercise the full open pipeline
/// (<see cref="WorkbookOpenService.LoadAsync"/> followed by
/// <see cref="WorkbookSessionFactory.CreateOpened"/>), not LoadAsync alone -- LoadAsync's
/// trusted-cache branch no longer performs any recalculation itself, which the last test below
/// pins directly.
/// </summary>
public sealed class WorkbookOpenServiceVolatileRecalcTests
{
    [Fact]
    public async Task Session_XlsxWithNowFormulaReevaluatesStaleVolatileValueOnOpen()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "now.xlsx");
        // A long-stale cached serial date (year ~2009); real Excel would never still show this
        // after opening the file today, because NOW() re-evaluates on every open.
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("NOW()", cachedValue: "40000"));

        var session = await OpenAsSessionAsync(tempPath);

        var sheet = session.Workbook.GetSheet("FormulaCases");
        // NOW() evaluates to a DateTimeValue (an OLE-Automation serial date, same underlying double
        // representation as a plain number), not a NumberValue.
        var value = sheet!.GetCell(1, 3)!.Value.Should().BeOfType<DateTimeValue>().Subject;
        value.Value.Should().BeGreaterThan(45000,
            "NOW() must re-evaluate to today's serial date once a session opens this workbook, " +
            "instead of keeping the file's stale cached value from years ago");
    }

    [Fact]
    public async Task Session_XlsxWithRandFormulaReevaluatesStaleVolatileValueOnOpen()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "rand.xlsx");
        // A cached value outside RAND()'s [0, 1) range proves the loaded value could not have come
        // from a real RAND() evaluation -- it must be the stale value from the last save.
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("RAND()", cachedValue: "5"));

        var session = await OpenAsSessionAsync(tempPath);

        var sheet = session.Workbook.GetSheet("FormulaCases");
        var value = sheet!.GetCell(1, 3)!.Value.Should().BeOfType<NumberValue>().Subject;
        value.Value.Should().BeInRange(0d, 1d,
            "RAND() must re-evaluate once a session opens this workbook; the stale cached 5 is " +
            "outside RAND()'s [0, 1) range");
    }

    [Fact]
    public async Task Session_XlsxWithVolatilityHiddenBehindDefinedNameRefreshesOnOpen()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "named-volatile.xlsx");
        // The cell's own formula text is "SUM(SalesRange)" -- it never mentions OFFSET/INDIRECT/etc.
        // itself. The volatility is hidden behind the defined name SalesRange, whose OWN refers-to
        // formula is OFFSET(FormulaCases!$A$1,0,0). A per-cell text scan of the formula can never
        // see this (R29-meta-1); only resolving the name and recursing into its formula text (as
        // RecalcEngine.CollectReferences' NamedRangeNode case does) can.
        await File.WriteAllBytesAsync(
            tempPath,
            CreateXlsxWithNamedRangeFormula(
                cellFormula: "SUM(SalesRange)",
                namedFormulaText: "OFFSET(FormulaCases!$A$1,0,0)",
                cachedValue: "999"));

        var session = await OpenAsSessionAsync(tempPath);

        var sheet = session.Workbook.GetSheet("FormulaCases");
        sheet!.GetCell(1, 3)!.Value.Should().Be(new NumberValue(2),
            "SalesRange's own definition (OFFSET(FormulaCases!$A$1,0,0)) is volatile, so " +
            "=SUM(SalesRange) must refresh on open even though its own formula text never " +
            "mentions OFFSET -- the stale cached 999 must not survive");
    }

    [Fact]
    public async Task Session_XlsxWithNonVolatileFormulaAndTrustedCacheKeepsStaleCachedValueUnrecalculated()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "non-volatile.xlsx");
        // SUM(A1:B1) with A1=2,B1=3 truly evaluates to 5; cache it as a deliberately WRONG 999 so a
        // survived 999 can only mean the trusted cache was left alone, not "recomputed to the same
        // answer by coincidence".
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("SUM(A1:B1)", cachedValue: "999"));

        var session = await OpenAsSessionAsync(tempPath);

        var sheet = session.Workbook.GetSheet("FormulaCases");
        sheet!.GetCell(1, 3)!.Value.Should().Be(new NumberValue(999),
            "the selective volatile-only recalc must leave a non-volatile formula's trusted cached " +
            "value completely untouched, even though it happens to be stale/wrong");
    }

    [Fact]
    public async Task Session_XlsxWithManualCalculationModeLeavesStaleVolatileValueUntouched()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "manual-now.xlsx");
        await File.WriteAllBytesAsync(
            tempPath,
            CreateXlsxWithFormula("NOW()", cachedValue: "40000", calcMode: "manual"));

        var session = await OpenAsSessionAsync(tempPath);

        var sheet = session.Workbook.GetSheet("FormulaCases");
        // Nothing recalculates in Manual mode, so the cell keeps whatever the loader read straight
        // from the file's cached <v> element (a plain number -- this minimal test file has no
        // number format attached, so it was never reclassified as a date/time value either).
        var value = sheet!.GetCell(1, 3)!.Value.Should().BeOfType<NumberValue>().Subject;
        value.Value.Should().Be(40000,
            "real Excel does not recalculate anything -- including volatile functions -- once a " +
            "workbook's own calculation mode is Manual; only an explicit F9/edit should");
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
    public async Task LoadAsync_AloneNoLongerRecalculatesVolatileCellsItself()
    {
        // Pins that WorkbookOpenService.LoadAsync's trusted-cache branch no longer builds its own
        // throwaway RecalcEngine/DependencyGraph to do this (R29-meta-2): without any session ever
        // opening the workbook, the volatile cell keeps the file's raw stale cached value exactly
        // as loaded. The tests above prove the session (WorkbookSessionFactory.Create) is what
        // performs the (correct, name-aware) selective recalc instead -- so the dependency graph is
        // built exactly once per open, by the session, not twice.
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "now-alone.xlsx");
        await File.WriteAllBytesAsync(tempPath, CreateXlsxWithFormula("NOW()", cachedValue: "40000"));
        var service = new WorkbookOpenService();

        var result = await service.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        var sheet = result.Workbook.GetSheet("FormulaCases");
        var value = sheet!.GetCell(1, 3)!.Value.Should().BeOfType<NumberValue>().Subject;
        value.Value.Should().Be(40000,
            "LoadAsync alone must leave even a volatile cell's stale cached value untouched now -- " +
            "the selective recalc happens only once, when a session opens this workbook");
    }

    private static async Task<WorkbookSession> OpenAsSessionAsync(string path)
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

        var target = new WorkbookOpenTarget(path, adapter, ".xlsx", format);
        return new WorkbookSessionFactory().CreateOpened(
            target,
            result,
            viewportHeight: 240,
            viewportWidth: 320);
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

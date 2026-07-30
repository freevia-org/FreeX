using FluentAssertions;
using FreeX.Core.IO;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R92-io-legacy-format-read-5-1/5-2/5-3: <see cref="LegacyXlsFileAdapter"/> silently dropped
/// formulas/charts/CF/DV/autofilter/defined-names on a .xlsb (or HSSF-parse-failure) fallback,
/// silently discarded a .xls VBA macro project, and silently dropped embedded legacy charts --
/// all with zero user-visible loss report. These tests exercise the real product entry point
/// (<see cref="LegacyXlsFileAdapter.LoadWithWarnings"/>, which <see cref="LegacyXlsFileAdapter.Load"/>
/// now delegates to) and assert the loss is surfaced in the returned <see cref="XlsxLoadResult.Warnings"/>
/// list -- the same list <c>WorkbookOpenService</c> already threads into
/// <c>MainWindow.Backstage.ShowXlsxLoadWarningsIfNeeded</c>, which is called unconditionally after
/// every open regardless of file extension.
/// </summary>
public sealed class R92_LegacyXlsFileAdapterLossReportTests
{
    [Fact]
    public void LoadWithWarnings_XlsbBinaryFallback_ReportsLegacyBinaryFallbackWarning()
    {
        // .xlsb is BIFF12/BRT, so NPOI's HSSFWorkbook (BIFF8-only) is guaranteed to throw here and
        // LegacyXlsFileAdapter must fall back to ExcelDataReader, which reads only computed values
        // (no formula text, no charts/CF/DV/autofilter/defined names).
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Simple.xlsb");
        using var stream = File.OpenRead(path);
        var adapter = new LegacyXlsFileAdapter();

        var result = adapter.LoadWithWarnings(stream);

        result.Workbook.Sheets.Should().NotBeEmpty();
        result.HasWarnings.Should().BeTrue();
        result.Warnings.Should().Contain(warning =>
            warning.Contains("legacy-binary-fallback", StringComparison.OrdinalIgnoreCase) &&
            warning.Contains("formula", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadWithWarnings_NormalXlsParsedByHssf_DoesNotReportLegacyBinaryFallbackWarning()
    {
        // No-regression sibling: when HSSF successfully parses the BIFF8 stream (the common case),
        // no fallback occurred, so the fallback warning must not fire.
        var bytes = BuildPlainXlsBytes();
        using var stream = new MemoryStream(bytes);
        var adapter = new LegacyXlsFileAdapter();

        var result = adapter.LoadWithWarnings(stream);

        result.Workbook.Sheets.Should().NotBeEmpty();
        result.Warnings.Should().NotContain(warning =>
            warning.Contains("legacy-binary-fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadWithWarnings_MacroEnabledXls_ReportsMacroProjectNotPreservedWarning()
    {
        var bytes = BuildXlsBytesWithVbaProject();
        using var stream = new MemoryStream(bytes);
        var adapter = new LegacyXlsFileAdapter();

        var result = adapter.LoadWithWarnings(stream);

        result.Workbook.HasVbaProjectPackage.Should().BeTrue();
        result.Warnings.Should().Contain(warning =>
            warning.Contains("[macros]", StringComparison.Ordinal) &&
            warning.Contains("VBA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadWithWarnings_NonMacroXls_DoesNotReportMacroProjectNotPreservedWarning()
    {
        // No-regression sibling: a workbook without a VBA project must not get a false-positive
        // macro-loss warning.
        var bytes = BuildPlainXlsBytes();
        using var stream = new MemoryStream(bytes);
        var adapter = new LegacyXlsFileAdapter();

        var result = adapter.LoadWithWarnings(stream);

        result.Workbook.HasVbaProjectPackage.Should().BeFalse();
        result.Warnings.Should().NotContain(warning => warning.Contains("[macros]", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadWithWarnings_XlsWithEmbeddedChartDefinedName_ReportsChartLossWarning()
    {
        // Legacy BIFF charts are anchored via internal "_xlchart.N" defined names (the only
        // chart-adjacent code in LegacyXlsFileAdapter, IsExcelReservedDefinedName, exists purely to
        // hide them from the user-visible Name Manager) -- simulate that anchor without needing a
        // full chart sub-stream, matching how the production loss-detection reads it.
        var bytes = BuildXlsBytesWithChartDefinedName();
        using var stream = new MemoryStream(bytes);
        var adapter = new LegacyXlsFileAdapter();

        var result = adapter.LoadWithWarnings(stream);

        result.Warnings.Should().Contain(warning =>
            warning.Contains("[charts]", StringComparison.Ordinal) &&
            warning.Contains("1 embedded chart", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadWithWarnings_XlsWithOrdinaryDefinedName_DoesNotReportChartLossWarningAndKeepsName()
    {
        // No-regression sibling: an ordinary (non-chart) defined name must still load normally and
        // must not trigger a false-positive chart-loss warning.
        var bytes = BuildXlsBytesWithNormalDefinedName();
        using var stream = new MemoryStream(bytes);
        var adapter = new LegacyXlsFileAdapter();

        var result = adapter.LoadWithWarnings(stream);

        result.Warnings.Should().NotContain(warning => warning.Contains("[charts]", StringComparison.Ordinal));
        result.Workbook.NamedRanges.Should().ContainKey("MyRange");
    }

    [Fact]
    public void Load_StillDelegatesToLoadWithWarningsAndReturnsWorkbook()
    {
        // Load(Stream) is the IFileAdapter member every caller outside WorkbookOpenService uses;
        // confirm it still returns a usable workbook after being rebased onto LoadWithWarnings.
        var bytes = BuildPlainXlsBytes();
        using var stream = new MemoryStream(bytes);
        var adapter = new LegacyXlsFileAdapter();

        var workbook = adapter.Load(stream);

        workbook.Sheets.Should().NotBeEmpty();
    }

    private static byte[] BuildPlainXlsBytes()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Sheet1");
        sheet.CreateRow(0).CreateCell(0).SetCellValue(42);

        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }

    private static byte[] BuildXlsBytesWithVbaProject()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Sheet1");
        sheet.CreateRow(0).CreateCell(0).SetCellValue(42);

        using var baseStream = new MemoryStream();
        hssf.Write(baseStream, leaveOpen: true);
        baseStream.Position = 0;

        var poifs = new POIFSFileSystem(baseStream);
        using var vbaBytes = new MemoryStream([0x01, 0x02, 0x03, 0x04]);
        poifs.Root.CreateDocument("_VBA_PROJECT_CUR", vbaBytes);

        using var outStream = new MemoryStream();
        poifs.WriteFileSystem(outStream);
        return outStream.ToArray();
    }

    private static byte[] BuildXlsBytesWithChartDefinedName()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Sheet1");
        sheet.CreateRow(0).CreateCell(0).SetCellValue(1);
        sheet.CreateRow(1).CreateCell(0).SetCellValue(2);

        var chartName = hssf.CreateName();
        chartName.NameName = "_xlchart.1";
        chartName.RefersToFormula = "Sheet1!$A$1:$A$2";

        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }

    private static byte[] BuildXlsBytesWithNormalDefinedName()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Sheet1");
        sheet.CreateRow(0).CreateCell(0).SetCellValue(1);

        var normalName = hssf.CreateName();
        normalName.NameName = "MyRange";
        normalName.RefersToFormula = "Sheet1!$A$1";

        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }
}

using FluentAssertions;
using FreeX.Core.IO;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R92-io-legacy-format-read-5-1/5-2: verifies the actual product entry point,
/// <see cref="WorkbookOpenService.LoadAsync"/>, threads a real <see cref="LegacyXlsFileAdapter"/>'s
/// loss warnings into <see cref="WorkbookOpenResult.LoadWarnings"/> -- the same field
/// MainWindow.Backstage.ShowXlsxLoadWarningsIfNeeded displays unconditionally after every open,
/// regardless of extension. Before this round's fix, <c>WorkbookOpenService</c> only special-cased
/// <c>XlsxFileAdapter</c> for warnings and called the plain <c>IFileAdapter.Load</c> for every other
/// adapter (including <see cref="LegacyXlsFileAdapter"/>), so <see cref="WorkbookOpenResult.LoadWarnings"/>
/// stayed empty for every legacy .xls/.xlsb open no matter what was lost.
/// </summary>
public sealed class R92_WorkbookOpenServiceLegacyXlsWarningsTests
{
    [Fact]
    public async Task LoadAsync_MacroEnabledXls_PopulatesLoadWarningsThroughRealLegacyAdapter()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "macro-workbook.xls");
        await File.WriteAllBytesAsync(path, BuildXlsBytesWithVbaProject());

        var service = new WorkbookOpenService();
        var adapter = new LegacyXlsFileAdapter();

        var result = await service.LoadAsync(
            path,
            adapter,
            ".xls",
            new FileFormatDescriptor(".xls", "XLS 97-2003 Workbook"));

        result.Workbook.HasVbaProjectPackage.Should().BeTrue();
        result.FeatureReport.Should().BeNull(); // no XlsxFeatureReport gate exists for .xls
        result.LoadWarnings.Should().Contain(warning => warning.Contains("[macros]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_PlainXls_LeavesLoadWarningsEmptyThroughRealLegacyAdapter()
    {
        // No-regression sibling: a plain .xls with no lossy legacy features must open through the
        // same real service + real adapter without spuriously reporting a warning.
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "plain-workbook.xls");
        await File.WriteAllBytesAsync(path, BuildPlainXlsBytes());

        var service = new WorkbookOpenService();
        var adapter = new LegacyXlsFileAdapter();

        var result = await service.LoadAsync(
            path,
            adapter,
            ".xls",
            new FileFormatDescriptor(".xls", "XLS 97-2003 Workbook"));

        result.Workbook.HasVbaProjectPackage.Should().BeFalse();
        result.LoadWarnings.Should().BeEmpty();
    }

    private static byte[] BuildPlainXlsBytes()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Sheet1");
        sheet.CreateRow(0).CreateCell(0).SetCellValue(1);

        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }

    private static byte[] BuildXlsBytesWithVbaProject()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Sheet1");
        sheet.CreateRow(0).CreateCell(0).SetCellValue(1);

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
}

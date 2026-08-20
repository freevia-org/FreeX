using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// freex-data-model-limits F1: every format that falls into WorkbookOpenService.LoadAsync's generic
/// "adapter.Load(fileStream)" fallback (CSV/TSV, PRN, SLK, DIF, DBF, ...) had no way to tell the user
/// that rows/columns past CellAddress.MaxRow/MaxCol were silently dropped -- only the XlsxFileAdapter
/// and LegacyXlsFileAdapter branches populated LoadWarnings. TestFileAdapter implements plain
/// IFileAdapter (not XlsxFileAdapter/LegacyXlsFileAdapter), so it exercises that exact fallback branch
/// the same way a real CSV/PRN/SLK/DIF/DBF adapter would.
/// </summary>
public sealed class WorkbookOpenServiceGridLimitWarningsTests
{
    [Fact]
    public async Task LoadAsync_FallbackAdapterWorkbookReachingMaxRow_YieldsGridLimitWarning()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "oversized.csv");
        await File.WriteAllTextAsync(tempPath, "payload");
        var adapter = new TestFileAdapter(_ =>
        {
            // Mirrors what DelimitedTextWorkbookReader.Load (and PrnFileAdapter/DbfFileAdapter/
            // SlkFileAdapter/DifFileAdapter) actually produce for a source file with more than
            // CellAddress.MaxRow rows: data written all the way up to and including the last row
            // the grid supports, with nothing past it.
            var workbook = new Workbook("Loaded");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, CellAddress.MaxRow, 1), new TextValue("last"));
            return workbook;
        }, extension: ".csv", formatName: "CSV");

        var result = await new WorkbookOpenService().LoadAsync(
            tempPath,
            adapter,
            ".csv",
            new FileFormatDescriptor(".csv", "CSV"));

        result.LoadWarnings.Should().ContainSingle(
            w => w.Contains("grid-limit", StringComparison.OrdinalIgnoreCase) &&
                 w.Contains("Sheet1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_FallbackAdapterWorkbookReachingMaxCol_YieldsGridLimitWarning()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "oversized-wide.csv");
        await File.WriteAllTextAsync(tempPath, "payload");
        var adapter = new TestFileAdapter(_ =>
        {
            var workbook = new Workbook("Loaded");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol), new TextValue("last"));
            return workbook;
        }, extension: ".csv", formatName: "CSV");

        var result = await new WorkbookOpenService().LoadAsync(
            tempPath,
            adapter,
            ".csv",
            new FileFormatDescriptor(".csv", "CSV"));

        result.LoadWarnings.Should().ContainSingle(
            w => w.Contains("grid-limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadAsync_FallbackAdapterWorkbookWellWithinLimits_NoRegressionNoWarnings()
    {
        // Sibling/no-regression case: an ordinary small CSV-shaped workbook, nowhere near the grid
        // limit, must keep opening exactly as before -- silently and with an empty LoadWarnings list.
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "normal.csv");
        await File.WriteAllTextAsync(tempPath, "payload");
        var adapter = new TestFileAdapter(_ =>
        {
            var workbook = new Workbook("Loaded");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            sheet.SetCell(new CellAddress(sheet.Id, 10, 3), new TextValue("J3"));
            return workbook;
        }, extension: ".csv", formatName: "CSV");

        var result = await new WorkbookOpenService().LoadAsync(
            tempPath,
            adapter,
            ".csv",
            new FileFormatDescriptor(".csv", "CSV"));

        result.LoadWarnings.Should().BeEmpty();
    }
}

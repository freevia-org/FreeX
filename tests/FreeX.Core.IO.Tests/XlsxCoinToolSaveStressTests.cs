using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit.Sdk;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxCoinToolSaveStressTests
{
    private const string CoinToolEnvironmentVariable = "FREEX_COIN_TOOL_WORKBOOK_PATH";
    private const string CoinToolManifestId = "public-coin-tool-exampledata-001";
    private const string CoinToolRelativePath = @"test-corpus\public\COIN_Tool_v1_FULL_exampledata.xlsm";
    private static readonly TimeSpan SourcePatchSaveBudget = TimeSpan.FromSeconds(30);

    [BenchmarkFact]
    [Trait("Category", "ExternalWorkbook")]
    public void CoinToolWorkbook_ExistingLiteralCellEdit_CompletesViaSourcePatchSave()
    {
        var sourcePath = ResolveCoinToolWorkbookPath()
            ?? throw SkipException.ForSkip(
                $"{CoinToolManifestId} save stress requires {CoinToolEnvironmentVariable} or {CoinToolRelativePath}.");

        var sourceInfo = new FileInfo(sourcePath);
        sourceInfo.Length.Should().BeGreaterThan(20_000_000, CoinToolManifestId);

        var adapter = new XlsxFileAdapter();
        var loadStopwatch = Stopwatch.StartNew();
        XlsxLoadResult loadResult;
        using (var source = File.OpenRead(sourcePath))
            loadResult = adapter.LoadWithWarnings(source, inspectFeatures: true);
        loadStopwatch.Stop();

        var workbook = loadResult.Workbook;
        workbook.SheetCount.Should().BeGreaterThan(0, CoinToolManifestId);
        workbook.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(1_000_000, CoinToolManifestId);
        Console.WriteLine(
            "ISSUE127_COIN_LOAD " +
            $"manifest_id={CoinToolManifestId} bytes={sourceInfo.Length} sheets={workbook.SheetCount} " +
            $"cells={workbook.Sheets.Sum(sheet => sheet.CellCount)} warnings={loadResult.Warnings.Count} " +
            $"unsupported_features={loadResult.FeatureReport?.Features.Count ?? 0} elapsed_ms={loadStopwatch.Elapsed.TotalMilliseconds:F2}");

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var prepareReason)
            .Should()
            .BeTrue(prepareReason);

        var (sheet, address) = FindExistingTextCell(workbook)
            ?? throw new InvalidOperationException($"{CoinToolManifestId} did not contain an editable literal text cell.");
        sheet.SetCell(address, new TextValue("issue127-source-patch-save"));

        using var temp = new TestTemporaryDirectory();
        var outputPath = Path.Combine(temp.Path, "COIN_Tool_v1_FULL_exampledata.issue127.xlsm");
        var saveStopwatch = Stopwatch.StartNew();
        using (var output = File.Create(outputPath))
            adapter.Save(workbook, output);
        saveStopwatch.Stop();

        var outputInfo = new FileInfo(outputPath);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        outputInfo.Length.Should().BeGreaterThan(
            sourceInfo.Length / 2,
            $"{CoinToolManifestId} should remain a large macro-enabled package after a one-cell source patch");
        saveStopwatch.Elapsed.Should().BeLessThan(SourcePatchSaveBudget);
        Console.WriteLine(
            "ISSUE127_COIN_SAVE " +
            $"manifest_id={CoinToolManifestId} edit={sheet.Name}!{address} output_bytes={outputInfo.Length} " +
            $"elapsed_ms={saveStopwatch.Elapsed.TotalMilliseconds:F2} save_path={adapter.LastSaveDiagnostics.PathLabel} " +
            $"save_reason={adapter.LastSaveDiagnostics.Reason}");
    }

    private static string? ResolveCoinToolWorkbookPath()
    {
        var configured = Environment.GetEnvironmentVariable(CoinToolEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured))
                return configured;

            throw new FileNotFoundException(
                $"{CoinToolEnvironmentVariable} does not point to an existing workbook.",
                configured);
        }

        var workspacePath = TestWorkspaceFiles.FindRepoFile(
            "test-corpus",
            "public",
            "COIN_Tool_v1_FULL_exampledata.xlsm");
        return File.Exists(workspacePath) ? workspacePath : null;
    }

    private static (Sheet Sheet, CellAddress Address)? FindExistingTextCell(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell is { HasFormula: false, Value: TextValue })
                    return (sheet, address);
            }
        }

        return null;
    }
}

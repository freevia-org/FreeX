using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R128-avalonia-lossy-format-feature-loss-confirm
/// (src/FreeX.App.Avalonia/MainWindow.cs, SaveWorkbookToTargetAsync). The WPF host gates every
/// Save-As to a plain/single-sheet lossy format (.csv/.txt/.prn/.slk/.dif/.dbf/.tab/.tsv) through
/// <see cref="LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation"/> +
/// ConfirmLossyFormatFeatureLossSave (src/FreeX.App.Host/MainWindow.Backstage.cs), which pops a
/// "Possible Data Loss" Yes/No dialog before writing whenever the workbook has more than one sheet or
/// any chart. The Avalonia shell's equivalent save path never called the planner at all -- it went
/// straight from the file picker to the save service with no gate, silently dropping every sheet but
/// the current one (and any charts) when saving a multi-sheet/chart-bearing workbook to CSV.
///
/// These tests drive the REAL production entry points directly via the internal test seams
/// <c>OpenWorkbookFromTargetAsyncForTest</c>/<c>SaveWorkbookToTargetAsyncForTest</c> (mirroring R116's
/// convention) -- the source fixture is produced by <see cref="XlsxFileAdapter"/> itself (our own
/// writer), never hand-authored XML/CSV, so the open/save round-trip through the real
/// <see cref="CsvFileAdapter"/> is real.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R128_LossyFormatFeatureLossConfirmTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SaveWorkbookToTargetAsync_MultiSheetWorkbookSavedAsCsv_UserDeclines_LeavesDiskUntouched()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new TestTemporaryDirectory("R128-");
            var xlsxPath = Path.Combine(tempDir.Path, "Book1.xlsx");
            var csvPath = Path.Combine(tempDir.Path, "Book1.csv");

            var xlsxAdapter = new XlsxFileAdapter();
            var xlsxFormat = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);

            // A real multi-sheet workbook, written through our own writer -- saving this as CSV can
            // only ever keep one sheet.
            WriteMultiSheetWorkbook(xlsxAdapter, xlsxPath);

            var window = new MainWindow([]);
            try
            {
                var openTarget = new WorkbookOpenTarget(xlsxPath, xlsxAdapter, ".xlsx", xlsxFormat);
                await window.OpenWorkbookFromTargetAsyncForTest(openTarget);

                var confirmPromptShown = false;
                var confirmedExtension = string.Empty;
                window.LossyFormatFeatureLossConfirmOverrideForTest = ext =>
                {
                    confirmPromptShown = true;
                    confirmedExtension = ext;
                    return UserMessageResult.No;
                };

                var csvAdapter = new CsvFileAdapter();
                var saveTarget = new FileSaveTarget(csvPath, csvAdapter);
                var result = await window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                result.Should().BeFalse(
                    "Save must refuse to proceed once the user declines the data-loss confirm prompt");
                confirmPromptShown.Should().BeTrue(
                    "a multi-sheet workbook saved to a plain/single-sheet format (CSV) must trigger " +
                    "the feature-loss confirm prompt -- before the fix, SaveWorkbookToTargetAsync never " +
                    "called LossyFormatFeatureLossPlanner at all, so this prompt never fired");
                confirmedExtension.Should().Be(".csv");
                File.Exists(csvPath).Should().BeFalse(
                    "before the fix, the save proceeded unconditionally with no gate, so declining the " +
                    "(never-shown) prompt could not stop the write -- the file must not exist on disk " +
                    "once the user has declined");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            // IMPORTANT: HeadlessUnitTestSession.Dispatch's Func<Task> (non-generic) overload does
            // NOT propagate an exception/assertion failure thrown inside the delegate back to the
            // awaiting xUnit test -- it is silently swallowed and the test reports Passed regardless
            // of what happened inside. Only the Func<Task<T>> overload propagates correctly. This
            // return makes the compiler pick that overload; do not remove it.
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SaveWorkbookToTargetAsync_SingleSheetWorkbookNoChartsSavedAsCsv_SavesNormally_NoConfirmPrompt()
    {
        // Sibling no-regression case: a single-sheet workbook with no charts loses nothing when saved
        // as CSV, so the ordinary Save-As must keep working exactly as before this fix -- no confirm
        // prompt, and the content actually lands on disk.
        await Session.Dispatch(async () =>
        {
            using var tempDir = new TestTemporaryDirectory("R128-");
            var xlsxPath = Path.Combine(tempDir.Path, "Book1.xlsx");
            var csvPath = Path.Combine(tempDir.Path, "Book1.csv");

            var xlsxAdapter = new XlsxFileAdapter();
            var xlsxFormat = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);

            WriteSingleSheetWorkbook(xlsxAdapter, xlsxPath);

            var window = new MainWindow([]);
            try
            {
                var openTarget = new WorkbookOpenTarget(xlsxPath, xlsxAdapter, ".xlsx", xlsxFormat);
                await window.OpenWorkbookFromTargetAsyncForTest(openTarget);

                var confirmPromptShown = false;
                window.LossyFormatFeatureLossConfirmOverrideForTest = _ =>
                {
                    confirmPromptShown = true;
                    return UserMessageResult.No;
                };

                var csvAdapter = new CsvFileAdapter();
                var saveTarget = new FileSaveTarget(csvPath, csvAdapter);
                var result = await window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                result.Should().BeTrue(
                    "a single-sheet workbook with no charts loses nothing when saved as CSV, so the " +
                    "save must succeed exactly as before this fix");
                confirmPromptShown.Should().BeFalse(
                    "the feature-loss confirm prompt must only fire when the workbook actually has " +
                    "content the target format can't hold");
                File.Exists(csvPath).Should().BeTrue("the save must actually write the file");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void WriteMultiSheetWorkbook(XlsxFileAdapter adapter, string path)
    {
        var workbook = WorkbookFactory.Create();
        var sheet1 = workbook.GetSheet("Sheet1")!;
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("sheet-1-value"));
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("sheet-2-value"));
        using var stream = File.Create(path);
        adapter.Save(workbook, stream);
    }

    private static void WriteSingleSheetWorkbook(XlsxFileAdapter adapter, string path)
    {
        var workbook = WorkbookFactory.Create();
        var sheet = workbook.GetSheet("Sheet1")!;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        using var stream = File.Create(path);
        adapter.Save(workbook, stream);
    }

}

using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R134-io-getdata-refresh-shrink-wpf (src/FreeX.App.Host/MainWindow.DataCommands.cs,
/// the WPF-shell half of the round-134 fix). The original round-134 fix only patched the Avalonia shell's
/// Get Data refresh path (MainWindow.GetData.cs / <c>ImportDataSource</c> / <c>RefreshImportedData</c>) --
/// the WPF host's <c>ImportDataFromFileAsync</c> (invoked from <c>GetDataBtn_Click</c>) always built a
/// plain 3-arg <see cref="ImportSheetCommand"/> with no remembered extent, so leftover cells from a
/// larger, earlier import into the same destination survived a second, smaller import into that same
/// cell -- exactly the bug round 134 fixed for Avalonia, left open in WPF.
///
/// Entry point under test: <c>MainWindow.ImportDataFromFileAsync</c> (private, invoked via reflection
/// below), which is what <c>GetDataBtn_Click</c> calls after the user picks a file. Refresh All now
/// delegates to the same core import path with the remembered source and original anchor; this test
/// keeps direct coverage of repeated Get Data imports into that anchor.
/// </summary>
public sealed class R134_GetDataImportShrinkClearsLeftoverCellsWpfTests
{
    [Fact]
    public void ImportDataFromFileAsync_ReimportSmallerSourceToSameDestination_ClearsLeftoverCells()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ShrinkHarness.Create();

            harness.SetSelection(1, 1); // destination A1

            // First import: 10 rows x 5 columns of data at A1.
            var firstFile = harness.CreateTempImportFile();
            try
            {
                harness.RunImport(firstFile, ShrinkHarness.MakeGridAdapter(rowCount: 10, colCount: 5));
            }
            finally
            {
                File.Delete(firstFile);
            }

            var sheet = harness.OriginalWorkbook.GetSheet(harness.OriginalSheetId)!;

            // Sanity: the first import actually wrote the full 10x5 block.
            sheet.GetValue(10, 5).Should().Be(new NumberValue(50), "the first (larger) import must have written its full extent");

            // Second import, same destination (A1), SHRUNK source: 6 rows x 3 columns.
            harness.SetSelection(1, 1);
            var secondFile = harness.CreateTempImportFile();
            try
            {
                harness.RunImport(secondFile, ShrinkHarness.MakeGridAdapter(rowCount: 6, colCount: 3));
            }
            finally
            {
                File.Delete(secondFile);
            }

            // The new, smaller import's own cells must be present and correct.
            sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
            sheet.GetValue(6, 3).Should().Be(new NumberValue(18));

            // Rows 7-10 (below the new 6-row extent, within the old 5-column width) must be cleared --
            // this is the round-134 bug: without the previousExtent plumbing, these kept the FIRST
            // import's stale values (11..50) forever.
            for (var row = 7u; row <= 10u; row++)
            {
                for (var col = 1u; col <= 5u; col++)
                {
                    sheet.GetCell(new CellAddress(harness.OriginalSheetId, row, col)).Should().BeNull(
                        $"row {row} col {col} is outside the shrunk import's extent and must be cleared, not left over from the larger first import");
                }
            }

            // Columns 4-5 (to the right of the new 3-column width, within the new 6-row extent) must
            // also be cleared for the same reason.
            for (var row = 1u; row <= 6u; row++)
            {
                for (var col = 4u; col <= 5u; col++)
                {
                    sheet.GetCell(new CellAddress(harness.OriginalSheetId, row, col)).Should().BeNull(
                        $"row {row} col {col} is outside the shrunk import's extent and must be cleared, not left over from the larger first import");
                }
            }
        });
    }

    [Fact]
    public void ImportDataFromFileAsync_ReimportToDifferentDestination_DoesNotClearUnrelatedCells()
    {
        // Sibling/no-regression case: a second import to a DIFFERENT destination cell must not be
        // treated as a refresh of the first one -- nothing at the first destination should be touched.
        StaTestRunner.Run(() =>
        {
            using var harness = ShrinkHarness.Create();

            harness.SetSelection(1, 1); // destination A1
            var firstFile = harness.CreateTempImportFile();
            try
            {
                harness.RunImport(firstFile, ShrinkHarness.MakeGridAdapter(rowCount: 4, colCount: 4));
            }
            finally
            {
                File.Delete(firstFile);
            }

            var sheet = harness.OriginalWorkbook.GetSheet(harness.OriginalSheetId)!;
            sheet.GetValue(4, 4).Should().Be(new NumberValue(16));

            harness.SetSelection(10, 10); // a completely different destination (J10)
            var secondFile = harness.CreateTempImportFile();
            try
            {
                harness.RunImport(secondFile, ShrinkHarness.MakeGridAdapter(rowCount: 2, colCount: 2));
            }
            finally
            {
                File.Delete(secondFile);
            }

            // The first import's block at A1:D4 must be completely untouched.
            sheet.GetValue(4, 4).Should().Be(new NumberValue(16), "a different destination must not clear the earlier import's cells");
            sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        });
    }

    private sealed class ShrinkHarness : IDisposable
    {
        private readonly MethodInfo _importDataFromFileAsync;

        private ShrinkHarness(MainWindow window, Workbook originalWorkbook)
        {
            Window = window;
            OriginalWorkbook = originalWorkbook;
            OriginalSheetId = originalWorkbook.Sheets[0].Id;

            _importDataFromFileAsync = typeof(MainWindow)
                .GetMethod("ImportDataFromFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ImportDataFromFileAsync");
        }

        public MainWindow Window { get; }

        public Workbook OriginalWorkbook { get; }

        public SheetId OriginalSheetId { get; }

        public void SetSelection(uint row, uint col)
        {
            var address = new CellAddress(OriginalSheetId, row, col);
            Window.SheetGrid.SelectedRange = new GridRange(address, address);
        }

        public string CreateTempImportFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"freex-r134-getdata-shrink-{Guid.NewGuid():N}.testimport");
            File.WriteAllText(path, "placeholder");
            return path;
        }

        /// <summary>
        /// Builds a fake adapter whose Load() returns a workbook with one sheet, populated with
        /// sequential NumberValue cells 1..(rowCount*colCount) in row-major order -- e.g.
        /// MakeGridAdapter(10, 5) writes row 1 = 1..5, row 2 = 6..10, ..., row 10 = 46..50.
        /// </summary>
        public static TestFileAdapter MakeGridAdapter(int rowCount, int colCount) =>
            new(load: _ =>
            {
                var workbook = new Workbook("Imported");
                var sheet = workbook.AddSheet("Data");
                var value = 1;
                for (var row = 1u; row <= rowCount; row++)
                {
                    for (var col = 1u; col <= colCount; col++)
                    {
                        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));
                        value++;
                    }
                }

                return workbook;
            });

        public void RunImport(string importPath, IFileAdapter adapter)
        {
            var task = (Task)_importDataFromFileAsync.Invoke(
                Window,
                [importPath, adapter, ".testimport", (FileFormatDescriptor?)null])!;

            // Pump the dispatcher while waiting: the awaited Task.Run inside ImportDataFromFileAsync
            // resumes via the DispatcherSynchronizationContext installed on this STA thread, so a
            // nested frame (rather than a blocking .Wait()/.Result) is required to let it continue.
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);

            if (task.IsFaulted)
                throw task.Exception!.GetBaseException();
        }

        public static ShrinkHarness Create()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");

            var workbooksById = new Dictionary<WorkbookId, Workbook> { [initialWorkbook.Id] = initialWorkbook };
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(id => workbooksById.TryGetValue(id, out var wb)
                    ? new TestCommandContext(wb)
                    : throw new KeyNotFoundException($"No test workbook registered for {id}")),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (MainWindow.Startup.cs) replaces the constructor's workbook with a
            // brand-new default one unless adopting a shared document, so the live workbook is
            // whatever workbookRef.Current now points to (mirrors R68_GetDataImportOrderingRaceTests).
            var originalWorkbook = workbookRef.Current;
            workbooksById[originalWorkbook.Id] = originalWorkbook;

            return new ShrinkHarness(window, originalWorkbook);
        }

        public void Dispose()
        {
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// No-op <see cref="IUserMessageService"/> for tests that construct <see cref="MainWindow"/> directly
    /// and don't want real WPF MessageBox windows popping up.
    /// </summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Ok;
    }
}

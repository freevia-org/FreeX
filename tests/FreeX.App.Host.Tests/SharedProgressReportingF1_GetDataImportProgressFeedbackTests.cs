using System.IO;
using System.Reflection;
using System.Windows;
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
/// Regression coverage for shared-progress-reporting finding F1 (src/FreeX.App.Host/MainWindow.DataCommands.cs,
/// <c>ImportDataFromFileAtDestinationAsync</c>). Before the fix, Get Data / Refresh All set
/// <c>RootGrid.IsEnabled = false</c> for the whole import and then awaited
/// <c>WorkbookImportWorkflow.ImportPathAsync</c> (adapter.Load on a background thread) with zero status-bar
/// text, no progress bar, and no busy cursor -- the window simply went inert. This test proves the footer
/// progress panel (the same <c>StatusSaveProgressPanel</c>/<c>ShowSaveProgress</c>/<c>HideSaveProgress</c>
/// plumbing ExportAsPdf/ExportAsXps already use for their own background work) is now shown for the
/// duration of the import and hidden again once it completes.
///
/// Entry point under test: <c>MainWindow.ImportDataFromFileAsync</c> (private, invoked via reflection
/// below), which is what <c>GetDataBtn_Click</c> calls after the user picks a file, and which
/// <c>RefreshAllBtn_Click</c> also funnels through (via <c>ImportDataFromFileAtDestinationAsync</c>) --
/// so both gestures the finding names inherit the same fix from this one shared method.
/// </summary>
public sealed class SharedProgressReportingF1_GetDataImportProgressFeedbackTests
{
    [Fact]
    public void ImportDataFromFileAsync_WhileImportIsInFlight_ShowsFooterProgressWithFileName_ThenHidesOnCompletion()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ImportProgressHarness.Create();

            harness.SetSelection(1, 1);
            var importFile = harness.CreateTempImportFile();
            try
            {
                var task = harness.BeginImport(importFile, ImportProgressHarness.MakeGridAdapter(rowCount: 2, colCount: 2));

                // The async method runs synchronously up to its first real await (the Task.Run inside
                // WorkbookImportWorkflow.ImportPathAsync), so by the time Invoke() returns above, the
                // ShowSaveProgress call and the RootGrid.IsEnabled = false guard have both already run,
                // and the background load is merely queued -- its continuation cannot resume on this
                // thread until we pump the dispatcher below. This makes the "in flight" assertions
                // deterministic rather than a timing-dependent race.
                harness.Window.RootGrid.IsEnabled.Should().BeFalse(
                    "input must still be blocked for the duration of the import (unchanged behavior)");
                harness.Window.StatusSaveProgressPanel.Visibility.Should().Be(Visibility.Visible,
                    "the footer progress panel must appear for the duration of the import -- this is " +
                    "the finding: previously nothing showed any progress feedback at all");
                harness.Window.StatusSaveProgressText.Text.Should().Contain(
                    Path.GetFileName(importFile),
                    "the progress text should name the file being imported, matching Open/Save/Export");

                harness.PumpUntilComplete(task);
            }
            finally
            {
                File.Delete(importFile);
            }

            harness.Window.StatusSaveProgressPanel.Visibility.Should().Be(Visibility.Collapsed,
                "the footer progress panel must be hidden again once the import finishes");
            harness.Window.RootGrid.IsEnabled.Should().BeTrue(
                "input must be re-enabled once the import finishes");
        });
    }

    /// <summary>
    /// Sibling no-regression: the new progress-footer calls must not disturb the actual import result --
    /// the imported cells still land correctly and the panel does not stay stuck open on the happy path.
    /// </summary>
    [Fact]
    public void ImportDataFromFileAsync_StillImportsDataCorrectly_AlongsideTheNewProgressFeedback()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ImportProgressHarness.Create();

            harness.SetSelection(1, 1);
            var importFile = harness.CreateTempImportFile();
            try
            {
                var task = harness.BeginImport(importFile, ImportProgressHarness.MakeGridAdapter(rowCount: 3, colCount: 2));
                harness.PumpUntilComplete(task);
            }
            finally
            {
                File.Delete(importFile);
            }

            var sheet = harness.OriginalWorkbook.GetSheet(harness.OriginalSheetId)!;
            sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
            sheet.GetValue(3, 2).Should().Be(new NumberValue(6));
        });
    }

    private sealed class ImportProgressHarness : IDisposable
    {
        private readonly MethodInfo _importDataFromFileAsync;

        private ImportProgressHarness(MainWindow window, Workbook originalWorkbook)
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
            var path = Path.Combine(Path.GetTempPath(), $"freex-progressreporting-f1-getdata-{Guid.NewGuid():N}.testimport");
            File.WriteAllText(path, "placeholder");
            return path;
        }

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

        /// <summary>
        /// Starts the import and returns the in-flight Task WITHOUT pumping the dispatcher, so the
        /// caller can inspect UI state exactly as it stands right after the method's synchronous prefix
        /// has run (see the comment at the call site above).
        /// </summary>
        public Task BeginImport(string importPath, IFileAdapter adapter) =>
            (Task)_importDataFromFileAsync.Invoke(
                Window,
                [importPath, adapter, ".testimport", (FileFormatDescriptor?)null])!;

        public void PumpUntilComplete(Task task)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);

            if (task.IsFaulted)
                throw task.Exception!.GetBaseException();
        }

        public static ImportProgressHarness Create()
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

            var originalWorkbook = workbookRef.Current;
            workbooksById[originalWorkbook.Id] = originalWorkbook;

            return new ImportProgressHarness(window, originalWorkbook);
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

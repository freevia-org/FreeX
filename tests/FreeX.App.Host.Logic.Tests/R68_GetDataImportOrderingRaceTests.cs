using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R68-async-ordering-race-sweep-2 (src/FreeX.App.Host/MainWindow.DataCommands.cs,
/// GetDataBtn_Click's extracted ImportDataFromFileAsync helper).
///
/// Before the fix: the background import (<c>await Task.Run(() => adapter.Load(stream))</c>) was
/// unguarded, and the follow-up executed <c>TryExecuteCommand(new ImportSheetCommand(_currentSheetId,
/// destination, ...), ...)</c>, which always reads the CURRENT <c>_workbook.Id</c>/<c>_currentSheetId</c>
/// at the moment it runs. A concurrent File &gt; Open completing while the import's background load was
/// still in flight would swap the window onto a different workbook before the import resumed, so the
/// imported data would silently land in the WRONG (newly opened) workbook instead of the one Get Data
/// was invoked on.
///
/// After the fix, the target workbook/sheet/destination are captured before the await and the
/// ImportSheetCommand is executed through the captured <see cref="WorkbookSession"/>, so the import
/// always lands in the workbook it was invoked on. Input is
/// also blocked for the duration (RootGrid.IsEnabled = false, mirroring ExportAsPdf) so File &gt; Open is
/// unreachable via the ribbon while the import is in flight -- matching Excel's modal Get Data.
/// </summary>
public sealed class R68_GetDataImportOrderingRaceTests
{
    [Fact]
    public void ImportDataFromFileAsync_ConcurrentOpenMidImport_LandsInOriginalWorkbookNotTheNewOne()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ImportRaceHarness.Create();

            Workbook? swappedInWorkbook = null;
            SheetId? swappedInSheetId = null;

            harness.SetSelection(1, 1); // destination A1 on the ORIGINAL workbook/sheet
            var tempFile = harness.CreateTempImportFile();
            try
            {
                var adapter = new TestFileAdapter(load: _ =>
                {
                    // Simulate a concurrent File > Open landing WHILE this background Load() is
                    // still running: swap the window onto a brand-new workbook before the import's
                    // await resumes, exactly as MainWindow.Backstage.cs's OpenFileAsync would.
                    swappedInWorkbook = new Workbook("Book2");
                    var swappedSheet = swappedInWorkbook.AddSheet("Sheet1");
                    swappedInSheetId = swappedSheet.Id;
                    harness.RegisterWorkbook(swappedInWorkbook);
                    harness.SwapCurrentWorkbook(swappedInWorkbook);

                    var importedWorkbook = new Workbook("Imported");
                    var importedSheet = importedWorkbook.AddSheet("Data");
                    importedSheet.SetCell(new CellAddress(importedSheet.Id, 1, 1), new TextValue("Imported!"));
                    return importedWorkbook;
                });

                harness.RunImport(tempFile, adapter);
            }
            finally
            {
                File.Delete(tempFile);
            }

            // The data must have landed in the workbook Get Data was actually invoked on...
            harness.OriginalWorkbook.GetSheet(harness.OriginalSheetId)!
                .GetValue(1, 1).Should().Be(new TextValue("Imported!"), "the import must target the workbook Get Data was invoked on");

            // ...and must NOT have landed in the workbook that File > Open swapped in mid-import.
            swappedInWorkbook.Should().NotBeNull();
            swappedInWorkbook!.GetSheet(swappedInSheetId!.Value)!
                .GetCell(new CellAddress(swappedInSheetId.Value, 1, 1)).Should().BeNull(
                    "a workbook opened after Get Data was invoked must not receive the import");
        });
    }

    [Fact]
    public void ImportDataFromFileAsync_NoConcurrentOpen_StillImportsNormally()
    {
        // Sibling/no-regression case: without a concurrent Open, the import must still land at the
        // selected destination in the (only) active workbook, exactly as before the fix.
        StaTestRunner.Run(() =>
        {
            using var harness = ImportRaceHarness.Create();

            harness.SetSelection(2, 3); // destination C2
            var tempFile = harness.CreateTempImportFile();
            try
            {
                var adapter = new TestFileAdapter(load: _ =>
                {
                    var importedWorkbook = new Workbook("Imported");
                    var importedSheet = importedWorkbook.AddSheet("Data");
                    importedSheet.SetCell(new CellAddress(importedSheet.Id, 1, 1), new NumberValue(42));
                    return importedWorkbook;
                });

                harness.RunImport(tempFile, adapter);
            }
            finally
            {
                File.Delete(tempFile);
            }

            harness.OriginalWorkbook.GetSheet(harness.OriginalSheetId)!
                .GetValue(2, 3).Should().Be(new NumberValue(42));
            harness.IsImportingDataFlag.Should().BeFalse("the busy guard must be released once the import completes");
            harness.RootGridIsEnabled.Should().BeTrue("input must be re-enabled once the import completes");
        });
    }

    private sealed class ImportRaceHarness : IDisposable
    {
        private readonly MethodInfo _importDataFromFileAsync;
        private readonly MethodInfo _replaceWorkbookSession;
        private readonly FieldInfo _isImportingDataField;
        private readonly Dictionary<WorkbookId, Workbook> _workbooksById;

        private ImportRaceHarness(
            MainWindow window,
            Workbook originalWorkbook,
            Dictionary<WorkbookId, Workbook> workbooksById)
        {
            Window = window;
            OriginalWorkbook = originalWorkbook;
            OriginalSheetId = originalWorkbook.Sheets[0].Id;
            _workbooksById = workbooksById;

            _importDataFromFileAsync = typeof(MainWindow)
                .GetMethod("ImportDataFromFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ImportDataFromFileAsync");
            _replaceWorkbookSession = typeof(MainWindow)
                .GetMethod("ReplaceWorkbookSession", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ReplaceWorkbookSession");
            _isImportingDataField = typeof(MainWindow)
                .GetField("_isImportingData", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_isImportingData");
        }

        public MainWindow Window { get; }

        public Workbook OriginalWorkbook { get; }

        public SheetId OriginalSheetId { get; }

        public bool IsImportingDataFlag => (bool)_isImportingDataField.GetValue(Window)!;

        public bool RootGridIsEnabled => Window.RootGrid.IsEnabled;

        public void SetSelection(uint row, uint col)
        {
            var address = new CellAddress(OriginalSheetId, row, col);
            Window.SheetGrid.SelectedRange = new GridRange(address, address);
        }

        public string CreateTempImportFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"freex-r68-getdata-{Guid.NewGuid():N}.testimport");
            File.WriteAllText(path, "placeholder");
            return path;
        }

        /// <summary>Registers a workbook so the harness's WorkbookId-keyed CommandBus can resolve it.</summary>
        public void RegisterWorkbook(Workbook workbook) => _workbooksById[workbook.Id] = workbook;

        /// <summary>
        /// Simulates a concurrent File > Open landing mid-import by replacing the window's
        /// authoritative session on its dispatcher, exactly as MainWindow.Backstage.cs does.
        /// </summary>
        public void SwapCurrentWorkbook(Workbook newWorkbook)
        {
            Window.Dispatcher.Invoke(() =>
                _replaceWorkbookSession.Invoke(
                    Window,
                    [new StartupWorkbookLoadResult(
                        newWorkbook,
                        "Book2.fxl",
                        "Opened .fxl.",
                        IsFallback: false)]));
        }

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

        public static ImportRaceHarness Create()
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
            // brand-new default one unless adopting a shared document (see R46_PasteColumnWidthsTileTests),
            // so the live workbook is whatever workbookRef.Current now points to.
            var originalWorkbook = workbookRef.Current;
            workbooksById[originalWorkbook.Id] = originalWorkbook;

            return new ImportRaceHarness(window, originalWorkbook, workbooksById);
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

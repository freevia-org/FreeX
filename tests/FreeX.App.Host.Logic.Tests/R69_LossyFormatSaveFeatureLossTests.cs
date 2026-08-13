using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R69-services-file-open-save-6-3 (src/FreeX.App.Host/MainWindow.Backstage.cs,
/// SaveWorkbookToTargetAsync). Before the fix, Save As to a lossy plain/single-sheet format
/// (CSV/TXT/PRN/SLK/DIF/DBF) wrote silently with no feature-loss warning at all -- the
/// ConfirmUnsupportedXlsxFeatureSave gate was scoped only to ".xlsx". A multi-sheet workbook with a
/// chart saved as CSV would silently drop every sheet but the current one, plus the chart.
/// </summary>
public sealed class R69_LossyFormatSaveFeatureLossTests
{
    [Fact]
    public void MultiSheetWorkbookWithChart_SaveAsCsv_PromptsAndCancelsSaveWhenDeclined()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = LossySaveHarness.Create(acceptFeatureLoss: false);
            harness.SetWorkbook(CreateMultiSheetWorkbookWithChart());

            var path = Path.Combine(Path.GetTempPath(), $"freex-r69-lossy-decline-{Guid.NewGuid():N}.csv");
            try
            {
                var saved = harness.RunSave(new FileSaveTarget(path, new CsvFileAdapter()));

                saved.Should().BeFalse("declining the feature-loss confirmation must cancel the save");
                harness.MessageService.Calls.Should().Be(1,
                    "a multi-sheet workbook with a chart must trigger the feature-loss confirmation before writing CSV");
                File.Exists(path).Should().BeFalse("nothing should be written to disk once the user declines");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    [Fact]
    public void MultiSheetWorkbookWithChart_SaveAsCsv_WritesWhenConfirmationAccepted()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = LossySaveHarness.Create(acceptFeatureLoss: true);
            harness.SetWorkbook(CreateMultiSheetWorkbookWithChart());

            var path = Path.Combine(Path.GetTempPath(), $"freex-r69-lossy-accept-{Guid.NewGuid():N}.csv");
            try
            {
                var saved = harness.RunSave(new FileSaveTarget(path, new CsvFileAdapter()));

                saved.Should().BeTrue("accepting the feature-loss confirmation must let the save proceed");
                harness.MessageService.Calls.Should().Be(1);
                File.Exists(path).Should().BeTrue("the CSV must actually be written once the user accepts");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    [Fact]
    public void SingleSheetPlainWorkbook_SaveAsCsv_DoesNotPrompt()
    {
        // Sibling/no-regression case: nothing is lost moving a single-sheet, chart-free workbook to
        // CSV, so the new gate must stay silent -- exactly like before this fix.
        StaTestRunner.Run(() =>
        {
            using var harness = LossySaveHarness.Create(acceptFeatureLoss: false);
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
            harness.SetWorkbook(workbook);

            var path = Path.Combine(Path.GetTempPath(), $"freex-r69-lossy-noloss-{Guid.NewGuid():N}.csv");
            try
            {
                var saved = harness.RunSave(new FileSaveTarget(path, new CsvFileAdapter()));

                saved.Should().BeTrue("a single-sheet workbook with no charts loses nothing and must save straight through");
                harness.MessageService.Calls.Should().Be(0, "nothing is lost, so no confirmation should be shown");
                File.Exists(path).Should().BeTrue();
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    [Fact]
    public void XlsxSave_StillUsesItsOwnExistingUnsupportedFeatureGate_NotTheNewLossyGate()
    {
        // Sibling/no-regression case: .xlsx must keep going through ConfirmUnsupportedXlsxFeatureSave
        // exactly as before -- the new lossy-format gate must not double-prompt (or otherwise interfere)
        // for xlsx saves, even for a workbook that would trip the new gate's own multi-sheet/chart check.
        StaTestRunner.Run(() =>
        {
            using var harness = LossySaveHarness.Create(acceptFeatureLoss: true);
            harness.SetWorkbook(CreateMultiSheetWorkbookWithChart());
            // No XlsxFeatureReport with unsupported features is set, so ConfirmUnsupportedXlsxFeatureSave
            // must short-circuit true without prompting (its own existing, unchanged behavior).

            var path = Path.Combine(Path.GetTempPath(), $"freex-r69-lossy-xlsx-{Guid.NewGuid():N}.xlsx");
            try
            {
                var saved = harness.RunSave(new FileSaveTarget(path, new XlsxFileAdapter()));

                saved.Should().BeTrue();
                harness.MessageService.Calls.Should().Be(0,
                    "xlsx must not go through the new lossy-format confirmation at all");
                File.Exists(path).Should().BeTrue();
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    private static Workbook CreateMultiSheetWorkbookWithChart()
    {
        var workbook = new Workbook("Book1");
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("Hello"));
        sheet1.Charts.Add(new ChartModel());
        workbook.AddSheet("Sheet2");
        return workbook;
    }

    private sealed class LossySaveHarness : IDisposable
    {
        private readonly MethodInfo _saveMethod;
        private readonly MethodInfo _replaceWorkbookSession;

        private LossySaveHarness(MainWindow window, RecordingUserMessageService messageService)
        {
            Window = window;
            MessageService = messageService;
            _saveMethod = typeof(MainWindow).GetMethod(
                "SaveWorkbookToTargetAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SaveWorkbookToTargetAsync");
            _replaceWorkbookSession = typeof(MainWindow).GetMethod(
                "ReplaceWorkbookSession", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ReplaceWorkbookSession");
        }

        public MainWindow Window { get; }

        public RecordingUserMessageService MessageService { get; }

        public void SetWorkbook(Workbook workbook) =>
            _replaceWorkbookSession.Invoke(
                Window,
                [new StartupWorkbookLoadResult(
                    workbook,
                    "Book.fxl",
                    "Opened .fxl.",
                    IsFallback: false)]);

        public bool RunSave(FileSaveTarget target)
        {
            var task = (Task<bool>)_saveMethod.Invoke(Window, [target])!;

            // Pump the dispatcher while waiting: the save's background write resumes via the
            // DispatcherSynchronizationContext installed on this STA thread, so a nested frame (rather
            // than a blocking .Wait()/.Result) is required to let it continue (mirrors R68's
            // ImportRaceHarness.RunImport).
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);

            if (task.IsFaulted)
                throw task.Exception!.GetBaseException();

            return task.Result;
        }

        public static LossySaveHarness Create(bool acceptFeatureLoss)
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var messageService = new RecordingUserMessageService(acceptFeatureLoss);
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                messageService);

            window.Show();
            PumpDispatcher();

            return new LossySaveHarness(window, messageService);
        }

        public void Dispose()
        {
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }

        private static void PumpDispatcher()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    /// <summary>
    /// Records how many times a message/prompt was shown and answers Yes or No consistently, so tests
    /// can both assert the confirmation fired and control the simulated user's answer.
    /// </summary>
    private sealed class RecordingUserMessageService(bool acceptYes) : IUserMessageService
    {
        public int Calls { get; private set; }

        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => acceptYes;

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon)
        {
            Calls++;
            return acceptYes ? UserMessageResult.Yes : UserMessageResult.No;
        }
    }
}

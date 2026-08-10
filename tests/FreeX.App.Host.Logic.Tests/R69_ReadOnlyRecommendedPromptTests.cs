using System.Reflection;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R69-services-file-open-save-6-2 (src/FreeX.App.Host/MainWindow.Backstage.cs,
/// OpenFileAsync). Before the fix, a workbook saved with "Read-Only Recommended"
/// (<c>Workbook.FileSharing.ReadOnlyRecommended</c>) or a write-reservation password
/// (<c>ReservationPassword</c>) opened fully editable with no prompt at all -- the metadata
/// round-tripped through Save/Open but was never enforced. <c>ApplyReadOnlyRecommendedPromptIfNeeded</c>
/// (invoked by <c>OpenFileAsync</c> right after a successful load) now prompts once and records the
/// user's choice on the session's <c>_isWorkbookReadOnly</c> flag.
///
/// This is the minimal fix scope noted at <c>_isWorkbookReadOnly</c>'s declaration: it surfaces the
/// prompt and records the session's read-only intent, but does not yet block Save-over or individual
/// edit commands (residual enforcement work, out of scope here).
/// </summary>
public sealed class R69_ReadOnlyRecommendedPromptTests
{
    [Fact]
    public void ReadOnlyRecommendedWorkbook_PromptsUser_AndAcceptingMarksSessionReadOnly()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ReadOnlyPromptHarness.Create(acceptReadOnly: true);

            var workbook = new Workbook("Budget.xlsx");
            workbook.AddSheet("Sheet1");
            workbook.FileSharing = new WorkbookFileSharingModel { ReadOnlyRecommended = true };

            harness.ApplyReadOnlyRecommendedPromptIfNeeded(workbook);

            harness.MessageService.Calls.Should().Be(1,
                "a Read-Only-Recommended workbook must prompt the user before it's treated as editable");
            harness.IsWorkbookReadOnly.Should().BeTrue(
                "accepting the read-only prompt must mark this session's read-only flag");
        });
    }

    [Fact]
    public void ReservationPasswordWorkbook_PromptsUser_AndDecliningLeavesSessionEditable()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ReadOnlyPromptHarness.Create(acceptReadOnly: false);

            var workbook = new Workbook("Locked.xlsx");
            workbook.AddSheet("Sheet1");
            workbook.FileSharing = new WorkbookFileSharingModel { ReservationPassword = "secret" };

            harness.ApplyReadOnlyRecommendedPromptIfNeeded(workbook);

            harness.MessageService.Calls.Should().Be(1,
                "a write-reservation-password workbook must also prompt, not just ReadOnlyRecommended");
            harness.IsWorkbookReadOnly.Should().BeFalse(
                "declining the read-only prompt must leave the session editable");
        });
    }

    [Fact]
    public void NormalWorkbook_OpensWithoutPrompt_AndSessionStaysEditable()
    {
        // Sibling/no-regression case: a workbook with no FileSharing restriction at all must not
        // prompt, matching every workbook opened before this fix.
        StaTestRunner.Run(() =>
        {
            using var harness = ReadOnlyPromptHarness.Create(acceptReadOnly: true);

            var workbook = new Workbook("Book1.xlsx");
            workbook.AddSheet("Sheet1");

            harness.ApplyReadOnlyRecommendedPromptIfNeeded(workbook);

            harness.MessageService.Calls.Should().Be(0, "a normal workbook must not prompt at all");
            harness.IsWorkbookReadOnly.Should().BeFalse();
        });
    }

    private sealed class ReadOnlyPromptHarness : IDisposable
    {
        private readonly MethodInfo _applyMethod;
        private readonly FieldInfo _readOnlySessionField;

        private ReadOnlyPromptHarness(MainWindow window, RecordingUserMessageService messageService)
        {
            Window = window;
            MessageService = messageService;
            _applyMethod = typeof(MainWindow).GetMethod(
                "ApplyReadOnlyRecommendedPromptIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ApplyReadOnlyRecommendedPromptIfNeeded");
            _readOnlySessionField = typeof(MainWindow).GetField(
                "_workbookReadOnlySession", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbookReadOnlySession");
        }

        public MainWindow Window { get; }

        public RecordingUserMessageService MessageService { get; }

        public bool IsWorkbookReadOnly =>
            ((WorkbookReadOnlySession)_readOnlySessionField.GetValue(Window)!).IsReadOnly;

        public void ApplyReadOnlyRecommendedPromptIfNeeded(Workbook workbook) =>
            _applyMethod.Invoke(Window, [workbook]);

        public static ReadOnlyPromptHarness Create(bool acceptReadOnly)
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var messageService = new RecordingUserMessageService(acceptReadOnly);
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

            return new ReadOnlyPromptHarness(window, messageService);
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
    /// can both assert the prompt fired and control the simulated user's answer.
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

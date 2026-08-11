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
/// Regression coverage for R69-services-file-open-save-6-2 and the round-134 SECURITY fix
/// (src/FreeX.App.Host/MainWindow.Backstage.cs, OpenFileAsync). Before R69, a workbook saved with
/// "Read-Only Recommended" (<c>Workbook.FileSharing.ReadOnlyRecommended</c>) or a write-reservation
/// password (<c>ReservationPassword</c>) opened fully editable with no prompt at all -- the metadata
/// round-tripped through Save/Open but was never enforced.
///
/// R69 added a prompt for both cases, but for the password case that prompt was a plain Yes/No "open
/// read-only?" question -- the password itself was never actually asked for or checked, so declining
/// (answering "No") granted full write access with zero verification. The round-134 fix replaces that
/// with a real password prompt verified against the stored hash via
/// <see cref="ProtectionPasswordHelper.VerifyStoredPassword"/>: the correct password unlocks a writable
/// session, and -- matching Excel -- a wrong password or Cancel falls back to read-only rather than
/// refusing to open.
///
/// This is still the minimal fix scope noted at <c>_isWorkbookReadOnly</c>'s declaration: it enforces
/// write-reservation on open, but does not yet block individual edit commands (residual enforcement
/// work, out of scope here) beyond forcing Save-over through Save-As (R83).
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
    public void ReadOnlyRecommendedWorkbook_DecliningPrompt_LeavesSessionEditable()
    {
        // Sibling/no-regression case: the plain "Read-Only Recommended" Yes/No flow (no password
        // involved at all) is untouched by the round-134 password-verification fix.
        StaTestRunner.Run(() =>
        {
            using var harness = ReadOnlyPromptHarness.Create(acceptReadOnly: false);

            var workbook = new Workbook("Budget.xlsx");
            workbook.AddSheet("Sheet1");
            workbook.FileSharing = new WorkbookFileSharingModel { ReadOnlyRecommended = true };

            harness.ApplyReadOnlyRecommendedPromptIfNeeded(workbook);

            harness.IsWorkbookReadOnly.Should().BeFalse(
                "declining the plain Read-Only-Recommended prompt (no password involved) must leave the session editable");
        });
    }

    [Fact]
    public void ReservationPasswordWorkbook_CorrectPassword_UnlocksEditableSession()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ReadOnlyPromptHarness.Create(acceptReadOnly: true, reservationPasswordEntry: _ => "secret");

            var workbook = new Workbook("Locked.xlsx");
            workbook.AddSheet("Sheet1");
            workbook.FileSharing = new WorkbookFileSharingModel { ReservationPassword = "secret" };

            harness.ApplyReadOnlyRecommendedPromptIfNeeded(workbook);

            harness.IsWorkbookReadOnly.Should().BeFalse(
                "typing the correct write-reservation password must unlock a fully editable session");
            harness.MessageService.Calls.Should().Be(0,
                "a correct password must not trigger the 'opened as read-only' warning");
        });
    }

    [Fact]
    public void ReservationPasswordWorkbook_WrongPassword_OpensReadOnlyAndWarns()
    {
        // THE security case: before the round-134 fix, this path never asked for a password at all --
        // it showed a Yes/No "open read-only?" question, and answering "No" granted full write access
        // with zero verification. Now a wrong password must fall back to a genuinely read-only session.
        // acceptReadOnly is deliberately false: under the pre-fix Yes/No prompt this simulates the
        // user declining read-only (answering "No"), which is exactly the click that used to hand out
        // full write access with the password never checked -- acceptReadOnly:true would coincidentally
        // yield the same IsWorkbookReadOnly=true result under both the old and new code, certifying
        // nothing.
        StaTestRunner.Run(() =>
        {
            using var harness = ReadOnlyPromptHarness.Create(acceptReadOnly: false, reservationPasswordEntry: _ => "not-the-password");

            var workbook = new Workbook("Locked.xlsx");
            workbook.AddSheet("Sheet1");
            workbook.FileSharing = new WorkbookFileSharingModel { ReservationPassword = "secret" };

            harness.ApplyReadOnlyRecommendedPromptIfNeeded(workbook);

            harness.IsWorkbookReadOnly.Should().BeTrue(
                "a wrong write-reservation password must fall back to a read-only session, not grant write access");
            harness.MessageService.Calls.Should().Be(1,
                "a wrong password must surface an 'opened as read-only' notice");
        });
    }

    [Fact]
    public void ReservationPasswordWorkbook_CancelledPrompt_OpensReadOnlyWithoutIncorrectPasswordWarning()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ReadOnlyPromptHarness.Create(acceptReadOnly: true, reservationPasswordEntry: _ => null);

            var workbook = new Workbook("Locked.xlsx");
            workbook.AddSheet("Sheet1");
            workbook.FileSharing = new WorkbookFileSharingModel { ReservationPassword = "secret" };

            harness.ApplyReadOnlyRecommendedPromptIfNeeded(workbook);

            harness.IsWorkbookReadOnly.Should().BeTrue(
                "cancelling the write-reservation password prompt must fall back to a read-only session");
            harness.MessageService.Calls.Should().Be(0,
                "a plain Cancel already communicates its own intent and should not also show an 'incorrect password' warning");
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

        public static ReadOnlyPromptHarness Create(bool acceptReadOnly, Func<string, string?>? reservationPasswordEntry = null)
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

            if (reservationPasswordEntry is not null)
            {
                var overrideField = typeof(MainWindow).GetField(
                    "_reservationPasswordPromptOverrideForTest", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(nameof(MainWindow), "_reservationPasswordPromptOverrideForTest");
                overrideField.SetValue(window, reservationPasswordEntry);
            }

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

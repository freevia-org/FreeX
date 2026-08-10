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
/// Regression coverage for R83-services-doc-recovery-props-5-1 (src/FreeX.App.Host/MainWindow.Backstage.cs
/// + MainWindow.WorkbookLifecycle.cs). Before the fix, <c>ApplyReadOnlyRecommendedPromptIfNeeded</c>
/// (see R69_ReadOnlyRecommendedPromptTests) set <c>_isWorkbookReadOnly</c> on open but nothing ever
/// consulted it again: <c>SaveResolvedAsync</c> resolved straight to the existing path via
/// <c>FileSavePlanner.TryResolveExistingPath</c> and silently overwrote the very file the user had just
/// told FreeX to treat as read-only. <c>ResolveExistingSaveTarget</c> now withholds the existing-path
/// target whenever the session is marked read-only, which makes <c>SaveResolvedAsync</c> fall through to
/// the Save-As dialog instead (Excel parity: Ctrl+S on a Read-Only-Recommended/write-reservation
/// workbook is always forced through Save-As, never a silent overwrite).
/// </summary>
public sealed class R83_ReadOnlyWorkbookSaveEnforcementTests
{
    [Fact]
    public void ReadOnlySession_ResolveExistingSaveTarget_ReturnsNull_EvenWithAResolvableExistingPath()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = SaveTargetHarness.Create();
            harness.SetCurrentFilePath(@"C:\fake\Budget.fxjson");
            harness.SetWorkbookReadOnly(true);

            var target = harness.ResolveExistingSaveTarget();

            target.Should().BeNull(
                "a session marked read-only by ApplyReadOnlyRecommendedPromptIfNeeded must never " +
                "resolve back to its own path -- Save must fall through to Save-As instead of " +
                "silently overwriting the protected file");
        });
    }

    [Fact]
    public void EditableSession_ResolveExistingSaveTarget_StillResolvesTheExistingPath()
    {
        // Sibling/no-regression case: an ordinary (non-read-only) session with a resolvable existing
        // path must keep resolving to it, exactly as before this fix -- only the read-only session's
        // behavior changed.
        StaTestRunner.Run(() =>
        {
            using var harness = SaveTargetHarness.Create();
            const string path = @"C:\fake\Budget.fxjson";
            harness.SetCurrentFilePath(path);
            harness.SetWorkbookReadOnly(false);

            var target = harness.ResolveExistingSaveTarget();

            target.Should().NotBeNull(
                "an editable session with a resolvable existing path must still Save-over it directly");
            target!.Path.Should().Be(path);
        });
    }

    private sealed class SaveTargetHarness : IDisposable
    {
        private readonly MethodInfo _resolveMethod;
        private readonly FieldInfo _readOnlySessionField;
        private readonly PropertyInfo _currentFilePathProperty;

        private SaveTargetHarness(MainWindow window)
        {
            Window = window;
            _resolveMethod = typeof(MainWindow).GetMethod(
                "ResolveExistingSaveTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ResolveExistingSaveTarget");
            _readOnlySessionField = typeof(MainWindow).GetField(
                "_workbookReadOnlySession", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbookReadOnlySession");
            _currentFilePathProperty = typeof(MainWindow).GetProperty(
                "_currentFilePath", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "_currentFilePath");
        }

        public MainWindow Window { get; }

        public void SetWorkbookReadOnly(bool value) =>
            ((WorkbookReadOnlySession)_readOnlySessionField.GetValue(Window)!).ApplyPromptDecision(value);

        public void SetCurrentFilePath(string? path) => _currentFilePathProperty.SetValue(Window, path);

        public FileSaveTarget? ResolveExistingSaveTarget() =>
            (FileSaveTarget?)_resolveMethod.Invoke(Window, null);

        public static SaveTargetHarness Create()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var adapters = new IFileAdapter[]
            {
                new TestFileAdapter(save: (_, _) => { }, extension: ".fxjson")
            };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                adapters,
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            window.Show();
            PumpDispatcher();

            return new SaveTargetHarness(window);
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

    /// <summary>A no-op message service -- this test's method under test never prompts.</summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => true;

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Yes;
    }
}

using System.IO;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Free.Shared.AppServices;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R118: an invalid/nonexistent/unsupported command-line file argument used to be silently
/// swallowed with zero user feedback -- App.xaml.cs's startup-arg loop `continue`d past any path
/// that failed File.Exists with no else-branch, and MainWindow.Backstage.cs's OpenFileAsync (the
/// single choke point for every open path: File > Open, drag-drop, MRU clicks, and command-line
/// startup args) discarded WorkbookOpenTargetPlanner.TryCreateOpenTarget's descriptive failure
/// `message` via `out _`. Real Excel always surfaces a visible error dialog for an unopenable
/// command-line file argument instead of quietly falling back to a blank workbook.
/// </summary>
public sealed class R118_StartupFileArgumentSilentSwallowTests
{
    /// <summary>
    /// Primary fail-before/pass-after coverage: OpenFileAsync (reached here via the internal
    /// OpenStartupFileAsync wrapper, the exact same real entry point App.xaml.cs's startup-arg
    /// loop calls) must surface WorkbookOpenTargetPlanner's failure message to the user instead of
    /// silently no-opping when the planner can't create an open target.
    /// </summary>
    [Fact]
    public void R118_OpenStartupFileAsync_UnsupportedExtension_SurfacesPlannerMessageToUser()
    {
        var messages = new RecordingUserMessageService();
        var tempPath = Path.Combine(Path.GetTempPath(), $"r118-startup-arg-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(tempPath, "not a real workbook");

        try
        {
            StaTestRunner.Run(() =>
            {
                var initialWorkbook = new Workbook("Book1");
                initialWorkbook.AddSheet("Sheet1");
                var workbookRef = new WorkbookRef { Current = initialWorkbook };

                // No registered file adapters at all: WorkbookOpenTargetPlanner.TryCreateOpenTarget
                // will fail for ANY extension with a "Unsupported file type: {ext}." message -- this
                // isolates the planner-message-discarded defect from the (already-working) load-time
                // exception handler further down OpenFileAsync.
                var window = new MainWindow(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindow>.Instance,
                    new ViewportService(),
                    new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                    new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                    [],
                    workbookRef,
                    initialWorkbook,
                    messages);

                try
                {
                    window.Show();
                    PumpDispatcher();

                    RunAsyncOnSta(() => window.OpenStartupFileAsync(tempPath));
                    PumpDispatcher();
                }
                finally
                {
                    MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                    PumpDispatcher();
                }
            });

            messages.Messages.Should().NotBeEmpty(
                "the planner's discarded failure reason must be surfaced to the user instead of " +
                "silently no-opping (R118)");
            messages.Messages.Should().Contain(message =>
                    message.Contains("Unsupported file type", StringComparison.Ordinal) &&
                    message.Contains(".xlsx", StringComparison.Ordinal),
                "the specific planner message (extension-derived) must reach the user, not a generic string");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>
    /// No-regression sibling: when the planner DOES resolve a valid open target (a genuinely
    /// supported, existing file), OpenFileAsync must open it normally with no spurious warning --
    /// proving the new failure-surfacing branch only fires on an actual planner failure.
    /// </summary>
    [Fact]
    public void R118_OpenStartupFileAsync_SupportedExistingFile_OpensWithoutSpuriousWarning()
    {
        var messages = new RecordingUserMessageService();
        var tempPath = Path.Combine(Path.GetTempPath(), $"r118-startup-arg-ok-{Guid.NewGuid():N}.fxjson");
        File.WriteAllText(tempPath, "payload");
        Workbook? loadedWorkbook = null;

        try
        {
            StaTestRunner.Run(() =>
            {
                var initialWorkbook = new Workbook("Book1");
                initialWorkbook.AddSheet("Sheet1");
                var workbookRef = new WorkbookRef { Current = initialWorkbook };

                var adapter = new TestFileAdapter(
                    load: _ =>
                    {
                        var loaded = new Workbook("Loaded");
                        loaded.AddSheet("Sheet1");
                        return loaded;
                    },
                    extension: ".fxjson",
                    formatName: "Fake");

                var window = new MainWindow(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindow>.Instance,
                    new ViewportService(),
                    new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                    new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                    [adapter],
                    workbookRef,
                    initialWorkbook,
                    messages);

                try
                {
                    window.Show();
                    PumpDispatcher();

                    RunAsyncOnSta(() => window.OpenStartupFileAsync(tempPath));
                    PumpDispatcher();

                    loadedWorkbook = workbookRef.Current;
                }
                finally
                {
                    MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                    PumpDispatcher();
                }
            });

            messages.Messages.Should().BeEmpty(
                "a genuinely openable file must not trigger the planner-failure warning path");
            loadedWorkbook.Should().NotBeNull();
            // MainWindow.Backstage.cs's OpenFileAsync renames the loaded workbook to the file's
            // display name (plan.DisplayName derived from the opened file's name), so assert on
            // that rename actually having happened -- proof the real load pipeline ran end to end,
            // not just that no warning fired.
            loadedWorkbook!.Name.Should().Be(Path.GetFileNameWithoutExtension(tempPath),
                "the supported file must still actually open through the real pipeline, not just avoid the warning");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Covers the other half of the fix: App.xaml.cs's App_OnStartup calls
    /// MainWindow.ReportStartupFileNotFound when NO command-line argument resolved to an
    /// existing file (a typo'd path, a directory, a URL, ...). This test exercises that method
    /// directly -- the same real method App_OnStartup dispatches to via mainWindow.Dispatcher.
    /// </summary>
    [Fact]
    public void R118_ReportStartupFileNotFound_ShowsMessageNamingTheRequestedPath()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var messages = new RecordingUserMessageService();
            var window = new MainWindow(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                messages);

            const string missingPath = @"C:\definitely-not-a-real-path\typo.xlsx";

            try
            {
                window.Show();
                PumpDispatcher();

                window.ReportStartupFileNotFound(missingPath);
                PumpDispatcher();

                messages.Messages.Should().ContainSingle()
                    .Which.Should().Contain(missingPath,
                        "the message must name the exact path the user tried to open on the command line");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    /// <summary>
    /// Runs an async delegate to completion on the shared STA dispatcher thread (must itself run
    /// inside a <see cref="StaTestRunner.Run"/> callback). Plain <c>.GetAwaiter().GetResult()</c>
    /// on that thread would deadlock the moment the delegate awaits something that resumes via the
    /// captured <c>DispatcherSynchronizationContext</c>, because nothing would be pumping the
    /// dispatcher's queue while we block. Pushing a nested <see cref="DispatcherFrame"/> that exits
    /// once the task completes keeps the message loop actively pumping for the whole await chain.
    /// </summary>
    private static void RunAsyncOnSta(Func<Task> asyncAction)
    {
        var task = asyncAction();
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(
                _ => frame.Continue = false,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
        }

        // Propagate any exception (and pick up the final result/state) now that the task is done.
        task.GetAwaiter().GetResult();
    }

    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public List<string> Messages { get; } = new();

        public void ShowError(string message, string title = "Error") => Messages.Add($"Error: {title}: {message}");

        public void ShowWarning(string message, string title = "Warning") => Messages.Add($"Warning: {title}: {message}");

        public void ShowInfo(string message, string title = "Information") => Messages.Add($"Info: {title}: {message}");

        public bool AskYesNo(string message, string title = "Confirm")
        {
            Messages.Add($"AskYesNo: {title}: {message}");
            return true;
        }

        public UserMessageResult ShowMessage(string message, string title, UserMessageButtons buttons, UserMessageIcon icon)
        {
            Messages.Add($"ShowMessage: {title}: {message}");
            return UserMessageResult.Ok;
        }
    }
}

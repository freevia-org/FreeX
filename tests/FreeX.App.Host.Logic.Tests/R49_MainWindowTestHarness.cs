using System.Reflection;
using System.Windows;
using Free.Shared.AppServices;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Shared harness for the round-49 app-host regression tests (R49-commands-outline-group-3-2,
/// R49-render-multiarea-selection-3-1/3-2/3-3, R49-commands-cf-manage-3-2,
/// R49-render-header-frozen-corner-3-2). Mirrors the MainWindow-construction pattern already used
/// by R46_PasteColumnWidthsTileTests.cs's PasteColumnWidthsHarness, factored out so every R49 test
/// file doesn't have to repeat it.
/// </summary>
internal static class R49MainWindowTestHarness
{
    public static (MainWindow Window, Workbook Workbook) CreateWindow(
        IPlatformClipboard? platformClipboard = null)
    {
        platformClipboard ??= new InMemoryPlatformClipboard();

        // Closing the last test window must not shut down the shared WPF dispatcher. Several
        // rendering tests run after these window tests and use the same STA for DrawingVisual /
        // RenderTargetBitmap work.
        if (Application.Current is { } application)
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var initialWorkbook = new Workbook("Book1");
        initialWorkbook.AddSheet("Sheet1");

        var workbookRef = new WorkbookRef { Current = initialWorkbook };
        var window = new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
            [],
            workbookRef,
            initialWorkbook,
            new R49RecordingUserMessageService(),
            platformClipboard: platformClipboard);

        window.Show();
        PumpDispatcher();

        // MainWindow_Loaded (MainWindow.Startup.cs) replaces the constructor's workbook with a
        // brand-new default one unless adopting a shared document, so the live workbook is
        // whatever workbookRef.Current now points to (see R41_FreezePaneScrollPreservationTests /
        // R46_PasteColumnWidthsTileTests).
        return (window, workbookRef.Current);
    }

    public static void Close(MainWindow window)
    {
        window.SuppressNextClosePrompt();
        window.Close();
        PumpDispatcher();
    }

    /// <summary>
    /// Runs pending dispatcher work and returns once the window has settled.
    /// </summary>
    /// <remarks>
    /// r444: the sentinel is posted at <c>SystemIdle</c>, the LOWEST priority, not <c>Background</c>.
    /// A Background sentinel drains only Background and above, so anything the window posted at
    /// ContextIdle, ApplicationIdle or SystemIdle survived the pump -- and so did work posted BY
    /// work the pump had just run, which is how a UI actually settles, one stage handing off to the
    /// next. Every UI test in this lane treats this call as "the window has settled" and then
    /// asserts, so those gaps turned such assertions into timing races: they passed or failed by
    /// machine load rather than by behaviour. Both gaps are pinned by
    /// R444_PumpDispatcherDrainsIdleWorkTests, which fails against the old Background sentinel.
    ///
    /// The deadline exists so that a component re-posting work forever fails its test slowly
    /// instead of hanging the whole lane with no output to diagnose.
    /// </remarks>
    public static void PumpDispatcher()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var frame = new System.Windows.Threading.DispatcherFrame();

        dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.SystemIdle,
            new Action(() => frame.Continue = false));

        var deadline = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(10),
            System.Windows.Threading.DispatcherPriority.Send,
            (_, _) => frame.Continue = false,
            dispatcher);
        deadline.Start();

        try
        {
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        finally
        {
            deadline.Stop();
        }
    }

    /// <summary>Invokes a private/internal instance method on MainWindow by name via reflection.</summary>
    public static object? Invoke(MainWindow window, string methodName, params object?[] args)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), methodName);
        return method.Invoke(window, args);
    }

    /// <summary>No-op <see cref="IUserMessageService"/> so tests that construct MainWindow directly
    /// don't pop up real WPF MessageBox windows (mirrors R46_PasteColumnWidthsTileTests's local copy).</summary>
    private sealed class R49RecordingUserMessageService : IUserMessageService
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

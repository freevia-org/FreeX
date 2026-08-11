using System.Reflection;
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

    public static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
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

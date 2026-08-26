using System.Reflection;
using Free.Shared.Testing;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Shared shell for state-local WPF interaction tests. Startup, recovery, clipboard, focus, and
/// multi-window tests deliberately remain on fresh windows.
/// </summary>
internal static class ReusableFreeXMainWindowSession
{
    private static readonly ReusableWpfWindowSession<MainWindow> Session = new(CreateWindow, ResetWindow);
    private static WorkbookRef? _workbookRef;

    internal static void Run(Action<MainWindow> action) => Session.Run(action);

    internal static void Run(Action<MainWindow, WorkbookRef> action) =>
        Session.Run(window => action(window, _workbookRef!));

    private static MainWindow CreateWindow()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        _workbookRef = workbookRef;
        return new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
            [],
            workbookRef,
            workbook,
            NullUserMessageService.Instance);
    }

    private static void ResetWindow(MainWindow window)
    {
        Invoke(window, "CreateNewWorkbook");
        Invoke(window, "HideStartScreen");
        window.UpdateLayout();
    }

    private static void Invoke(MainWindow window, string methodName) =>
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!
            .Invoke(window, null);
}

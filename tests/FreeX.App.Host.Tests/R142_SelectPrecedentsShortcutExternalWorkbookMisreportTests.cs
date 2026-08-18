using System.Reflection;
using System.Windows;
using FreeX.App.Presentation;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R142 remediation: Trace Precedents (TracePrecedentsForCell) was fixed to stop reporting
/// "no direct precedents" for a formula whose only precedent is an external-workbook reference
/// (R142-core-commands-formula-auditing-trace-precedents-external-workbook-misreport), but its
/// keyboard-shortcut sibling -- Ctrl+[ (Select Direct Precedents) -- called the same
/// FormulaAuditSelectionPlanner.Plan / FormulaAuditingService.GetDirectPrecedents API and still
/// unconditionally reported "No direct precedents" with no HasExternalPrecedentReference check.
///
/// This test drives the REAL WPF entry point: MainWindow.KeyboardFocus.cs's private
/// ExecuteCommandShortcut(shortcut, sender, e) -- the same method a real Ctrl+[ keypress routes to
/// via KeyboardShortcutMatcher.TryGetCommandShortcut + _keyboardCommandDispatcher.TryExecute (see
/// MainWindow.KeyboardCommands.cs registering KeyboardCommandShortcut.SelectDirectPrecedents ->
/// SelectFormulaAuditCells(selectDependents: false, includeTransitive: false), and
/// WorkbookKeyboardShortcutCatalog.cs mapping Ctrl+OemOpenBrackets to that same shortcut) -- not a
/// hand-built model or the private SelectFormulaAuditCells method called directly.
/// </summary>
public sealed class R142_SelectPrecedentsShortcutExternalWorkbookMisreportTests
{
    [Fact]
    public void CtrlOpenBracketShortcut_OnCellWithOnlyExternalWorkbookPrecedent_DoesNotReportNoPrecedents()
    {
        StaTestRunner.Run(() =>
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
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var formulaCell = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(formulaCell, Cell.FromFormula("'[Budget.xlsx]Sheet1'!A1"));

                // Sanity: this is exactly the misreport condition -- the formula's only precedent
                // cannot be represented as a local CellAddress, so the planner (built on
                // GetDirectPrecedents) has nothing to select and returns null.
                FormulaAuditSelectionPlanner.Plan(workbook, formulaCell, selectDependents: false, includeTransitive: false)
                    .Should().BeNull("GetDirectPrecedents cannot address a cell in another workbook");
                FormulaAuditingService.HasExternalPrecedentReference(workbook, formulaCell)
                    .Should().BeTrue("the formula's only precedent lives in another workbook");

                InvokeSetActiveCell(window, formulaCell);

                InvokeCommandShortcut(window, KeyboardCommandShortcut.SelectDirectPrecedents);
                PumpDispatcher();

                var statusText = (System.Windows.Controls.TextBlock)window.FindName("StatusReadyText")!;
                statusText.Text.Should().NotBe("No direct precedents",
                    "the formula DOES have a precedent -- it is just in another workbook, which FreeX cannot select");
                statusText.Text.Should().Contain("another workbook",
                    "the status must tell the user the true reason nothing was selected");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void CtrlOpenBracketShortcut_OnOrdinaryCellWithNoPrecedents_StillReportsNoDirectPrecedents()
    {
        // No-regression sibling: an ordinary formula-less/precedent-less cell must keep the plain
        // "No direct precedents" status -- only the genuine external-reference case gets the new
        // message.
        StaTestRunner.Run(() =>
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
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var plainCell = new CellAddress(sheet.Id, 3, 3);
                sheet.SetCell(plainCell, new NumberValue(5));

                InvokeSetActiveCell(window, plainCell);

                InvokeCommandShortcut(window, KeyboardCommandShortcut.SelectDirectPrecedents);
                PumpDispatcher();

                var statusText = (System.Windows.Controls.TextBlock)window.FindName("StatusReadyText")!;
                statusText.Text.Should().Be("No direct precedents");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static void InvokeCommandShortcut(MainWindow window, KeyboardCommandShortcut shortcut)
    {
        var method = typeof(MainWindow).GetMethod(
            "ExecuteCommandShortcut",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteCommandShortcut");
        method.Invoke(window, [shortcut, window, new RoutedEventArgs()]);
    }

    private static void InvokeSetActiveCell(MainWindow window, CellAddress addr)
    {
        var method = typeof(MainWindow).GetMethod(
            "SetActiveCell",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
        method.Invoke(window, [addr]);
    }
}

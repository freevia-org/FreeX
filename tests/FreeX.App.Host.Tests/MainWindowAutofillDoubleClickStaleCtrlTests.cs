using System.Reflection;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

// Regression coverage for R79-commands-autofill-series-5-1: double-click on the fill handle
// never raises GridView's AutofillModifiersResolved event (that pairing only happens at
// drag-release), so MainWindow's captured _autofillCtrlHeld field can be left stale from a
// PRIOR Ctrl-held drag. Excel's double-click fill always behaves like a plain (non-Ctrl) drag,
// so OnAutofillHandleDoubleClicked must ignore the stale field and pass ctrlHeld: false.
public sealed class MainWindowAutofillDoubleClickStaleCtrlTests
{
    [Fact]
    public void DoubleClickFillHandle_WithStaleCtrlHeldFromPriorDrag_StillContinuesSeries()
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

                // Source: B1:B2 = 10, 20 -- a 2-cell numeric trend that Excel's fill handle would
                // continue (30, 40, 50) on a plain drag/double-click, or flip to a verbatim copy
                // (20, 20, 20) when Ctrl is held.
                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
                // Adjacent column C populated down to row 5 so the double-click extent detector
                // has somewhere to extend the fill to.
                sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(1));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(2));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(3));
                sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(4));
                sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(5));

                var source = new GridRange(
                    new CellAddress(sheet.Id, 1, 2),
                    new CellAddress(sheet.Id, 2, 2));

                // Simulate the stale state left behind by an earlier Ctrl-held drag (step 1 of the
                // finding's failure scenario). In production this is set only via GridView's
                // AutofillModifiersResolved event at drag-release; here we set the field directly
                // since a double-click never raises that event at all.
                var ctrlField = typeof(MainWindow).GetField("_autofillCtrlHeld", BindingFlags.Instance | BindingFlags.NonPublic);
                ctrlField.Should().NotBeNull("MainWindow must declare the captured Ctrl-flip field");
                ctrlField!.SetValue(window, true);

                var doubleClickMethod = typeof(MainWindow).GetMethod(
                    "OnAutofillHandleDoubleClicked", BindingFlags.Instance | BindingFlags.NonPublic);
                doubleClickMethod.Should().NotBeNull();
                doubleClickMethod!.Invoke(window, [source]);
                PumpDispatcher();

                // Excel's real behavior: double-click always continues the detected series exactly
                // like a plain (non-Ctrl) drag, regardless of what a prior drag's Ctrl state was.
                sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(30),
                    "double-click must continue the series like a plain drag, ignoring the stale Ctrl state left by a prior drag");
                sheet.GetValue(new CellAddress(sheet.Id, 4, 2)).Should().Be(new NumberValue(40));
                sheet.GetValue(new CellAddress(sheet.Id, 5, 2)).Should().Be(new NumberValue(50));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void DoubleClickFillHandle_WithoutPriorStaleCtrlState_StillContinuesSeries()
    {
        // No-regression sibling: same scenario but without ever touching _autofillCtrlHeld, so it
        // sits at its default (false) -- the double-click must behave identically either way.
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

                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(1));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(2));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(3));
                sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(4));
                sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(5));

                var source = new GridRange(
                    new CellAddress(sheet.Id, 1, 2),
                    new CellAddress(sheet.Id, 2, 2));

                var doubleClickMethod = typeof(MainWindow).GetMethod(
                    "OnAutofillHandleDoubleClicked", BindingFlags.Instance | BindingFlags.NonPublic);
                doubleClickMethod.Should().NotBeNull();
                doubleClickMethod!.Invoke(window, [source]);
                PumpDispatcher();

                sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(30));
                sheet.GetValue(new CellAddress(sheet.Id, 4, 2)).Should().Be(new NumberValue(40));
                sheet.GetValue(new CellAddress(sheet.Id, 5, 2)).Should().Be(new NumberValue(50));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }
}

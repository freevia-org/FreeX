using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Cleanup batch MED14 — round-10 MED/LOW findings.
/// </summary>
public sealed class FreeXCleanupMED14Tests
{
    // P39 (MED): the WPF in-cell editor TextBox had no AutomationProperties.Name (or AutomationId) at
    // all, so a screen-reader user entering edit mode heard a bare "edit" with no indication of which
    // cell was being edited. Verify the real inline editor now exposes an automation Name that includes
    // the cell address, and that it updates when a different cell is edited.
    [Fact]
    public void ShowInlineEditor_SetsAccessibleNameWithCellAddress()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("hello")));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("world")));
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = System.Windows.WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            try
            {
                window.Show();
                window.Activate();
                window.UpdateLayout();
                PumpDispatcher();

                var showInlineEditor = typeof(MainWindow)
                    .GetMethod("ShowInlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(nameof(MainWindow), "ShowInlineEditor");
                var setActiveCell = typeof(MainWindow)
                    .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
                var inlineEditorField = typeof(MainWindow)
                    .GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditor");

                setActiveCell.Invoke(window, [new CellAddress(sheet.Id, 1, 1)]);
                PumpDispatcher();
                showInlineEditor.Invoke(window, [new CellAddress(sheet.Id, 1, 1)]);
                PumpDispatcher();

                var editor = (TextBox)inlineEditorField.GetValue(window)!;
                var nameAtA1 = AutomationProperties.GetName(editor);
                var automationId = AutomationProperties.GetAutomationId(editor);

                nameAtA1.Should().NotBeNullOrWhiteSpace("the inline editor must have an accessible name");
                nameAtA1.Should().Contain("A1", "the accessible name must identify the cell being edited");
                automationId.Should().Be("WorksheetInlineCellEditor");

                setActiveCell.Invoke(window, [new CellAddress(sheet.Id, 2, 2)]);
                PumpDispatcher();
                showInlineEditor.Invoke(window, [new CellAddress(sheet.Id, 2, 2)]);
                PumpDispatcher();

                var nameAtB2 = AutomationProperties.GetName(editor);
                nameAtB2.Should().Contain("B2", "the accessible name must track the currently-edited cell");
                nameAtB2.Should().NotBe(nameAtA1, "moving to a different cell must update the announced name");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new System.Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = Freexcel.App.UI.GridView;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowFormulaBarSyncTests
{
    [Fact]
    public void ClearSelection_RefreshesFormulaBarForClearedActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "stale text");
            harness.SelectActiveCell(1, 1);
            harness.FormulaBarText.Should().Be("stale text");

            harness.ClearSelectedContents();

            harness.CellText(1, 1).Should().BeNull();
            harness.FormulaBarText.Should().BeEmpty();
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly FieldInfo _workbookField;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _executeClearSelection;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _workbookField = typeof(MainWindow)
                .GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbook");
            _setActiveCell = typeof(MainWindow)
                .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _executeClearSelection = typeof(MainWindow)
                .GetMethod("ExecuteClearSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteClearSelection");
        }

        public string FormulaBarText => ((TextBox)_window.FindName("FormulaBar")).Text;

        public void SetCellText(uint row, uint col, string text)
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new TextValue(text)));
        }

        public string? CellText(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            return sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value is TextValue text
                ? text.Value
                : null;
        }

        public void SelectActiveCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _setActiveCell.Invoke(_window, [new CellAddress(sheet.Id, row, col)]);
            PumpDispatcher();
        }

        public void ClearSelectedContents()
        {
            _executeClearSelection.Invoke(_window, null);
            PumpDispatcher();
        }

        public static MainWindowHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window);
        }

        private Workbook Workbook =>
            (Workbook)(_workbookField.GetValue(_window)
                ?? throw new InvalidOperationException("MainWindow workbook is not initialized."));

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}

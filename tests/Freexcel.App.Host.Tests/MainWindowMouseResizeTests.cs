using System.Reflection;
using System.Windows;
using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowMouseResizeTests
{
    [Fact]
    public void DoubleClickColumnResizeBorder_AutoFitsColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SetCell(2, 3, "a much longer display value for autofit");

            harness.AutoFitColumn(3);

            harness.CurrentSheet.ColumnWidths[3]
                .Should()
                .BeGreaterThan(harness.CurrentSheet.DefaultColumnWidth);
        });
    }

    [Fact]
    public void DoubleClickRowResizeBorder_AutoFitsRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SetCell(4, 2, "first\nsecond\nthird");

            harness.AutoFitRow(4);

            harness.CurrentSheet.RowHeights[4]
                .Should()
                .BeGreaterThan(harness.CurrentSheet.DefaultRowHeight);
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _onColumnAutoFitRequested;
        private readonly MethodInfo _onRowAutoFitRequested;
        private readonly FieldInfo _workbookField;
        private readonly FieldInfo _currentSheetIdField;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _onColumnAutoFitRequested = typeof(MainWindow)
                .GetMethod("OnColumnAutoFitRequested", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnColumnAutoFitRequested");
            _onRowAutoFitRequested = typeof(MainWindow)
                .GetMethod("OnRowAutoFitRequested", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnRowAutoFitRequested");
            _workbookField = typeof(MainWindow)
                .GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbook");
            _currentSheetIdField = typeof(MainWindow)
                .GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
        }

        public Sheet CurrentSheet
        {
            get
            {
                var sheetId = (SheetId)_currentSheetIdField.GetValue(_window)!;
                return CurrentWorkbook.GetSheet(sheetId) ?? throw new InvalidOperationException("Current sheet was not found.");
            }
        }

        public void SetCell(uint row, uint col, string text)
        {
            CurrentSheet.SetCell(new CellAddress(CurrentSheet.Id, row, col), new TextValue(text));
            PumpDispatcher();
        }

        public void AutoFitColumn(uint col)
        {
            _onColumnAutoFitRequested.Invoke(_window, [col]);
            PumpDispatcher();
        }

        public void AutoFitRow(uint row)
        {
            _onRowAutoFitRequested.Invoke(_window, [row]);
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

        private Workbook CurrentWorkbook =>
            (Workbook)_workbookField.GetValue(_window)!;

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

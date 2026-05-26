using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = Freexcel.App.UI.GridView;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowQuickAnalysisKeyboardTests
{
    [Fact]
    public void KeyboardQuickAnalysisMenu_FocusesFirstOptionAndTargetsSelectedRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(1, 1, 3, 2);
            harness.OpenQuickAnalysisMenu();

            harness.FocusedMenuHeader.Should().Be("Data Bars");
            harness.ContextMenuPlacementTargetName.Should().Be("SheetGrid");
            harness.OpenMenuHeaders.Should().ContainInOrder(["Formatting", "Data Bars", "Color Scale", "Icon Set"]);
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.DataBars);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Color Scale");

            harness.FocusedMenuHeader.Should().Be("Color Scale");
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.ColorScale);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Icon Set");

            harness.FocusedMenuHeader.Should().Be("Icon Set");
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.IconSet);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Column");

            harness.FocusedMenuHeader.Should().Be("Column");
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.ColumnChart);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Stacked Column");

            harness.FocusedMenuHeader.Should().Be("Stacked Column");
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.StackedColumnChart);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("100% Stacked Column");

            harness.FocusedMenuHeader.Should().Be("100% Stacked Column");
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.StackedColumnChart);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Line");

            harness.FocusedMenuHeader.Should().Be("Line");
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.LineChart);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Bar");

            harness.FocusedMenuHeader.Should().Be("Bar");
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.BarChart);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));
        });
    }

    [Fact]
    public void KeyboardQuickAnalysisMenu_WithNoSelectionReportsUnsupportedState()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(1, 1, 3, 2);
            harness.OpenQuickAnalysisMenu();
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.DataBars);

            harness.ClearSelection();
            harness.OpenQuickAnalysisMenu();

            harness.FocusedMenuHeader.Should().BeNull();
            harness.QuickAnalysisPreviewVisual.Should().Be(GridQuickAnalysisPreviewVisualKind.None);
            harness.QuickAnalysisPreviewRange.Should().BeNull();
            harness.StatusText.Should().Be("Select a range to use Quick Analysis.");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Workbook _workbook;
        private readonly MethodInfo _showQuickAnalysisMenu;

        private MainWindowHarness(MainWindow window, Workbook workbook)
        {
            _window = window;
            _workbook = workbook;
            _showQuickAnalysisMenu = typeof(MainWindow)
                .GetMethod("ShowQuickAnalysisMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ShowQuickAnalysisMenu");
        }

        public string? FocusedMenuHeader =>
            Keyboard.FocusedElement is MenuItem menuItem ? menuItem.Header?.ToString() : null;

        public string? ContextMenuPlacementTargetName =>
            ActiveContextMenu?.PlacementTarget is FrameworkElement target ? target.Name : null;

        public IReadOnlyList<string> OpenMenuHeaders =>
            ActiveContextMenu?.Items.OfType<MenuItem>()
                .Select(item => item.Header?.ToString() ?? "")
                .ToList() ?? [];

        public GridQuickAnalysisPreviewVisualKind QuickAnalysisPreviewVisual =>
            SheetGrid.QuickAnalysisPreviewVisual;

        public (uint StartRow, uint StartCol, uint EndRow, uint EndCol)? QuickAnalysisPreviewRange
        {
            get
            {
                var range = SheetGrid.QuickAnalysisPreviewRange;
                return range is { } value
                    ? (value.Start.Row, value.Start.Col, value.End.Row, value.End.Col)
                    : null;
            }
        }

        public string StatusText =>
            ((TextBlock)_window.FindName("StatusReadyText")).Text;

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheet = _workbook.Sheets[0];
            SheetGrid.SelectedRange = new GridRange(
                new CellAddress(sheet.Id, startRow, startCol),
                new CellAddress(sheet.Id, endRow, endCol));
            PumpDispatcher();
        }

        public void ClearSelection()
        {
            SheetGrid.SelectedRange = null;
            PumpDispatcher();
        }

        public void OpenQuickAnalysisMenu()
        {
            _showQuickAnalysisMenu.Invoke(_window, null);
            PumpDispatcher();
        }

        public void FocusMenuItem(string header)
        {
            var item = ActiveContextMenu?.Items.OfType<MenuItem>()
                .FirstOrDefault(item => item.Header?.ToString() == header)
                ?? throw new InvalidOperationException($"Menu item '{header}' was not found.");
            item.Focus();
            Keyboard.Focus(item);
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
            return new MainWindowHarness(window, workbook);
        }

        private SheetGridView SheetGrid =>
            (SheetGridView)_window.FindName("SheetGrid");

        private ContextMenu? ActiveContextMenu
        {
            get
            {
                if (Keyboard.FocusedElement is not MenuItem menuItem)
                    return null;

                return ItemsControl.ItemsControlFromItemContainer(menuItem) as ContextMenu;
            }
        }

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

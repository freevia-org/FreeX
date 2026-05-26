using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowWorksheetContextMenuKeyboardTests
{
    [Fact]
    public void KeyboardWorksheetContextMenu_FocusesCutAndShowsClipboardAccessHeaders()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenKeyboardContextMenu();

            harness.FocusedMenuHeader.Should().Be("Cu_t");
            harness.ContextMenuPlacementTargetName.Should().Be("SheetGrid");
            harness.OpenMenuHeaders.Should().StartWith(["Cu_t", "_Copy", "_Paste", "Paste _Special..."]);
        });
    }

    [Fact]
    public void KeyboardWorksheetContextMenu_WithWholeRowSelectionShowsRowScopedCommands()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectWholeRows(3, 4);
            harness.OpenKeyboardContextMenu();

            harness.FocusedMenuHeader.Should().Be("Cu_t");
            harness.ContextMenuPlacementTargetName.Should().Be("SheetGrid");
            harness.OpenMenuHeaders.Should().ContainInOrder([
                "Cu_t",
                "_Copy",
                "_Paste",
                "Insert Row _Above",
                "Delete _Row(s)",
                "Row _Height...",
                "AutoFit Row He_ight",
                "_Hide Rows",
                "Unhide Ro_ws",
                "_Group",
                "_Ungroup",
                "_Format Cells...",
                "Clear C_ontents"
            ]);
            harness.OpenMenuHeaders.Should().NotContain("Column _Width...");
            harness.OpenMenuHeaders.Should().NotContain("Insert Column _Left");
        });
    }

    [Fact]
    public void KeyboardWorksheetContextMenu_WithWholeColumnSelectionShowsColumnScopedCommands()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectWholeColumns(2, 3);
            harness.OpenKeyboardContextMenu();

            harness.FocusedMenuHeader.Should().Be("Cu_t");
            harness.ContextMenuPlacementTargetName.Should().Be("SheetGrid");
            harness.OpenMenuHeaders.Should().ContainInOrder([
                "Cu_t",
                "_Copy",
                "_Paste",
                "Insert Column _Left",
                "Delete _Column(s)",
                "Column _Width...",
                "AutoFit Column Wi_dth",
                "Hide Col_umns",
                "Unhide Co_lumns",
                "_Group",
                "_Ungroup",
                "_Format Cells...",
                "Clear C_ontents"
            ]);
            harness.OpenMenuHeaders.Should().NotContain("Row _Height...");
            harness.OpenMenuHeaders.Should().NotContain("Insert Row _Above");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Workbook _workbook;
        private readonly MethodInfo _openKeyboardContextMenu;

        private MainWindowHarness(MainWindow window, Workbook workbook)
        {
            _window = window;
            _workbook = workbook;
            _openKeyboardContextMenu = typeof(MainWindow)
                .GetMethod("OpenKeyboardContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OpenKeyboardContextMenu");
        }

        public string? FocusedMenuHeader =>
            Keyboard.FocusedElement is MenuItem menuItem ? menuItem.Header?.ToString() : null;

        public string? ContextMenuPlacementTargetName =>
            ActiveContextMenu?.PlacementTarget is FrameworkElement target ? target.Name : null;

        public IReadOnlyList<string> OpenMenuHeaders =>
            ActiveContextMenu?.Items.OfType<MenuItem>()
                .Select(item => item.Header?.ToString() ?? "")
                .ToList() ?? [];

        public void SelectWholeRows(uint startRow, uint endRow)
        {
            var sheet = _workbook.Sheets[0];
            var range = new GridRange(
                new CellAddress(sheet.Id, startRow, 1),
                new CellAddress(sheet.Id, endRow, CellAddress.MaxCol));
            SheetGrid.SelectedRange = range;
            PumpDispatcher();
        }

        public void SelectWholeColumns(uint startCol, uint endCol)
        {
            var sheet = _workbook.Sheets[0];
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, startCol),
                new CellAddress(sheet.Id, CellAddress.MaxRow, endCol));
            SheetGrid.SelectedRange = range;
            PumpDispatcher();
        }

        public void OpenKeyboardContextMenu()
        {
            _openKeyboardContextMenu.Invoke(_window, null);
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

        private Freexcel.App.UI.GridView SheetGrid =>
            (Freexcel.App.UI.GridView)_window.FindName("SheetGrid");

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

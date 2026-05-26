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

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _openKeyboardContextMenu;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
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
            return new MainWindowHarness(window);
        }

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

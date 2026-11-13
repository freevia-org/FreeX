using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Excel: after Ctrl+C, while the copied range is still outlined, pressing Enter on the destination
/// pastes there and ends copy mode. Without an outlined copy, Enter keeps moving the selection.
/// </summary>
public sealed class EnterPastesCopiedRangeTests
{
    [Fact]
    public void Enter_WithACopiedRangeOutlined_PastesAndEndsCopyMode()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = EnterHarness.Create();
            harness.SetCellValue(1, 1, new NumberValue(42));
            harness.SelectActiveCell(1, 1);
            harness.Window.ExecuteCopy();
            harness.Grid.ClipboardRange.Should().NotBeNull("sanity: Copy shows the marching-ants outline");

            harness.SelectActiveCell(3, 2);
            harness.PressEnterOnGrid();

            harness.GetCellValue(3, 2).Should().Be(new NumberValue(42), "Enter pastes the copied cell");
            harness.Grid.ClipboardRange.Should().BeNull("pasting with Enter ends copy mode, as in Excel");
            harness.Grid.SelectedRange!.Value.Start.Should().Be(
                new CellAddress(harness.SheetId, 3, 2),
                "Enter pastes in place instead of moving the selection");
        });
    }

    [Fact]
    public void Enter_WithoutACopiedRange_StillMovesTheSelection()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = EnterHarness.Create();
            harness.SelectActiveCell(1, 1);

            harness.PressEnterOnGrid();

            harness.Grid.SelectedRange!.Value.Start.Should().Be(new CellAddress(harness.SheetId, 2, 1));
        });
    }

    private sealed class EnterHarness : IDisposable
    {
        private EnterHarness(MainWindow window) => Window = window;

        public MainWindow Window { get; }

        public SheetGridView Grid => (SheetGridView)Window.FindName("SheetGrid");

        private Sheet Sheet => Window.Session.Workbook.Sheets[0];

        public SheetId SheetId => Sheet.Id;

        public void SetCellValue(uint row, uint col, ScalarValue value)
        {
            Sheet.SetCell(new CellAddress(SheetId, row, col), value);
            DispatcherTestPump.PumpDispatcher();
        }

        public ScalarValue GetCellValue(uint row, uint col) =>
            Sheet.GetCell(new CellAddress(SheetId, row, col))?.Value ?? BlankValue.Instance;

        public void SelectActiveCell(uint row, uint col)
        {
            Window.SetActiveCell(new CellAddress(SheetId, row, col));
            DispatcherTestPump.PumpDispatcher();
        }

        public void PressEnterOnGrid()
        {
            Grid.Focus();
            Keyboard.Focus(Grid);
            DispatcherTestPump.PumpDispatcher();
            var source = PresentationSource.FromVisual(Grid)
                ?? throw new InvalidOperationException("The grid has no presentation source.");
            Grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            });
            DispatcherTestPump.PumpDispatcher();
        }

        public static EnterHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                Array.Empty<FreeX.Core.IO.IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance,
                platformClipboard: new InMemoryPlatformClipboard())
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            return new EnterHarness(window);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            DispatcherTestPump.PumpDispatcher();
        }
    }
}

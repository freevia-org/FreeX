using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for review group O2-nav-r1c1:
///   - J24: Go To Special / Find-family shortcuts expand a single active-cell selection to the
///     sheet's used range (Excel semantics), while an explicit multi-cell selection is honored as-is.
///   - J59: The Go To dialog's pre-filled default reference honors R1C1 mode.
///   - J29: F4 reference-cycling in the formula bar/inline editor handles R1C1 formulas.
///   - J48: The Name Box / Go To reference parser (WorkbookReferenceNavigator) accepts whole-column
///     (A:A) and whole-row (5:5) references.
/// </summary>
public sealed class GoToNavigationR1C1RegressionTests
{
    [Fact]
    public void FindFormulasShortcut_FromSingleActiveCell_ExpandsSearchToWholeUsedRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            // A formula cell far away from the active cell -- only reachable if the search expands
            // to the sheet's used range instead of staying pinned to the 1x1 active-cell selection.
            harness.SetCellFormula(20, 5, "=1+1");
            harness.SelectActiveCell(1, 1);

            harness.InvokeFindFormulasMenuItem();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 20, 5),
                new CellAddress(harness.CurrentSheetId, 20, 5)));
        });
    }

    [Fact]
    public void FindFormulasShortcut_FromExplicitMultiCellSelection_DoesNotExpandBeyondSelection()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            // A formula outside the explicit selection must NOT be picked up: real Excel honors an
            // explicit multi-cell selection instead of silently widening it to the used range.
            harness.SetCellFormula(20, 5, "=1+1");
            harness.SelectRange(1, 1, 3, 3);

            harness.InvokeFindFormulasMenuItem();

            // No formula cell exists inside A1:C3, so nothing should be found/selected -- the selection
            // stays exactly as the user set it.
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 3, 3)));
        });
    }

    [Fact]
    public void GoToDialogDefaultAddress_WhenR1C1ModeEnabled_UsesR1C1Notation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(useR1C1ReferenceStyle: true);

            harness.SelectActiveCell(5, 2);

            harness.ComputeGoToDefaultAddress().Should().Be("R5C2");
        });
    }

    [Fact]
    public void GoToDialogDefaultAddress_WhenA1ModeActive_UsesA1Notation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(useR1C1ReferenceStyle: false);

            harness.SelectActiveCell(5, 2);

            harness.ComputeGoToDefaultAddress().Should().Be("B5");
        });
    }

    [Fact]
    public void F4_InFormulaBar_WhenR1C1ModeEnabled_CyclesR1C1Reference()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(useR1C1ReferenceStyle: true);

            harness.SelectActiveCell(3, 3);
            harness.SetFormulaEditCell(3, 3);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=R[-2]C[-1]+R[1]C");
            harness.SetFormulaBarCaretIndex(3);

            harness.PressFormulaBarKey(Key.F4).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=R1C2+R[1]C");
        });
    }

    [Fact]
    public void F4_InInlineEditor_WhenR1C1ModeEnabled_CyclesR1C1Reference()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(useR1C1ReferenceStyle: true);

            harness.SelectActiveCell(3, 3);
            harness.ShowInlineEditor(3, 3);
            harness.SetInlineEditorText("=R[-2]C[-1]+R[1]C");
            harness.SetInlineEditorCaretIndex(3);

            harness.PressInlineEditorKey(Key.F4).Should().BeTrue();

            harness.InlineEditorText.Should().Be("=R1C2+R[1]C");
        });
    }

    [Theory]
    [InlineData("A:A", 1u, 1u)]
    [InlineData("C:E", 3u, 5u)]
    public void WorkbookReferenceNavigator_ParsesWholeColumnRange(string reference, uint startCol, uint endCol)
    {
        var sheetId = SheetId.New();

        FreeX.App.Services.WorkbookReferenceNavigator
            .TryParseReferenceRange(reference, sheetId, definedNames: null, out var range)
            .Should()
            .BeTrue();

        range.Start.Should().Be(new CellAddress(sheetId, 1, startCol));
        range.End.Should().Be(new CellAddress(sheetId, CellAddress.MaxRow, endCol));
    }

    [Theory]
    [InlineData("5:5", 5u, 5u)]
    [InlineData("5:9", 5u, 9u)]
    public void WorkbookReferenceNavigator_ParsesWholeRowRange(string reference, uint startRow, uint endRow)
    {
        var sheetId = SheetId.New();

        FreeX.App.Services.WorkbookReferenceNavigator
            .TryParseReferenceRange(reference, sheetId, definedNames: null, out var range)
            .Should()
            .BeTrue();

        range.Start.Should().Be(new CellAddress(sheetId, startRow, 1));
        range.End.Should().Be(new CellAddress(sheetId, endRow, CellAddress.MaxCol));
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _findFormulasMenuItemClick;
        private readonly MethodInfo _recalculateWorkbook;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _findFormulasMenuItemClick = typeof(MainWindow)
                .GetMethod("FindFormulasMenuItem_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FindFormulasMenuItem_Click");
            _recalculateWorkbook = typeof(MainWindow)
                .GetMethod("RecalculateWorkbook", BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), "RecalculateWorkbook");
        }

        public SheetId CurrentSheetId => Workbook.Sheets[0].Id;

        public GridRange? SelectedRange => ((SheetGridView)_window.FindName("SheetGrid")).SelectedRange;

        public string FormulaBarText => ((TextBox)_window.FindName("FormulaBar")).Text;

        public string? InlineEditorText => InlineEditor?.Text;

        private TextBox? InlineEditor => _window.InlineEditorForTest;

        private Workbook Workbook => _window.Session.Workbook;

        public void SetCellFormula(uint row, uint col, string formulaText)
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromFormula(formulaText));
            // Go To Special > Formulas filters by the formula's *computed* value type (matching
            // Excel's Numbers/Text/Logicals/Errors sub-filters), so a never-recalculated formula
            // cell (still BlankValue) would not be found -- force a recalc so the cached value is
            // populated before the test drives the Find-Formulas shortcut.
            _recalculateWorkbook.Invoke(_window, []);
        }

        public void SelectActiveCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _window.SetActiveCellForTest(new CellAddress(sheet.Id, row, col));
            PumpDispatcher();
        }

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheet = Workbook.Sheets[0];
            var range = new GridRange(
                new CellAddress(sheet.Id, startRow, startCol),
                new CellAddress(sheet.Id, endRow, endCol));
            var grid = (SheetGridView)_window.FindName("SheetGrid");
            grid.SelectedRanges = null;
            grid.SelectedRange = range;
            PumpDispatcher();
        }

        public void InvokeFindFormulasMenuItem()
        {
            _findFormulasMenuItemClick.Invoke(_window, [_window, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        public string ComputeGoToDefaultAddress()
        {
            // Mirrors FindGoToMenuItem_Click's default-address computation without opening the
            // modal Go To dialog itself.
            var formatCellReference = typeof(MainWindow)
                .GetMethod("FormatCellReference", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FormatCellReference");
            var range = SelectedRange
                ?? throw new InvalidOperationException("No selection to compute a default address for.");
            return (string)formatCellReference.Invoke(_window, [range.Start])!;
        }

        public void SetFormulaEditCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _window.SetFormulaEditCellForTest(new CellAddress(sheet.Id, row, col));
            PumpDispatcher();
        }

        public void ShowInlineEditor(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _window.ShowInlineEditorForTest(new CellAddress(sheet.Id, row, col));
            PumpDispatcher();
        }

        public void SetFormulaBarText(string text)
        {
            ((TextBox)_window.FindName("FormulaBar")).Text = text;
            PumpDispatcher();
        }

        public void SetFormulaBarCaretIndex(int caretIndex)
        {
            ((TextBox)_window.FindName("FormulaBar")).CaretIndex = caretIndex;
            PumpDispatcher();
        }

        public void SetInlineEditorText(string text)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            inlineEditor.Text = text;
            PumpDispatcher();
        }

        public void SetInlineEditorCaretIndex(int caretIndex)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            inlineEditor.CaretIndex = caretIndex;
            PumpDispatcher();
        }

        public void FocusFormulaBar()
        {
            var formulaBar = (TextBox)_window.FindName("FormulaBar");
            _window.Activate();
            FocusManager.SetFocusedElement(_window, formulaBar);
            formulaBar.Focus();
            Keyboard.Focus(formulaBar);
            PumpDispatcher();
        }

        public bool PressFormulaBarKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _window.RaiseFormulaBarKeyDownForTest(args);
            PumpDispatcher();
            return args.Handled;
        }

        public bool PressInlineEditorKey(Key key)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _window.RaiseInlineEditorKeyDownForTest(args);
            PumpDispatcher();
            return args.Handled;
        }

        public static MainWindowHarness Create(bool useR1C1ReferenceStyle = false)
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
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
                NullUserMessageService.Instance,
                options: new AppOptions { UseR1C1ReferenceStyle = useR1C1ReferenceStyle })
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window);
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
}

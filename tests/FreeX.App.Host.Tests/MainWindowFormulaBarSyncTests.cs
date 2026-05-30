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

public sealed class MainWindowFormulaBarSyncTests
{
    [Fact]
    public void NewWorkbook_SelectsA1AndBindsFormulaBarEditsToA1()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expected = new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1));

            harness.SelectedRange.Should().Be(expected);
            harness.CellAddressBoxText.Should().Be("A1");

            harness.SetFormulaBarText("fresh value");
            harness.CommitEdit().Should().BeTrue();

            harness.CellText(1, 1).Should().Be("fresh value");
            harness.SelectedRange.Should().Be(expected);
            harness.CellAddressBoxText.Should().Be("A1");
        });
    }

    [Fact]
    public void InsertedSheet_RebindsActiveCellToCurrentSheet()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var firstSheetId = harness.CurrentSheetId;

            harness.SetFormulaBarText("first sheet");
            harness.CommitEdit().Should().BeTrue();
            harness.InsertNewSheet();

            harness.CurrentSheetId.Should().NotBe(firstSheetId);
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.CellAddressBoxText.Should().Be("A1");

            harness.SetFormulaBarText("second sheet");
            harness.CommitEdit().Should().BeTrue();

            harness.CellText(1, 1, firstSheetId).Should().Be("first sheet");
            harness.CellText(1, 1, harness.CurrentSheetId).Should().Be("second sheet");
        });
    }

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

    [Fact]
    public void InlineEditorTextChange_RefreshesFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);

            harness.SetInlineEditorText("typed inline");

            harness.FormulaBarText.Should().Be("typed inline");
        });
    }

    [Fact]
    public void FormulaBarTextChange_WhileInlineEditorVisible_RefreshesInlineEditor()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);

            harness.SetFormulaBarText("typed in formula bar");

            harness.InlineEditorText.Should().Be("typed in formula bar");
        });
    }

    [Fact]
    public void FocusFormulaBar_WhileInlineEditorVisible_DoesNotCommitDraftEdit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("draft edit");

            harness.FocusFormulaBar();

            harness.InlineEditorVisible.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
            harness.FormulaBarText.Should().Be("draft edit");
        });
    }

    [Fact]
    public void CtrlEnterFormulaBarEdit_FillsSelectedRangeWhenNotChoosingFormulaReferences()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(1, 1, 2, 2);
            harness.SetFormulaEditCell(1, 1);
            harness.SetFormulaBarText("filled");

            harness.CommitEditAcrossSelection(fillFormulaEditCellOnly: false).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("filled");
            harness.CellText(1, 2).Should().Be("filled");
            harness.CellText(2, 1).Should().Be("filled");
            harness.CellText(2, 2).Should().Be("filled");
        });
    }

    [Fact]
    public void CtrlEnterFormulaReferenceEntry_CommitsOnlyOriginalEditCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(3, 3, 4, 4);
            harness.SetFormulaEditCell(1, 1);
            harness.SetFormulaBarText("=C3");

            harness.CommitEditAcrossSelection(fillFormulaEditCellOnly: true).Should().BeTrue();

            harness.CellFormula(1, 1).Should().Be("C3");
            harness.CellFormula(3, 3).Should().BeNull();
            harness.CellFormula(3, 4).Should().BeNull();
            harness.CellFormula(4, 3).Should().BeNull();
            harness.CellFormula(4, 4).Should().BeNull();
        });
    }

    [Fact]
    public void NameBoxEnter_NavigatesRefreshesFormulaBarAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(5, 3, "target cell");
            harness.SetCellAddressBoxText("C5");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 5, 3),
                new CellAddress(harness.CurrentSheetId, 5, 3)));
            harness.CellAddressBoxText.Should().Be("C5");
            harness.FormulaBarText.Should().Be("target cell");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly FieldInfo _workbookField;
        private readonly FieldInfo _currentSheetIdField;
        private readonly FieldInfo _formulaEditCellField;
        private readonly FieldInfo _inlineEditorField;
        private readonly MethodInfo _commitEdit;
        private readonly MethodInfo _commitEditAcrossSelection;
        private readonly MethodInfo _insertNewSheet;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _showInlineEditor;
        private readonly MethodInfo _executeClearSelection;
        private readonly MethodInfo _cellAddressBoxKeyDown;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _workbookField = typeof(MainWindow)
                .GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbook");
            _currentSheetIdField = typeof(MainWindow)
                .GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
            _formulaEditCellField = typeof(MainWindow)
                .GetField("_formulaEditCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_formulaEditCell");
            _inlineEditorField = typeof(MainWindow)
                .GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditor");
            _commitEdit = typeof(MainWindow)
                .GetMethod("CommitEdit", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CommitEdit");
            _commitEditAcrossSelection = typeof(MainWindow)
                .GetMethod("CommitEditAcrossSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CommitEditAcrossSelection");
            _insertNewSheet = typeof(MainWindow)
                .GetMethod("InsertNewSheet", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertNewSheet");
            _setActiveCell = typeof(MainWindow)
                .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _showInlineEditor = typeof(MainWindow)
                .GetMethod("ShowInlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ShowInlineEditor");
            _executeClearSelection = typeof(MainWindow)
                .GetMethod("ExecuteClearSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteClearSelection");
            _cellAddressBoxKeyDown = typeof(MainWindow)
                .GetMethod("CellAddressBox_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CellAddressBox_KeyDown");
        }

        public string FormulaBarText => ((TextBox)_window.FindName("FormulaBar")).Text;

        public string CellAddressBoxText => ((TextBox)_window.FindName("CellAddressBox")).Text;

        public SheetId CurrentSheetId => (SheetId)_currentSheetIdField.GetValue(_window)!;

        public GridRange? SelectedRange => ((SheetGridView)_window.FindName("SheetGrid")).SelectedRange;

        public string? InlineEditorText => InlineEditor?.Text;

        public bool InlineEditorVisible => InlineEditor?.IsVisible == true;

        public bool SheetGridFocused => ReferenceEquals(Keyboard.FocusedElement, (SheetGridView)_window.FindName("SheetGrid"));

        public void SetCellText(uint row, uint col, string text)
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new TextValue(text)));
        }

        public string? CellText(uint row, uint col) => CellText(row, col, Workbook.Sheets[0].Id);

        public string? CellText(uint row, uint col, SheetId sheetId)
        {
            var sheet = Workbook.GetSheet(sheetId)
                ?? throw new InvalidOperationException($"Sheet {sheetId} not found.");
            return sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value is TextValue text
                ? text.Value
                : null;
        }

        public string? CellFormula(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            return sheet.GetCell(new CellAddress(sheet.Id, row, col))?.FormulaText;
        }

        public void SelectActiveCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _setActiveCell.Invoke(_window, [new CellAddress(sheet.Id, row, col)]);
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

        public void SetFormulaEditCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _formulaEditCellField.SetValue(_window, new CellAddress(sheet.Id, row, col));
            PumpDispatcher();
        }

        public bool CommitEditAcrossSelection(bool fillFormulaEditCellOnly)
        {
            var committed = (bool)_commitEditAcrossSelection.Invoke(_window, [fillFormulaEditCellOnly])!;
            PumpDispatcher();
            return committed;
        }

        public bool CommitEdit()
        {
            var committed = (bool)_commitEdit.Invoke(_window, null)!;
            PumpDispatcher();
            return committed;
        }

        public void InsertNewSheet()
        {
            _insertNewSheet.Invoke(_window, null);
            PumpDispatcher();
        }

        public void ShowInlineEditor(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _showInlineEditor.Invoke(_window, [new CellAddress(sheet.Id, row, col)]);
            PumpDispatcher();
        }

        public void SetFormulaBarText(string text)
        {
            ((TextBox)_window.FindName("FormulaBar")).Text = text;
            PumpDispatcher();
        }

        public void SetCellAddressBoxText(string text)
        {
            ((TextBox)_window.FindName("CellAddressBox")).Text = text;
            PumpDispatcher();
        }

        public bool PressCellAddressBoxKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _cellAddressBoxKeyDown.Invoke(_window, [((TextBox)_window.FindName("CellAddressBox")), args]);
            PumpDispatcher();
            return args.Handled;
        }

        public void SetInlineEditorText(string text)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            inlineEditor.Text = text;
            PumpDispatcher();
        }

        public void FocusFormulaBar()
        {
            ((TextBox)_window.FindName("FormulaBar")).Focus();
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
                workbook,
                NullUserMessageService.Instance)
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

        private TextBox? InlineEditor => (TextBox?)_inlineEditorField.GetValue(_window);

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

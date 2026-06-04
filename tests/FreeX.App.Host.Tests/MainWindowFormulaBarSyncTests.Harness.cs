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

public sealed partial class MainWindowFormulaBarSyncTests
{
    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly FieldInfo _workbookField;
        private readonly FieldInfo _currentSheetIdField;
        private readonly FieldInfo _formulaEditCellField;
        private readonly FieldInfo _formulaRangeEntryModeField;
        private readonly FieldInfo _inlineEditorField;
        private readonly MethodInfo _commitEdit;
        private readonly MethodInfo _commitEditAcrossSelection;
        private readonly MethodInfo _insertNewSheet;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _showInlineEditor;
        private readonly MethodInfo _executeClearSelection;
        private readonly MethodInfo _formulaBarKeyDown;
        private readonly MethodInfo _cellAddressBoxKeyDown;
        private readonly MethodInfo _insertFormulaFunction;
        private readonly MethodInfo _insertDefinedNameIntoFormula;
        private readonly MethodInfo _formulaBarExpandButtonClick;
        private readonly MethodInfo _editActiveCellInFormulaBar;

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
            _formulaRangeEntryModeField = typeof(MainWindow)
                .GetField("_formulaRangeEntryMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_formulaRangeEntryMode");
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
            _formulaBarKeyDown = typeof(MainWindow)
                .GetMethod("FormulaBar_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FormulaBar_KeyDown");
            _cellAddressBoxKeyDown = typeof(MainWindow)
                .GetMethod("CellAddressBox_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CellAddressBox_KeyDown");
            _insertFormulaFunction = typeof(MainWindow)
                .GetMethod("InsertFormulaFunction", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertFormulaFunction");
            _insertDefinedNameIntoFormula = typeof(MainWindow)
                .GetMethod("InsertDefinedNameIntoFormula", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertDefinedNameIntoFormula");
            _formulaBarExpandButtonClick = typeof(MainWindow)
                .GetMethod("FormulaBarExpandBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FormulaBarExpandBtn_Click");
            _editActiveCellInFormulaBar = typeof(MainWindow)
                .GetMethod("EditActiveCellInFormulaBar", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EditActiveCellInFormulaBar");
        }

        public string FormulaBarText => ((TextBox)_window.FindName("FormulaBar")).Text;

        public string CellAddressBoxText => ((TextBox)_window.FindName("CellAddressBox")).Text;

        public SheetId CurrentSheetId => (SheetId)_currentSheetIdField.GetValue(_window)!;

        public GridRange? SelectedRange => ((SheetGridView)_window.FindName("SheetGrid")).SelectedRange;

        public string? InlineEditorText => InlineEditor?.Text;

        public bool InlineEditorVisible => InlineEditor?.IsVisible == true;

        public bool InlineEditorFocused => InlineEditor is { } inlineEditor && IsFocused(inlineEditor);

        public bool FormulaBarFocused => IsFocused((TextBox)_window.FindName("FormulaBar"));

        public bool CellAddressBoxFocused => IsFocused((TextBox)_window.FindName("CellAddressBox"));

        public int CellAddressBoxSelectionLength => ((TextBox)_window.FindName("CellAddressBox")).SelectionLength;

        public bool SheetGridFocused => IsFocused((SheetGridView)_window.FindName("SheetGrid"));

        public bool FormulaBarAcceptsReturn => ((TextBox)_window.FindName("FormulaBar")).AcceptsReturn;

        public int FormulaBarCaretIndex => ((TextBox)_window.FindName("FormulaBar")).CaretIndex;

        public double FormulaBarHeight => ((TextBox)_window.FindName("FormulaBar")).Height;

        public string FormulaBarExpandButtonAutomationName =>
            System.Windows.Automation.AutomationProperties.GetName((Button)_window.FindName("FormulaBarExpandBtn"));

        public void SetCellText(uint row, uint col, string text)
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new TextValue(text)));
        }

        public void SetCellFormula(uint row, uint col, string formulaText)
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromFormula(formulaText));
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

        public GridRange NamedRange(string name)
        {
            Workbook.TryGetNamedRange(name, out var range).Should().BeTrue();
            return range;
        }

        public void DefineNamedRange(string name, GridRange range)
        {
            Workbook.DefineNamedRange(name, range);
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

        public void InsertFormulaFunction(string functionName)
        {
            _insertFormulaFunction.Invoke(_window, [functionName]);
            PumpDispatcher();
        }

        public void InsertDefinedNameIntoFormula(string name)
        {
            _insertDefinedNameIntoFormula.Invoke(_window, [name]);
            PumpDispatcher();
        }

        public void ToggleFormulaBarExpansion()
        {
            var button = (Button)_window.FindName("FormulaBarExpandBtn");
            _formulaBarExpandButtonClick.Invoke(_window, [button, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        public void EditActiveCellInFormulaBar()
        {
            _editActiveCellInFormulaBar.Invoke(_window, null);
            PumpDispatcher();
        }

        public void SetFormulaBarText(string text)
        {
            ((TextBox)_window.FindName("FormulaBar")).Text = text;
            UpdateFormulaRangeEntryMode(text);
            PumpDispatcher();
        }

        public void SetFormulaBarCaretIndex(int caretIndex)
        {
            ((TextBox)_window.FindName("FormulaBar")).CaretIndex = caretIndex;
            PumpDispatcher();
        }

        public void SetInlineEditorCaretIndex(int caretIndex)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            inlineEditor.CaretIndex = caretIndex;
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

        public bool PressFormulaBarKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _formulaBarKeyDown.Invoke(_window, [((TextBox)_window.FindName("FormulaBar")), args]);
            PumpDispatcher();
            return args.Handled;
        }

        public void SetInlineEditorText(string text)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            inlineEditor.Text = text;
            UpdateFormulaRangeEntryMode(text);
            PumpDispatcher();
        }

        private void UpdateFormulaRangeEntryMode(string text)
        {
            if (FormulaEditInteractionPlanner.ShouldStartPointModeFromTypedText(text))
                _formulaRangeEntryModeField.SetValue(_window, true);
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
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window);
        }

        private Workbook Workbook =>
            (Workbook)(_workbookField.GetValue(_window)
                ?? throw new InvalidOperationException("MainWindow workbook is not initialized."));

        private TextBox? InlineEditor => (TextBox?)_inlineEditorField.GetValue(_window);

        private bool IsFocused(IInputElement element) =>
            ReferenceEquals(Keyboard.FocusedElement, element) ||
            ReferenceEquals(FocusManager.GetFocusedElement(_window), element);

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

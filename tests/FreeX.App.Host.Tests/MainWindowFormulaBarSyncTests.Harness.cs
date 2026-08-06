using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
    internal sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly ICommandBus _commandBus;
        private readonly FieldInfo _workbookField;
        private readonly FieldInfo _currentSheetIdField;
        private readonly FieldInfo _formulaEditCellField;
        private readonly FieldInfo _formulaRangeEntryModeField;
        private readonly PropertyInfo _selectionAnchorProperty;
        private readonly FieldInfo _selectionCursorField;
        private readonly FieldInfo _inlineEditorField;
        private readonly MethodInfo _commitEdit;
        private readonly MethodInfo _commitEditAcrossSelection;
        private readonly MethodInfo _insertNewSheet;
        private readonly MethodInfo _selectSingleSheetTab;
        private readonly MethodInfo _updateViewport;
        private readonly MethodInfo _refreshSheetTabs;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _showInlineEditor;
        private readonly MethodInfo _executeClearSelection;
        private readonly MethodInfo _formulaBarKeyDown;
        private readonly MethodInfo _inlineEditorKeyDown;
        private readonly MethodInfo _cellAddressBoxKeyDown;
        private readonly MethodInfo _insertRawFormulaFunction;
        private readonly MethodInfo _insertDefinedNameIntoFormula;
        private readonly MethodInfo _formulaBarExpandButtonClick;
        private readonly MethodInfo _editActiveCellInFormulaBar;
        private readonly MethodInfo _tryApplyFormulaRangeSelection;
        private readonly MethodInfo _raiseFormulaReferenceGripDragForTest;
        private readonly MethodInfo _tryHandleFormulaSheetTabClick;
        private readonly MethodInfo _tryToggleFormulaRangeEntrySelectionMode;
        private readonly MethodInfo _selectRow;
        private readonly MethodInfo _selectColumn;
        private readonly MethodInfo _addAdditionalRowSelection;
        private readonly MethodInfo _selectAll;

        private MainWindowHarness(MainWindow window, ICommandBus commandBus)
        {
            _window = window;
            _commandBus = commandBus;
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
            _selectionAnchorProperty = typeof(MainWindow)
                .GetProperty("_selectionAnchor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "_selectionAnchor");
            _selectionCursorField = typeof(MainWindow)
                .GetField("_selectionCursor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_selectionCursor");
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
            _selectSingleSheetTab = typeof(MainWindow)
                .GetMethod("SelectSingleSheetTab", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SelectSingleSheetTab");
            _updateViewport = typeof(MainWindow)
                .GetMethod("UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateViewport");
            _refreshSheetTabs = typeof(MainWindow)
                .GetMethod("RefreshSheetTabs", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RefreshSheetTabs");
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
            _inlineEditorKeyDown = typeof(MainWindow)
                .GetMethod("InlineEditor_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InlineEditor_KeyDown");
            _cellAddressBoxKeyDown = typeof(MainWindow)
                .GetMethod("CellAddressBox_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CellAddressBox_KeyDown");
            _insertRawFormulaFunction = typeof(MainWindow)
                .GetMethod("InsertRawFormulaFunction", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertRawFormulaFunction");
            _insertDefinedNameIntoFormula = typeof(MainWindow)
                .GetMethod("InsertDefinedNameIntoFormula", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertDefinedNameIntoFormula");
            _formulaBarExpandButtonClick = typeof(MainWindow)
                .GetMethod("FormulaBarExpandBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FormulaBarExpandBtn_Click");
            _editActiveCellInFormulaBar = typeof(MainWindow)
                .GetMethod("EditActiveCellInFormulaBar", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EditActiveCellInFormulaBar");
            _tryApplyFormulaRangeSelection = typeof(MainWindow)
                .GetMethod(
                    "TryApplyFormulaRangeSelection",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: [typeof(CellAddress), typeof(bool)],
                    modifiers: null)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryApplyFormulaRangeSelection");
            _raiseFormulaReferenceGripDragForTest = typeof(MainWindow)
                .GetMethod("RaiseFormulaReferenceGripDragForTest", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RaiseFormulaReferenceGripDragForTest");
            _tryHandleFormulaSheetTabClick = typeof(MainWindow)
                .GetMethod("TryHandleFormulaSheetTabClick", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleFormulaSheetTabClick");
            _tryToggleFormulaRangeEntrySelectionMode = typeof(MainWindow)
                .GetMethod("TryToggleFormulaRangeEntrySelectionMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryToggleFormulaRangeEntrySelectionMode");
            _selectRow = typeof(MainWindow)
                .GetMethod("SelectRow", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SelectRow");
            _selectColumn = typeof(MainWindow)
                .GetMethod("SelectColumn", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SelectColumn");
            _addAdditionalRowSelection = typeof(MainWindow)
                .GetMethod("AddAdditionalRowSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "AddAdditionalRowSelection");
            _selectAll = typeof(MainWindow)
                .GetMethod("SelectAll", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SelectAll");
        }

        /// <summary>
        /// The underlying <see cref="MainWindow"/> instance, for tests that need to invoke a
        /// private method directly by reflection (e.g. a validation helper that is not otherwise
        /// exposed through this harness).
        /// </summary>
        public MainWindow Window => _window;

        public string FormulaBarText => ((TextBox)_window.FindName("FormulaBar")).Text;

        public bool FormulaRangeEntryMode => (bool)_formulaRangeEntryModeField.GetValue(_window)!;

        public CellAddress? SelectionAnchor => (CellAddress?)_selectionAnchorProperty.GetValue(_window);

        public CellAddress? SelectionCursor => (CellAddress?)_selectionCursorField.GetValue(_window);

        public string CellAddressBoxText => CellAddressBox.Text;

        public string StatusReadyText => ((TextBlock)_window.FindName("StatusReadyText")).Text;

        private ComboBox CellAddressBox => (ComboBox)_window.FindName("CellAddressBox");

        private TextBox CellAddressBoxEditableTextBox
        {
            get
            {
                var comboBox = CellAddressBox;
                comboBox.ApplyTemplate();
                return (TextBox)comboBox.Template.FindName("PART_EditableTextBox", comboBox);
            }
        }

        public SheetId CurrentSheetId => (SheetId)_currentSheetIdField.GetValue(_window)!;

        public CellAddress? FormulaEditCell => (CellAddress?)_formulaEditCellField.GetValue(_window);

        public Workbook ActiveWorkbook => Workbook;

        public GridRange? SelectedRange => ((SheetGridView)_window.FindName("SheetGrid")).SelectedRange;

        public double VerticalScrollValue => ((ScrollBar)_window.FindName("VerticalScroll")).Value;

        public double HorizontalScrollValue => ((ScrollBar)_window.FindName("HorizontalScroll")).Value;

        public string? InlineEditorText => InlineEditor?.Text;

        public int? InlineEditorCaretIndex => InlineEditor?.CaretIndex;

        public bool InlineEditorVisible => InlineEditor?.IsVisible == true;

        public bool InlineEditorFocused => InlineEditor is { } inlineEditor && IsFocused(inlineEditor);

        public Color? InlineEditorBackgroundColor => InlineEditor?.Background is SolidColorBrush brush
            ? brush.Color
            : null;

        public double? InlineEditorBackgroundOpacity => InlineEditor?.Background?.Opacity;

        public bool FormulaBarFocused => IsFocused((TextBox)_window.FindName("FormulaBar"));

        public bool CellAddressBoxFocused => IsFocused(CellAddressBox) || IsFocused(CellAddressBoxEditableTextBox);

        public int CellAddressBoxSelectionLength => CellAddressBoxEditableTextBox.SelectionLength;

        public bool SheetGridFocused => IsFocused((SheetGridView)_window.FindName("SheetGrid"));

        public bool FormulaBarAcceptsReturn => ((TextBox)_window.FindName("FormulaBar")).AcceptsReturn;

        public int FormulaBarCaretIndex => ((TextBox)_window.FindName("FormulaBar")).CaretIndex;

        public double FormulaBarHeight => ((TextBox)_window.FindName("FormulaBar")).Height;

        public bool UndoQatEnabled => ((Button)_window.FindName("UndoQatBtn")).IsEnabled;

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

        public void SetCellNumber(uint row, uint col, double value)
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(value)));
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

        public ScalarValue? CellValue(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            return sheet.GetValue(new CellAddress(sheet.Id, row, col));
        }

        public void RenameFirstSheet(string name)
        {
            Workbook.Sheets[0].Name = name;
            PumpDispatcher();
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

        public void SelectSheet(SheetId sheetId)
        {
            _selectSingleSheetTab.Invoke(_window, [sheetId]);
            _updateViewport.Invoke(_window, null);
            _refreshSheetTabs.Invoke(_window, null);
            PumpDispatcher();
        }

        public Sheet AddSheet(string name) => Workbook.AddSheet(name);

        /// <summary>The workbook's first sheet, for tests that need direct model access (e.g. to
        /// register a <see cref="PivotTableModel"/>) beyond what the cell/formula helpers expose.</summary>
        public Sheet FirstSheet => Workbook.Sheets[0];

        public void SelectFormulaSheetTab(SheetId sheetId, ModifierKeys modifiers)
        {
            ((bool)_tryHandleFormulaSheetTabClick.Invoke(_window, [sheetId, modifiers])!).Should().BeTrue();
            PumpDispatcher();
        }

        public void SetCurrentSheetForFormulaPoint(SheetId sheetId)
        {
            _currentSheetIdField.SetValue(_window, sheetId);
            PumpDispatcher();
        }

        public void ShowInlineEditor(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _showInlineEditor.Invoke(_window, [new CellAddress(sheet.Id, row, col)]);
            PumpDispatcher();
        }

        /// <summary>
        /// Drives the real grid-click formula reference-insertion path (the method invoked from
        /// <c>SheetGrid_MouseDown</c> / <c>SheetGrid_MouseMove</c> during point-mode formula entry).
        /// </summary>
        public bool ApplyFormulaRangeSelection(uint row, uint col, bool extend)
        {
            var sheet = Workbook.Sheets[0];
            var applied = (bool)_tryApplyFormulaRangeSelection.Invoke(
                _window,
                [new CellAddress(sheet.Id, row, col), extend])!;
            PumpDispatcher();
            return applied;
        }

        public bool ApplyFormulaRangeSelection(SheetId sheetId, uint row, uint col, bool extend)
        {
            var applied = (bool)_tryApplyFormulaRangeSelection.Invoke(
                _window,
                [new CellAddress(sheetId, row, col), extend])!;
            PumpDispatcher();
            return applied;
        }

        public bool RaiseFormulaReferenceGripDrag(int highlightIndex, uint row, uint col)
        {
            var applied = (bool)_raiseFormulaReferenceGripDragForTest.Invoke(
                _window,
                [highlightIndex, new CellAddress(CurrentSheetId, row, col)])!;
            PumpDispatcher();
            return applied;
        }

        public void ToggleFormulaRangeEntrySelectionMode(ModifierKeys modifiers)
        {
            var toggled = (bool)_tryToggleFormulaRangeEntrySelectionMode.Invoke(
                _window,
                [Key.F8, modifiers])!;
            toggled.Should().BeTrue("F8 selection mode should be handled while formula Point mode is active");
            PumpDispatcher();
        }

        public void SelectWholeRow(uint row)
        {
            _selectRow.Invoke(_window, [row]);
            PumpDispatcher();
        }

        public void SelectWholeColumn(uint col)
        {
            _selectColumn.Invoke(_window, [col]);
            PumpDispatcher();
        }

        public void AddWholeRowFormulaReference(uint row)
        {
            _addAdditionalRowSelection.Invoke(_window, [row]);
            PumpDispatcher();
        }

        public void SelectWholeGrid()
        {
            _selectAll.Invoke(_window, null);
            PumpDispatcher();
        }

        public void InsertFormulaFunction(string functionName)
        {
            _insertRawFormulaFunction.Invoke(_window, [functionName]);
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

        public void ClickFormulaBarCancelButton()
        {
            var button = (Button)_window.FindName("FormulaBarCancelButton");
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
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

        public void SetFormulaBarSelection(int selectionStart, int selectionLength)
        {
            ((TextBox)_window.FindName("FormulaBar")).Select(selectionStart, selectionLength);
            PumpDispatcher();
        }

        public void SetInlineEditorCaretIndex(int caretIndex)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            inlineEditor.CaretIndex = caretIndex;
            PumpDispatcher();
        }

        public void SetInlineEditorSelection(int selectionStart, int selectionLength)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            inlineEditor.Select(selectionStart, selectionLength);
            PumpDispatcher();
        }

        public void SetCellAddressBoxText(string text)
        {
            CellAddressBox.Text = text;
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
            _cellAddressBoxKeyDown.Invoke(_window, [CellAddressBox, args]);
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

        public bool PressInlineEditorKey(Key key)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _inlineEditorKeyDown.Invoke(_window, [inlineEditor, args]);
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
            if (FormulaEditInteractionPlanner.BuildTextChangePlan(text).StartsPointMode)
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

        public void ExecuteCommandDirectly(IWorkbookCommand command)
        {
            _commandBus.Execute(Workbook.Id, command).Success.Should().BeTrue();
            PumpDispatcher();
        }

        public static MainWindowHarness Create(FreeXOptions? options = null)
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
                options: options)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window, commandBus);
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

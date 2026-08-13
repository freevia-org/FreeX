using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Presentation.FormulaBar;
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

        private MainWindowHarness(MainWindow window, ICommandBus commandBus)
        {
            _window = window;
            _commandBus = commandBus;
        }

        /// <summary>
        /// The underlying <see cref="MainWindow"/> instance, for tests that need to invoke a
        /// private method directly by reflection (e.g. a validation helper that is not otherwise
        /// exposed through this harness).
        /// </summary>
        public MainWindow Window => _window;

        public string FormulaBarText => ((TextBox)_window.FindName("FormulaBar")).Text;

        public bool FormulaRangeEntryMode => FormulaRangeEditingSession.PointMode;

        private FormulaRangeEditingSession FormulaRangeEditingSession =>
            _window.FormulaRangeEditingSessionForTest;

        public CellAddress? SelectionAnchor => _window.SelectionAnchorForTest;

        public CellAddress? SelectionCursor => _window.SelectionCursorForTest;

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

        public SheetId CurrentSheetId => _window.CurrentSheetIdForTest;

        public CellAddress? FormulaEditCell => _window.FormulaEditCellForTest;

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

        public void SetFormulaEditCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _window.SetFormulaEditCellForTest(new CellAddress(sheet.Id, row, col));
            PumpDispatcher();
        }

        public bool CommitEditAcrossSelection(bool fillFormulaEditCellOnly)
        {
            var committed = _window.CommitEditAcrossSelectionForTest(fillFormulaEditCellOnly);
            PumpDispatcher();
            return committed;
        }

        public bool CommitEdit()
        {
            var committed = _window.CommitEditForTest();
            PumpDispatcher();
            return committed;
        }

        public void InsertNewSheet()
        {
            _window.InsertNewSheetForTest();
            PumpDispatcher();
        }

        public void SelectSheet(SheetId sheetId)
        {
            _window.SelectSingleSheetTabForTest(sheetId);
            _window.UpdateViewportForTest();
            _window.RefreshSheetTabsForTest();
            PumpDispatcher();
        }

        public Sheet AddSheet(string name) => Workbook.AddSheet(name);

        /// <summary>The workbook's first sheet, for tests that need direct model access (e.g. to
        /// register a <see cref="PivotTableModel"/>) beyond what the cell/formula helpers expose.</summary>
        public Sheet FirstSheet => Workbook.Sheets[0];

        public void SelectFormulaSheetTab(SheetId sheetId, ModifierKeys modifiers)
        {
            _window.TryHandleFormulaSheetTabClickForTest(sheetId, modifiers).Should().BeTrue();
            PumpDispatcher();
        }

        public void SetCurrentSheetForFormulaPoint(SheetId sheetId)
        {
            _window.SetCurrentSheetForFormulaPointForTest(sheetId);
            PumpDispatcher();
        }

        public void ShowInlineEditor(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _window.ShowInlineEditorForTest(new CellAddress(sheet.Id, row, col));
            PumpDispatcher();
        }

        /// <summary>
        /// Drives the real grid-click formula reference-insertion path (the method invoked from
        /// <c>SheetGrid_MouseDown</c> / <c>SheetGrid_MouseMove</c> during point-mode formula entry).
        /// </summary>
        public bool ApplyFormulaRangeSelection(uint row, uint col, bool extend)
        {
            var sheet = Workbook.Sheets[0];
            var applied = _window.TryApplyFormulaRangeSelectionForTest(
                new CellAddress(sheet.Id, row, col), extend);
            PumpDispatcher();
            return applied;
        }

        public bool ApplyFormulaRangeSelection(SheetId sheetId, uint row, uint col, bool extend)
        {
            var applied = _window.TryApplyFormulaRangeSelectionForTest(
                new CellAddress(sheetId, row, col), extend);
            PumpDispatcher();
            return applied;
        }

        public bool RaiseFormulaReferenceGripDrag(int highlightIndex, uint row, uint col)
        {
            var applied = _window.RaiseFormulaReferenceGripDragForTest(
                highlightIndex, new CellAddress(CurrentSheetId, row, col));
            PumpDispatcher();
            return applied;
        }

        public void ToggleFormulaRangeEntrySelectionMode(ModifierKeys modifiers)
        {
            var toggled = _window.TryToggleFormulaRangeEntrySelectionModeForTest(Key.F8, modifiers);
            toggled.Should().BeTrue("F8 selection mode should be handled while formula Point mode is active");
            PumpDispatcher();
        }

        public void SelectWholeRow(uint row)
        {
            _window.SelectRowForTest(row);
            PumpDispatcher();
        }

        public void SelectWholeColumn(uint col)
        {
            _window.SelectColumnForTest(col);
            PumpDispatcher();
        }

        public void AddWholeRowFormulaReference(uint row)
        {
            _window.AddAdditionalRowSelectionForTest(row);
            PumpDispatcher();
        }

        public void SelectWholeGrid()
        {
            _window.SelectAllForTest();
            PumpDispatcher();
        }

        public void InsertFormulaFunction(string functionName)
        {
            _window.InsertRawFormulaFunctionForTest(functionName);
            PumpDispatcher();
        }

        public void InsertDefinedNameIntoFormula(string name)
        {
            _window.InsertDefinedNameIntoFormulaForTest(name);
            PumpDispatcher();
        }

        public void ToggleFormulaBarExpansion()
        {
            _window.ToggleFormulaBarExpansionForTest();
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
            _window.EditActiveCellInFormulaBarForTest();
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
            _window.RaiseCellAddressBoxKeyDownForTest(args);
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

        public void SetInlineEditorText(string text)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            inlineEditor.Text = text;
            UpdateFormulaRangeEntryMode(text);
            PumpDispatcher();
        }

        private void UpdateFormulaRangeEntryMode(string text)
        {
            FormulaRangeEditingSession.ApplyTextChanged(text);
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
            _window.ExecuteClearSelectionForTest();
            PumpDispatcher();
        }

        public void ExecuteCommandDirectly(IWorkbookCommand command)
        {
            _commandBus.Execute(Workbook.Id, command).Success.Should().BeTrue();
            PumpDispatcher();
        }

        public static MainWindowHarness Create(AppOptions? options = null)
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

        private Workbook Workbook => _window.Session.Workbook;

        private TextBox? InlineEditor => _window.InlineEditorForTest;

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

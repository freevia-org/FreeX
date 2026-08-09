using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shell;
using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    private sealed class MainWindowHarness : IDisposable
    {
        private static SharedMainWindowSession? SharedSession;

        private readonly MainWindow _window;
        private readonly Workbook _workbook;
        private readonly MethodInfo _enterKeyTipMode;
        private readonly MethodInfo _handleActiveRibbonKeyTip;
        private readonly MethodInfo _tryHandleDirectRibbonKeyTip;
        private readonly MethodInfo _tryHandleFocusedRibbonKeyboardNavigation;
        private readonly MethodInfo _isInsideRibbonSurface;
        private readonly MethodInfo _getVisibleKeyTipElements;
        private readonly MethodInfo _updateRibbonCompactMode;
        private readonly MethodInfo _updateViewport;
        private readonly MethodInfo _updateSsRecentList;
        private readonly MethodInfo _refreshSheetProtectionUi;
        private readonly MethodInfo _hideStartScreen;
        private readonly MethodInfo _rebuildQuickAccessToolbar;
        private readonly MethodInfo _zoomCustomMenuItemClick;
        private readonly MethodInfo _setActiveCell;
        private readonly PropertyInfo _selectionAnchorProperty;
        private readonly FieldInfo _selectionCursorField;
        private readonly MethodInfo _applyPivotFieldListLayout;
        private readonly MethodInfo _movePivotFieldToZone;
        private readonly Type _pivotFieldDropZoneType;
        private readonly Type _scopeType;
        private readonly FieldInfo _scopeField;
        private readonly FieldInfo _activeMenuField;
        private readonly FieldInfo _recentFilesField;
        private readonly FieldInfo _optionsField;
        private readonly MethodInfo _refreshReviewCommentNoteCommandStates;
        private readonly RecordingUserMessageService _messageService;

        private MainWindowHarness(
            MainWindow window,
            Workbook workbook,
            RecordingUserMessageService messageService)
        {
            _window = window;
            _workbook = workbook;
            _messageService = messageService;
            _enterKeyTipMode = typeof(MainWindow).GetMethod("EnterRibbonKeyTipMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EnterRibbonKeyTipMode");
            _handleActiveRibbonKeyTip = typeof(MainWindow).GetMethod("HandleActiveRibbonKeyTip", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "HandleActiveRibbonKeyTip");
            _tryHandleDirectRibbonKeyTip = typeof(MainWindow).GetMethod("TryHandleDirectRibbonKeyTip", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleDirectRibbonKeyTip");
            _tryHandleFocusedRibbonKeyboardNavigation = typeof(MainWindow).GetMethod("TryHandleFocusedRibbonKeyboardNavigation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleFocusedRibbonKeyboardNavigation");
            _isInsideRibbonSurface = typeof(MainWindow).GetMethod("IsInsideRibbonSurface", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "IsInsideRibbonSurface");
            _getVisibleKeyTipElements = typeof(MainWindow).GetMethod("GetVisibleKeyTipElements", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "GetVisibleKeyTipElements");
            _updateRibbonCompactMode = typeof(MainWindow).GetMethod("UpdateRibbonCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
            _updateViewport = typeof(MainWindow).GetMethod("UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateViewport");
            _updateSsRecentList = typeof(MainWindow).GetMethod("UpdateSsRecentList", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateSsRecentList");
            _refreshSheetProtectionUi = typeof(MainWindow).GetMethod("RefreshSheetProtectionUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RefreshSheetProtectionUi");
            _hideStartScreen = typeof(MainWindow).GetMethod("HideStartScreen", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "HideStartScreen");
            _rebuildQuickAccessToolbar = typeof(MainWindow).GetMethod("RebuildQuickAccessToolbar", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RebuildQuickAccessToolbar");
            _zoomCustomMenuItemClick = typeof(MainWindow).GetMethod("ZoomCustomMenuItem_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ZoomCustomMenuItem_Click");
            _setActiveCell = typeof(MainWindow).GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _selectionAnchorProperty = typeof(MainWindow).GetProperty("_selectionAnchor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "_selectionAnchor");
            _selectionCursorField = typeof(MainWindow).GetField("_selectionCursor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_selectionCursor");
            _applyPivotFieldListLayout = typeof(MainWindow).GetMethod("ApplyPivotFieldListLayout", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ApplyPivotFieldListLayout");
            _movePivotFieldToZone = typeof(MainWindow).GetMethod("MovePivotFieldToZone", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "MovePivotFieldToZone");
            _pivotFieldDropZoneType = typeof(MainWindow).GetNestedType("PivotFieldDropZone", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "PivotFieldDropZone");
            _scopeType = typeof(MainWindow).GetNestedType("RibbonKeyTipScope", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "RibbonKeyTipScope");
            _scopeField = typeof(MainWindow).GetField("_ribbonKeyTipScope", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_ribbonKeyTipScope");
            _activeMenuField = typeof(MainWindow).GetField("_activeRibbonKeyTipMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_activeRibbonKeyTipMenu");
            _recentFilesField = typeof(MainWindow).GetField("_recentFiles", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_recentFiles");
            _optionsField = typeof(MainWindow).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_options");
            _refreshReviewCommentNoteCommandStates = typeof(MainWindow).GetMethod(
                "RefreshReviewCommentNoteCommandStates", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RefreshReviewCommentNoteCommandStates");
        }

        public string? SelectedRibbonTabHeader =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem is TabItem tab
                ? tab.Header?.ToString()
                : null;

        public string KeyTipScope => _scopeField.GetValue(_window)?.ToString() ?? "";

        public bool? IsToggleChecked(string name) =>
            (_window.FindName(name) as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked;

        public IReadOnlyList<string> OverlayBadgeTexts =>
            (_window.FindName("KeyTipOverlay") as Canvas)?.Children
                .OfType<Border>()
                .Select(border => (border.Child as TextBlock)?.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Cast<string>()
                .ToList() ?? [];

        public Rect OverlayBadgeBounds(string text)
        {
            var overlay = (_window.FindName("KeyTipOverlay") as Canvas)
                ?? throw new InvalidOperationException("KeyTipOverlay was not found.");
            var badges = overlay.Children
                .OfType<Border>()
                .Where(border => string.Equals((border.Child as TextBlock)?.Text, text, StringComparison.Ordinal))
                .ToList();
            badges.Should().ContainSingle($"the overlay should contain one {text} badge");

            var badge = badges[0];
            var width = badge.ActualWidth > 0 ? badge.ActualWidth : badge.DesiredSize.Width;
            var height = badge.ActualHeight > 0 ? badge.ActualHeight : badge.DesiredSize.Height;
            return new Rect(Canvas.GetLeft(badge), Canvas.GetTop(badge), width, height);
        }

        public Rect ElementBounds(string name)
        {
            var root = (_window.FindName("RootGrid") as FrameworkElement)
                ?? throw new InvalidOperationException("RootGrid was not found.");
            var element = (_window.FindName(name) as FrameworkElement)
                ?? throw new InvalidOperationException($"{name} was not found.");
            return element.TransformToAncestor(root)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }

        public Rect RibbonButtonBoundsByTitle(string title)
        {
            var root = (_window.FindName("RootGrid") as FrameworkElement)
                ?? throw new InvalidOperationException("RootGrid was not found.");
            var matches = SelectedRibbonCommandButtons()
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), title, StringComparison.Ordinal))
                .ToList();
            matches.Should().ContainSingle($"the selected ribbon tab should expose one visible {title} button");

            var button = matches[0];
            return button.TransformToAncestor(root)
                .TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
        }

        public bool ActiveMenuIsOpen => ActiveMenu?.IsOpen == true;

        public (string Title, string Message)? LastInfoMessage => _messageService.LastInfo;

        public bool FocusedElementIsInsideRibbon =>
            Keyboard.FocusedElement is DependencyObject focusedElement &&
            (bool)_isInsideRibbonSurface.Invoke(_window, [focusedElement])!;

        public bool FocusedElementIsWorksheet =>
            ReferenceEquals(Keyboard.FocusedElement, _window.FindName("SheetGrid"));

        public bool StartScreenIsVisible =>
            (_window.FindName("StartScreenOverlay") as FrameworkElement)?.Visibility == Visibility.Visible;

        public bool NumberFormatDropDownIsOpen =>
            (_window.FindName("NumberFormatBox") as ComboBox)?.IsDropDownOpen == true;

        public bool NumberFormatBoxHasKeyboardFocus =>
            (_window.FindName("NumberFormatBox") as ComboBox)?.IsKeyboardFocusWithin == true ||
            ReferenceEquals(Keyboard.FocusedElement, _window.FindName("NumberFormatBox"));

        public bool UndoQatIsEnabled =>
            (_window.FindName("UndoQatBtn") as Button)?.IsEnabled == true;

        public bool RedoQatIsEnabled =>
            (_window.FindName("RedoQatBtn") as Button)?.IsEnabled == true;

        public bool UndoQatHistoryIsEnabled =>
            (_window.FindName("UndoQatHistoryBtn") as Button)?.IsEnabled == true;

        public bool RedoQatHistoryIsEnabled =>
            (_window.FindName("RedoQatHistoryBtn") as Button)?.IsEnabled == true;

        public bool TitleBarQatIsVisible =>
            (_window.FindName("TitleBarQatPanel") as FrameworkElement)?.Visibility == Visibility.Visible;

        public bool BelowRibbonQatIsVisible =>
            (_window.FindName("BelowRibbonQatRoot") as FrameworkElement)?.Visibility == Visibility.Visible;

        public bool? NamedButtonIsEnabled(string name) =>
            (_window.FindName(name) as Button)?.IsEnabled;

        public string? StatusZoomText =>
            (_window.FindName("StatusZoomText") as TextBlock)?.Text;

        public int ExpectedZoomSelectionPercent
        {
            get
            {
                if (_window.FindName("SheetGrid") is not SheetGridView { SelectedRange: { } range } sheetGrid)
                    return 100;

                return (int)Math.Round(ZoomSelectionPlanner.CalculateFitPercent(
                    sheetGrid.ActualWidth,
                    sheetGrid.ActualHeight,
                    range.ColCount,
                    range.RowCount));
            }
        }

        public (bool ShowGridlines, bool ShowHeadings, bool ShowRulers) ActiveSheetViewOptions
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.ShowGridlines, sheet.ShowHeadings, sheet.ShowRulers);
            }
        }

        public bool FormulaBarIsVisible =>
            (_window.FindName("FormulaBarBorder") as FrameworkElement)?.Visibility == Visibility.Visible;

        public WorksheetViewMode ActiveSheetViewMode => _workbook.Sheets[0].ViewMode;

        public bool ActiveSheetHasAutoFilter => _workbook.Sheets[0].AutoFilter is not null;

        public IReadOnlyList<uint> ActiveSheetRowPageBreaks => _workbook.Sheets[0].RowPageBreaks.ToList();

        public IReadOnlyList<uint> ActiveSheetColumnPageBreaks => _workbook.Sheets[0].ColumnPageBreaks.ToList();

        public WorksheetPageMargins ActiveSheetPageMargins => _workbook.Sheets[0].PageMargins;

        public WorksheetPageOrientation ActiveSheetPageOrientation => _workbook.Sheets[0].PageOrientation;

        public WorksheetPaperSize ActiveSheetPaperSize => _workbook.Sheets[0].PaperSize;

        public (bool PrintGridlines, bool PrintHeadings) ActiveSheetPrintOptions
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.PrintGridlines, sheet.PrintHeadings);
            }
        }

        public (uint StartRow, uint StartCol, uint EndRow, uint EndCol)? ActiveSheetPrintArea
        {
            get
            {
                var range = _workbook.Sheets[0].PrintArea;
                return range is { } value
                    ? (value.Start.Row, value.Start.Col, value.End.Row, value.End.Col)
                    : null;
            }
        }

        public WorkbookCalculationMode WorkbookCalculationMode => _workbook.CalculationMode;

        public (uint FrozenRows, uint FrozenCols) ActiveSheetFrozenPanes
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.FrozenRows, sheet.FrozenCols);
            }
        }

        public (uint? SplitRow, uint? SplitColumn) ActiveSheetSplitPanes
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.SplitRow, sheet.SplitColumn);
            }
        }

        public (uint Row, uint Col)? SelectedCellAddress
        {
            get
            {
                if (_window.FindName("SheetGrid") is not SheetGridView { SelectedRange: { } range })
                    return null;

                return (range.Start.Row, range.Start.Col);
            }
        }

        public bool ActiveCellBold
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                var address = new CellAddress(sheet.Id, 1, 1);
                var styleId = sheet.GetCell(address)?.StyleId
                    ?? sheet.GetStyleOnly(address.Row, address.Col)
                    ?? StyleId.Default;
                return _workbook.GetStyle(styleId).Bold;
            }
        }

        public void SetNumber(uint row, uint col, double value)
        {
            var sheet = _workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));
            PumpDispatcher();
        }

        public string? CellFormulaText(uint row, uint col)
        {
            var sheet = _workbook.Sheets[0];
            return sheet.GetCell(new CellAddress(sheet.Id, row, col))?.FormulaText;
        }

        public void SelectActiveCell()
        {
            var sheet = _workbook.Sheets[0];
            var address = new CellAddress(sheet.Id, 1, 1);
            if (_window.FindName("SheetGrid") is SheetGridView sheetGrid)
            {
                sheetGrid.SelectedObjectId = Guid.Empty;
                sheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.None;
                sheetGrid.SelectedRange = new GridRange(address, address);

                // Keep _selectionAnchor/_selectionCursor consistent with the reset selection so a
                // prior SelectRange's anchor doesn't linger into the next test (see SelectRange).
                _selectionAnchorProperty.SetValue(_window, address);
                _selectionCursorField.SetValue(_window, address);
            }
            PumpDispatcher();
        }

        public void SelectFirstChartObject()
        {
            var chart = _workbook.Sheets[0].Charts[0];
            SelectDrawingObject(chart.Id, FreeX.App.UI.ObjectKind.Chart);
        }

        public void SelectFirstShapeObject()
        {
            var shape = _workbook.Sheets[0].DrawingShapes[0];
            SelectDrawingObject(shape.Id, FreeX.App.UI.ObjectKind.Shape);
        }

        public void SelectFirstPictureObject()
        {
            var picture = _workbook.Sheets[0].Pictures[0];
            SelectDrawingObject(picture.Id, FreeX.App.UI.ObjectKind.Picture);
        }

        public void SelectFirstTextBoxObject()
        {
            var textBox = _workbook.Sheets[0].TextBoxes[0];
            SelectDrawingObject(textBox.Id, FreeX.App.UI.ObjectKind.TextBox);
        }

        private void SelectDrawingObject(Guid objectId, FreeX.App.UI.ObjectKind kind)
        {
            if (_window.FindName("SheetGrid") is SheetGridView sheetGrid)
            {
                sheetGrid.SelectedObjectId = objectId;
                sheetGrid.SelectedObjectKind = kind;
            }

            RefreshViewport();
        }

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheet = _workbook.Sheets[0];
            if (_window.FindName("SheetGrid") is SheetGridView sheetGrid)
            {
                var start = new CellAddress(sheet.Id, startRow, startCol);
                var end = new CellAddress(sheet.Id, endRow, endCol);
                sheetGrid.SelectedRanges = null;
                sheetGrid.SelectedRange = new GridRange(start, end);

                // SheetGrid.SelectedRange is a visual-only DependencyProperty; setting it alone
                // does NOT update the window's _selectionAnchor/_selectionCursor the way a real
                // selection (SetActiveCell/SetSelectionRange in MainWindow.Selection.cs) does.
                // Leaving the anchor stale makes anchor-driven commands -- Freeze Panes and Split
                // -- resolve their position from the previous active cell (e.g. A1) instead of this
                // selection, so mirror the anchor/cursor here to model a genuine selection.
                _selectionAnchorProperty.SetValue(_window, start);
                _selectionCursorField.SetValue(_window, end);
            }

            PumpDispatcher();
        }

        public void SetActiveCell(uint row, uint col)
        {
            var sheet = _workbook.Sheets[0];
            _setActiveCell.Invoke(_window, [new CellAddress(sheet.Id, row, col)]);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public GridRange? SelectedRange =>
            (_window.FindName("SheetGrid") as SheetGridView)?.SelectedRange;

        // Mirrors MainWindow's private _selectionAnchor (the true active/anchor cell of the
        // current selection). Anchor-driven ribbon commands -- Freeze Panes and Split -- resolve
        // their position from this cell, so tests assert it stays in sync with SelectRange.
        public (uint Row, uint Col)? SelectionAnchor =>
            _selectionAnchorProperty.GetValue(_window) is CellAddress addr ? (addr.Row, addr.Col) : null;

        public GridRange ActivePivotVisibleRange =>
            PivotUiPlanner.VisiblePivotRange(_workbook.Sheets[0].PivotTables[0]);

        public IReadOnlyList<string> PivotListItems(string listName) =>
            (_window.FindName(listName) as ListBox)?.Items
                .Cast<object>()
                .Select(item => PivotFieldListPaneBuilder.GetItemCaption(item) ?? item.ToString() ?? string.Empty)
                .Where(item => item.Length > 0)
                .ToList() ?? [];

        public void ApplyPivotLayoutWithoutRowFields()
        {
            var pivot = _workbook.Sheets[0].PivotTables[0];
            _applyPivotFieldListLayout.Invoke(
                _window,
                [
                    pivot,
                    Array.Empty<PivotFieldModel>(),
                    Array.Empty<PivotFieldModel>(),
                    Array.Empty<PivotFieldModel>(),
                    pivot.DataFields.ToList(),
                    true
                ]);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void MoveAvailablePivotFieldTo(string caption, string zoneName)
        {
            var zone = Enum.Parse(_pivotFieldDropZoneType, zoneName);
            _movePivotFieldToZone.Invoke(_window, [caption, zone, -1, null]);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public string? SetButtonKeyTip(string name, string keyTip)
        {
            var button = (_window.FindName(name) as ButtonBase)
                ?? throw new InvalidOperationException($"Button {name} was not found.");
            var originalKeyTip = RibbonTooltip.GetKeyTip(button);
            RibbonTooltip.SetKeyTip(button, keyTip);
            PumpDispatcher();
            return originalKeyTip;
        }

        public bool ButtonIsInBelowRibbonQat(string name) =>
            _window.FindName(name) is Button button &&
            ReferenceEquals(button.Parent, _window.FindName("BelowRibbonQatPanel"));

        public IReadOnlyList<string> QuickAccessToolbarAutomationIds =>
            QuickAccessToolbarButtons()
                .Select(AutomationProperties.GetAutomationId)
                .ToList();

        public IReadOnlyList<string> QuickAccessToolbarKeyTips =>
            QuickAccessToolbarButtons()
                .Select(button => RibbonTooltip.GetKeyTip(button) ?? "")
                .ToList();

        public IReadOnlyList<bool> QuickAccessToolbarChromeHitTestVisibility =>
            QuickAccessToolbarButtons()
                .Select(WindowChrome.GetIsHitTestVisibleInChrome)
                .ToList();

        public void ConfigureQuickAccessToolbar(IReadOnlyList<string> commandIds, bool belowRibbon)
        {
            var options = (AppOptions)_optionsField.GetValue(_window)!;
            options.QuickAccessToolbarCommands = commandIds.ToList();
            options.QuickAccessToolbarBelowRibbon = belowRibbon;
            _rebuildQuickAccessToolbar.Invoke(_window, null);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public IDisposable AddHomeRibbonCommandButton(string keyTip, string title)
        {
            var panel = (_window.FindName("HomeRibbonPanel") as Panel)
                ?? throw new InvalidOperationException("HomeRibbonPanel was not found.");
            var button = new Button
            {
                Content = title,
                Width = 96,
                Height = 28,
                IsEnabled = true
            };
            RibbonTooltip.SetTitle(button, title);
            RibbonTooltip.SetKeyTip(button, keyTip);

            panel.Children.Add(button);
            _window.UpdateLayout();
            PumpDispatcher();

            return new DisposableAction(() =>
            {
                panel.Children.Remove(button);
                _window.UpdateLayout();
                PumpDispatcher();
            });
        }

        public void AddNote(uint row, uint col, string text)
        {
            var sheet = _workbook.Sheets[0];
            sheet.Comments[new CellAddress(sheet.Id, row, col)] = text;
            // R127-review-delete-enablement-1: a real note/comment mutation always goes through
            // ApplyReviewRefreshPlan, which re-syncs the Review command states (Next/Previous
            // Note/Comment, Delete Note/Comment, Convert to Comments) from the sheet -- mirror
            // that here since this helper mutates the sheet model directly, bypassing that path.
            _refreshReviewCommentNoteCommandStates.Invoke(_window, null);
            PumpDispatcher();
        }

        public void AddThreadedComment(uint row, uint col, string text)
        {
            var sheet = _workbook.Sheets[0];
            sheet.ThreadedComments[new CellAddress(sheet.Id, row, col)] = new ThreadedComment(text);
            // See AddNote above (R127-review-delete-enablement-1).
            _refreshReviewCommentNoteCommandStates.Invoke(_window, null);
            PumpDispatcher();
        }

        public int RowOutlineLevel(uint row)
        {
            var sheet = _workbook.Sheets[0];
            return sheet.RowOutlineLevels.TryGetValue(row, out var level) ? level : 0;
        }

        public int DrawingShapeCount => _workbook.Sheets[0].DrawingShapes.Count;

        public DrawingShapeKind? LastDrawingShapeKind => _workbook.Sheets[0].DrawingShapes.LastOrDefault()?.Kind;

        public (uint Row, uint Col)? LastDrawingShapeAnchor
        {
            get
            {
                var anchor = _workbook.Sheets[0].DrawingShapes.LastOrDefault()?.Anchor;
                return anchor is { } value ? (value.Row, value.Col) : null;
            }
        }

        public int ChartCount => _workbook.Sheets[0].Charts.Count;

        public ChartType? LastChartType => _workbook.Sheets[0].Charts.LastOrDefault()?.Type;

        public void ClearCharts()
        {
            _workbook.Sheets[0].Charts.Clear();
            PumpDispatcher();
        }

        public string? ActiveMenuItemGestureText(string header) =>
            FindActiveMenuItem(header)?.InputGestureText;

        public bool? ActiveMenuItemIsEnabled(string header) =>
            FindActiveMenuItem(header)?.IsEnabled;

        public bool? ActiveMenuItemIsChecked(string header) =>
            FindActiveMenuItem(header)?.IsChecked;

        public bool ActiveMenuItemSubmenuIsOpen(string header) =>
            FindActiveMenuItem(header)?.IsSubmenuOpen == true;

        public WorkbookWindowArrangement WorkbookArrangement =>
            _workbook.WindowArrangement;

        public IReadOnlyList<string> VisibleCommandKeyTips(string keyTip)
        {
            var scope = Enum.Parse(_scopeType, "Commands");
            var elements = ((System.Collections.IEnumerable)_getVisibleKeyTipElements.Invoke(_window, [scope])!)
                .OfType<FrameworkElement>()
                .Where(element => string.Equals(RibbonTooltip.GetKeyTip(element), keyTip, StringComparison.OrdinalIgnoreCase))
                .Select(element => RibbonTooltip.GetTitle(element) ?? element.Name ?? element.GetType().Name)
                .ToList();
            return elements;
        }

        public IReadOnlyList<string> VisibleCommandKeyTipDump()
        {
            var scope = Enum.Parse(_scopeType, "Commands");
            return ((System.Collections.IEnumerable)_getVisibleKeyTipElements.Invoke(_window, [scope])!)
                .OfType<FrameworkElement>()
                .Select(element => $"{RibbonTooltip.GetKeyTip(element)}:{RibbonTooltip.GetTitle(element) ?? element.Name ?? element.GetType().Name}")
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ----- Declarative 2-state collapse helpers (live RibbonAdaptivePanel -> RibbonGroupHost) -----

        private DependencyObject? SelectedRibbonContentRoot
        {
            get
            {
                if (_window.FindName("RibbonTabs") is not TabControl { SelectedItem: TabItem tab })
                    return null;
                return tab.Content as DependencyObject ?? tab;
            }
        }

        private RibbonGroupHost? FindRibbonGroupHost(string groupName)
        {
            if (SelectedRibbonContentRoot is not { } root)
                return null;
            return WpfTestTree.FindVisualSelfAndDescendants<RibbonGroupHost>(root)
                .FirstOrDefault(host => string.Equals(host.GroupName, groupName, StringComparison.Ordinal));
        }

        public bool RibbonGroupIsCollapsed(string groupName) =>
            FindRibbonGroupHost(groupName)?.Collapsed == true;

        // The single overflow button a group folds into when collapsed (RibbonGroupHost swaps its content
        // to this button). Carries the group's derived keytip + title and a ContextMenu of the group's
        // commands. Returns null when the group is expanded or has no overflow button.
        private Button? CollapsedRibbonGroupOverflowButton(string groupName)
        {
            if (FindRibbonGroupHost(groupName) is not { Collapsed: true } host)
                return null;
            return WpfTestTree.FindVisualSelfAndDescendants<Button>(host)
                .FirstOrDefault(RibbonMetadata.IsCollapsedGroupButton);
        }

        public string? CollapsedRibbonGroupOverflowKeyTip(string groupName) =>
            CollapsedRibbonGroupOverflowButton(groupName) is { } button
                ? RibbonTooltip.GetKeyTip(button)
                : null;

        public string? CollapsedRibbonGroupOverflowTitle(string groupName) =>
            CollapsedRibbonGroupOverflowButton(groupName) is { } button
                ? RibbonTooltip.GetTitle(button)
                : null;

        // The collapsed overflow button's rendered width. The live 2-state engine sizes a collapsed group
        // to RibbonGroupHost.CollapsedWidth (~58px); a zero width means the overflow button never paints
        // and the whole group is unreachable from the ribbon (see flagged Charts deviation).
        public double CollapsedRibbonGroupOverflowWidth(string groupName) =>
            CollapsedRibbonGroupOverflowButton(groupName)?.ActualWidth ?? 0;

        // Opens the collapsed group's overflow dropdown (same path the button's click takes) and returns
        // the per-command keytips keyed by command header, so a test can assert the group's command set
        // (e.g. Column Chart -> CC) survives collapse and that deferred commands stay absent.
        public IReadOnlyDictionary<string, string?> CollapsedRibbonGroupOverflowMenuKeyTips(string groupName)
        {
            var result = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (CollapsedRibbonGroupOverflowButton(groupName) is not { ContextMenu: { } menu })
                return result;

            menu.IsOpen = true;
            PumpDispatcher();
            try
            {
                foreach (var item in EnumerateMenuItems(menu))
                {
                    var header = NormalizeMenuHeader(item.Header);
                    if (header.Length > 0)
                        result[header] = RibbonTooltip.GetKeyTip(item);
                }
            }
            finally
            {
                menu.IsOpen = false;
                PumpDispatcher();
            }

            return result;
        }

        // A collapsed group's overflow button carries the dropdown chevron glyph ("▾") so it reads as an
        // openable group, mirroring Excel's collapsed-group affordance.
        public bool CollapsedRibbonGroupOverflowHasDropdownGlyph(string groupName) =>
            CollapsedRibbonGroupOverflowButton(groupName) is { } button &&
            WpfTestTree.FindVisualSelfAndDescendants<TextBlock>(button)
                .Any(block => block.Text.Contains('▾'));

        // ----- Declarative ribbon combo-box (e.g. Number Format) live state -----

        private ComboBox? SelectedRibbonComboBox(string title)
        {
            if (SelectedRibbonContentRoot is not { } root)
                return null;
            return WpfTestTree.FindVisualSelfAndDescendants<ComboBox>(root)
                .FirstOrDefault(box => string.Equals(RibbonTooltip.GetTitle(box), title, StringComparison.Ordinal));
        }

        public string? RibbonComboBoxKeyTip(string title) =>
            SelectedRibbonComboBox(title) is { } box ? RibbonTooltip.GetKeyTip(box) : null;

        public bool? RibbonComboBoxIsEnabled(string title) =>
            SelectedRibbonComboBox(title)?.IsEnabled;

        public bool RibbonComboBoxDropDownIsOpen(string title) =>
            SelectedRibbonComboBox(title)?.IsDropDownOpen == true;

        public bool RibbonComboBoxHasKeyboardFocus(string title) =>
            SelectedRibbonComboBox(title) is { } box &&
            (box.IsKeyboardFocusWithin || ReferenceEquals(Keyboard.FocusedElement, box));

        public bool? CommandButtonIsEnabled(string name) =>
            (_window.FindName(name) as ButtonBase)?.IsEnabled;

        public string? CommandButtonHelpText(string name) =>
            _window.FindName(name) is DependencyObject element
                ? AutomationProperties.GetHelpText(element)
                : null;

        public void ShowPivotContextualTabs()
        {
            if (_window.FindName("PivotTableAnalyzeTab") is TabItem analyzeTab)
                analyzeTab.Visibility = Visibility.Visible;
            if (_window.FindName("PivotTableDesignTab") is TabItem designTab)
                designTab.Visibility = Visibility.Visible;

            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void RefreshViewport()
        {
            _updateViewport.Invoke(_window, null);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public bool ContextualTabIsVisible(string name) =>
            (_window.FindName(name) as TabItem)?.Visibility == Visibility.Visible;

        public bool PivotFieldListPaneIsVisible =>
            (_window.FindName("PivotFieldListPane") as FrameworkElement)?.Visibility == Visibility.Visible;

        private ContextMenu? ActiveMenu => _activeMenuField.GetValue(_window) as ContextMenu;

        private IEnumerable<ButtonBase> SelectedRibbonCommandButtons()
        {
            if (_window.FindName("RibbonTabs") is not TabControl { SelectedItem: TabItem selectedTab })
                return [];

            var root = selectedTab.Content as DependencyObject ?? selectedTab;
            return WpfTestTree.FindVisualSelfAndDescendants<DependencyObject>(root)
                .Concat(WpfTestTree.FindLogicalDescendants<DependencyObject>(root))
                .OfType<ButtonBase>()
                .Distinct()
                .Where(button => button.IsVisible);
        }

        private IEnumerable<Button> QuickAccessToolbarButtons()
        {
            if (_window.FindName("TitleBarQatPanel") is Panel titlePanel)
            {
                foreach (var button in titlePanel.Children.OfType<Button>())
                    yield return button;
            }

            if (_window.FindName("BelowRibbonQatPanel") is Panel belowRibbonPanel)
            {
                foreach (var button in belowRibbonPanel.Children.OfType<Button>())
                    yield return button;
            }
        }

        private MenuItem? FindActiveMenuItem(string header) =>
            ActiveMenu is { } menu
                ? EnumerateMenuItems(menu).FirstOrDefault(item => string.Equals(NormalizeMenuHeader(item.Header), header, StringComparison.Ordinal))
                : null;

        private static string NormalizeMenuHeader(object? header) =>
            header?.ToString()?.Replace("_", string.Empty, StringComparison.Ordinal) ?? string.Empty;

        public static MainWindowHarness Create(Action<Workbook>? configureWorkbook = null)
        {
            var session = SharedSession ??= CreateSharedSession();
            var window = session.Window;
            if (!window.IsVisible)
                window.Show();
            window.Activate();

            window.WindowState = WindowState.Normal;
            window.Width = 2400;
            window.Height = 720;
            if (window.FindName("RibbonTabs") is TabControl ribbonTabs)
                ribbonTabs.Width = 2400;
            window.UpdateLayout();
            PumpDispatcher();

            var createNewWorkbook = typeof(MainWindow).GetMethod("CreateNewWorkbook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateNewWorkbook");
            createNewWorkbook.Invoke(window, null);
            configureWorkbook?.Invoke(session.WorkbookRef.Current);

            var harness = new MainWindowHarness(window, session.WorkbookRef.Current, session.MessageService);
            harness.ResetUiState();
            return harness;
        }

        private static SharedMainWindowSession CreateSharedSession()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var messageService = new RecordingUserMessageService();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                messageService);

            window.WindowState = WindowState.Normal;
            window.Width = 2400;
            window.Height = 720;
            window.Show();
            window.Activate();
            if (window.FindName("RibbonTabs") is TabControl ribbonTabs)
                ribbonTabs.Width = 2400;
            window.UpdateLayout();
            PumpDispatcher();
            return new SharedMainWindowSession(window, workbookRef, messageService);
        }

        public void SetRibbonWidth(double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = width;
                ribbonTabs.SelectedIndex = 1;
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void SelectRibbonTab(string header, double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = width;
                ribbonTabs.SelectedItem = ribbonTabs.Items
                    .OfType<TabItem>()
                    .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
            PumpDispatcher();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void SetRecentFiles(IEnumerable<string> recentPaths, IEnumerable<string> pinnedPaths)
        {
            var store = (RecentFilesStore)_recentFilesField.GetValue(_window)!;
            store.Entries.Clear();
            store.Entries.AddRange(recentPaths.Select(path => new RecentFileEntry
            {
                Path = path,
                LastOpened = DateTimeOffset.UtcNow,
                IsPinned = false
            }));
            store.Entries.AddRange(pinnedPaths.Select(path => new RecentFileEntry
            {
                Path = path,
                LastOpened = DateTimeOffset.UtcNow,
                IsPinned = true
            }));
            _updateSsRecentList.Invoke(_window, [""]);
            PumpDispatcher();
        }

        public void RefreshSheetProtectionUi()
        {
            _refreshSheetProtectionUi.Invoke(_window, null);
            PumpDispatcher();
        }

        public void EnterKeyTipScope(string scope)
        {
            var value = Enum.Parse(_scopeType, scope);
            _enterKeyTipMode.Invoke(_window, [value]);
            PumpDispatcher();
        }

        public void HandleKeyTip(Key key)
        {
            _handleActiveRibbonKeyTip.Invoke(_window, [key]);
            PumpDispatcher();
        }

        public bool HandleDirectTopLevelKeyTip(Key key)
        {
            var handled = (bool)_tryHandleDirectRibbonKeyTip.Invoke(_window, [key])!;
            PumpDispatcher();
            return handled;
        }

        public bool FocusSelectedRibbonTab()
        {
            if (_window.FindName("RibbonTabs") is not TabControl { SelectedItem: TabItem tab })
                return false;

            var focused = tab.Focus();
            Keyboard.Focus(tab);
            PumpDispatcher();
            return focused || ReferenceEquals(Keyboard.FocusedElement, tab);
        }

        public bool HandleFocusedRibbonKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window);
            source.Should().NotBeNull("the shared test window must be visible before routing focused-ribbon keyboard input");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            var handled = (bool)_tryHandleFocusedRibbonKeyboardNavigation.Invoke(_window, [args])!;
            PumpDispatcher();
            return handled;
        }

        public void OpenRibbonMenu(Key tabKeyTip, params Key[] commandKeyTips)
        {
            // Defensively close any menu left open by a prior call in this (or a preceding)
            // test so the upcoming keytip sequence always starts from a known state, rather
            // than assuming EnterKeyTipScope alone fully resets the previously active popup.
            if (ActiveMenu is { } staleMenu)
                staleMenu.IsOpen = false;

            EnterKeyTipScope("TopLevel");
            HandleKeyTip(tabKeyTip);
            foreach (var keyTip in commandKeyTips)
                HandleKeyTip(keyTip);

            // A ContextMenu's Popup can finish opening on a dispatcher pass lower-priority
            // than the single Background-priority pump HandleKeyTip already performed (e.g.
            // when the STA dispatcher is catching up on queued work from earlier tests in the
            // same process). Poll with bounded, low-priority pumps instead of assuming the
            // menu is open synchronously, so this assertion reflects real open/closed state
            // rather than a timing artifact.
            var deadline = Environment.TickCount64 + 5000;
            while (!ActiveMenuIsOpen && Environment.TickCount64 < deadline)
                PumpDispatcherIdle();

            ActiveMenuIsOpen.Should().BeTrue(
                "the ribbon keytip sequence {0},{1} should open a menu",
                tabKeyTip,
                string.Join(",", commandKeyTips));
        }

        private static void PumpDispatcherIdle()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.SystemIdle,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        public void OpenCustomZoomDialogAndCancel()
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    var zoomDialog = _window.OwnedWindows.OfType<ZoomDialog>().Single();
                    zoomDialog.Close();
                }));

            _zoomCustomMenuItemClick.Invoke(_window, [_window, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        private void ResetUiState()
        {
            _window.Activate();
            _messageService.Clear();
            _hideStartScreen.Invoke(_window, null);
            ConfigureQuickAccessToolbar(QuickAccessToolbarCatalog.DefaultCommandIds, belowRibbon: false);
            if (ActiveMenu is { } activeMenu)
                activeMenu.IsOpen = false;
            _scopeField.SetValue(_window, Enum.Parse(_scopeType, "None"));
            _activeMenuField.SetValue(_window, null);
            if (_window.FindName("KeyTipOverlay") is Canvas overlay)
            {
                overlay.Children.Clear();
                overlay.Visibility = Visibility.Collapsed;
            }
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = 2400;
                ribbonTabs.SelectedIndex = 1;
            }
            if (_window.FindName("NumberFormatBox") is ComboBox numberFormatBox)
                numberFormatBox.IsDropDownOpen = false;
            if (_window.FindName("ShapeFormatTab") is TabItem shapeFormatTab)
                shapeFormatTab.Visibility = Visibility.Collapsed;
            if (_window.FindName("PictureFormatTab") is TabItem pictureFormatTab)
                pictureFormatTab.Visibility = Visibility.Collapsed;
            SelectActiveCell();
            _window.UpdateLayout();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void Dispose()
        {
            ConfigureQuickAccessToolbar(QuickAccessToolbarCatalog.DefaultCommandIds, belowRibbon: false);
            if (ActiveMenu is { } activeMenu)
                activeMenu.IsOpen = false;
            if (_window.FindName("NumberFormatBox") is ComboBox numberFormatBox)
                numberFormatBox.IsDropDownOpen = false;
            _window.UpdateLayout();
            PumpDispatcher();
        }

        private sealed record SharedMainWindowSession(
            MainWindow Window,
            WorkbookRef WorkbookRef,
            RecordingUserMessageService MessageService);

        private sealed class RecordingUserMessageService : Free.Shared.AppServices.IUserMessageService
        {
            private readonly List<(string Kind, string Title, string Message)> _messages = [];

            public (string Title, string Message)? LastInfo
            {
                get
                {
                    for (var index = _messages.Count - 1; index >= 0; index--)
                    {
                        var message = _messages[index];
                        if (message.Kind == "Info")
                            return (message.Title, message.Message);
                    }

                    return null;
                }
            }

            public void Clear() => _messages.Clear();

            public void ShowError(string message, string title = "Error") =>
                _messages.Add(("Error", title, message));

            public void ShowWarning(string message, string title = "Warning") =>
                _messages.Add(("Warning", title, message));

            public void ShowInfo(string message, string title = "Information") =>
                _messages.Add(("Info", title, message));

            public bool AskYesNo(string message, string title = "Confirm")
            {
                _messages.Add(("Question", title, message));
                return false;
            }

            public UserMessageResult ShowMessage(
                string message,
                string title,
                UserMessageButtons buttons,
                UserMessageIcon icon)
            {
                _messages.Add(($"Message:{buttons}:{icon}", title, message));
                return UserMessageResult.Ok;
            }
        }

        private sealed class DisposableAction(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;

            public void Dispose()
            {
                var disposeAction = _dispose;
                if (disposeAction is null)
                    return;

                _dispose = null;
                disposeAction();
            }
        }

        private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl control)
        {
            foreach (var item in control.Items)
            {
                if (item is not MenuItem menuItem)
                    continue;

                yield return menuItem;

                foreach (var child in EnumerateMenuItems(menuItem))
                    yield return child;
            }
        }

    }

    private static void ConfigureWorkbookWithPivotTable(Workbook workbook)
    {
        var sheet = workbook.Sheets[0];
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheetId, 3, 2), new NumberValue(20));

        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 3, 2));
        var targetRange = new GridRange(
            new CellAddress(sheetId, 6, 5),
            new CellAddress(sheetId, 9, 6));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString()
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Region"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
    }

    private static void ConfigureWorkbookWithExpandablePivotTable(Workbook workbook)
    {
        var sheet = workbook.Sheets[0];
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheetId, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheetId, 1, 4), new TextValue("Units"));
        sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheetId, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheetId, 2, 4), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheetId, 3, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheetId, 3, 3), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheetId, 3, 4), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheetId, 4, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheetId, 4, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheetId, 4, 4), new NumberValue(4));

        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 4));
        var targetRange = new GridRange(
            new CellAddress(sheetId, 6, 5),
            new CellAddress(sheetId, 12, 8));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString()
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Region"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Quarter"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Units", ContainsNumber: true));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
    }

    private static void ConfigureWorkbookWithChart(Workbook workbook)
    {
        var sheet = workbook.Sheets[0];
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheetId, 3, 2), new NumberValue(20));

        sheet.Charts.Add(new ChartModel
        {
            Name = "Chart 1",
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 3, 2)),
            Title = "Sales"
        });
    }

    private static void ConfigureWorkbookWithDrawingObjects(Workbook workbook)
    {
        var sheet = workbook.Sheets[0];
        var sheetId = sheet.Id;

        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Rectangle 1",
            Kind = DrawingShapeKind.Rectangle,
            Anchor = new CellAddress(sheetId, 2, 2),
            IsVisible = true
        });
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Picture 1",
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheetId, 4, 2),
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            IsVisible = true
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "Text Box 1",
            Anchor = new CellAddress(sheetId, 6, 2),
            Text = "Notes",
            IsVisible = true
        });
    }

    private static void ConfigureWorkbookWithPivotAndHiddenCharts(Workbook workbook)
    {
        var sheet = workbook.Sheets[0];
        sheet.Charts.Add(new ChartModel
        {
            Name = "Pivot Chart",
            Type = ChartType.Column,
            IsPivotChart = true
        });
        sheet.Charts.Add(new ChartModel
        {
            Name = "Hidden Chart",
            Type = ChartType.Line,
            IsVisible = false
        });
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    private sealed class TempRecentFiles : IDisposable
    {
        private readonly TestTemporaryDirectory _directory;

        private TempRecentFiles(TestTemporaryDirectory directory, IReadOnlyList<string> paths)
        {
            _directory = directory;
            Paths = paths;
        }

        public IReadOnlyList<string> Paths { get; }

        public static TempRecentFiles Create(int count)
        {
            var directory = new TestTemporaryDirectory();
            try
            {
                var paths = Enumerable.Range(1, count)
                    .Select(index =>
                    {
                        var path = Path.Combine(directory.Path, $"Book{index}.xlsx");
                        File.WriteAllText(path, "");
                        return path;
                    })
                    .ToList();
                return new TempRecentFiles(directory, paths);
            }
            catch
            {
                directory.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _directory.Dispose();
        }
    }

    private static void RunSta(Action action)
    {
        StaTestRunner.Run(action);
    }
}

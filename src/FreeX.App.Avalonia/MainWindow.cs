using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed class MainWindow : Window
{
    private enum CellBorderEdge
    {
        Top,
        Right,
        Bottom,
        Left
    }

    private enum ColorPaletteTarget
    {
        Fill,
        Font
    }

    private enum DirtyWorkbookCloseChoice
    {
        Cancel,
        Save,
        Discard
    }

    private enum ShellFocusRegion
    {
        Worksheet,
        Toolbar,
        FormulaBar,
        SheetTabs,
        StatusBar
    }

    private enum FindDialogAction
    {
        FindNext,
        FindAll
    }

    private enum ReplaceDialogAction
    {
        Replace,
        ReplaceAll
    }

    private sealed record FindDialogResult(
        string FindText,
        FindDialogAction Action,
        FindOptions Options,
        bool MatchCase,
        bool MatchEntireCell);

    private sealed record ReplaceDialogResult(
        string FindText,
        string ReplaceText,
        ReplaceDialogAction Action,
        FindOptions Options,
        bool MatchCase,
        bool MatchEntireCell,
        StyleDiff? ReplacementFormat);

    private sealed record FindOptionsControls(
        ComboBox WithinBox,
        ComboBox SearchBox,
        ComboBox LookInBox,
        CheckBox MatchCaseBox,
        CheckBox MatchEntireCellBox,
        Control Panel);
    private sealed record FindDialogSmokeProbe(
        Window Dialog,
        TextBox FindBox,
        Button FindNextButton,
        Button FindAllButton,
        Button CancelButton,
        FindOptionsControls OptionsControls,
        Button ChooseFormatButton,
        Button ClearFormatButton);
    private sealed record ReplaceDialogSmokeProbe(
        Window Dialog,
        TextBox FindBox,
        TextBox ReplaceBox,
        Button ReplaceButton,
        Button ReplaceAllButton,
        Button CancelButton,
        FindOptionsControls OptionsControls,
        Button ChooseFindFormatButton,
        Button ClearFindFormatButton,
        Button ChooseReplaceFormatButton,
        Button ClearReplaceFormatButton);
    private sealed record SingleInputDialogSmokeProbe(
        Window Dialog,
        TextBox InputBox,
        Button AcceptButton,
        Button CancelButton);
    private sealed record GoToSpecialDialogSmokeProbe(
        Window Dialog,
        ComboBox KindBox,
        CheckBox NumbersBox,
        CheckBox TextBox,
        CheckBox LogicalsBox,
        CheckBox ErrorsBox,
        Button OkButton,
        Button CancelButton);
    private sealed record GoToSpecialDialogResult(GoToSpecialKind Kind, GoToSpecialOptions Options);
    private sealed record GoToSpecialChoice(GoToSpecialKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    private const double CellIndentLevelWidth = 12;
    private const string CommaNumberFormat = "#,##0.00";
    private const string CurrencyNumberFormat = "$#,##0.00";
    private const double DoubleUnderlineSecondStrokeOffset = 2;
    private const string PercentNumberFormat = "0%";
    private const double HeaderColumnWidth = 58;
    private const double HeaderRowHeight = 28;
    private const double InitialViewportHeight = 880;
    private const double InitialViewportWidth = 1440;
    private const double MinimumDisplayedColumnWidth = 54;
    private const double MinimumDisplayedRowHeight = 22;
    private const double ZoomToSelectionDefaultColumnWidth = 80;
    private const double ZoomToSelectionDefaultRowHeight = 20;
    private const int ZoomStepPercent = 10;
    private const string NativeWorkbookExtension = ".fxl";
    private const string PlatformAboutSummary = "Built with .NET 10, Avalonia, ClosedXML.";
    private const string SheetTabContextHelpText = "Selects this sheet. Press F6 repeatedly to reach sheet tabs, use arrow keys to switch sheets, or right-click/press Shift+F10 for sheet tab options.";
    private static readonly ShellFocusRegion[] ShellFocusCycle =
    [
        ShellFocusRegion.Worksheet,
        ShellFocusRegion.Toolbar,
        ShellFocusRegion.FormulaBar,
        ShellFocusRegion.SheetTabs,
        ShellFocusRegion.StatusBar
    ];
    private static readonly IBrush WindowBackground = Brush(246, 247, 249);
    private static readonly IBrush HeaderBackground = Brush(241, 243, 246);
    private static readonly IBrush HeaderForeground = Brush(73, 80, 93);
    private static readonly IBrush GridLine = Brush(218, 222, 228);
    private static readonly IBrush ToolbarBorder = Brush(218, 222, 228);
    private static readonly IBrush SelectionBorder = Brush(11, 112, 116);
    private static readonly IBrush SelectionHeaderBackground = Brush(225, 244, 242);
    private static readonly IBrush SelectionHeaderForeground = Brush(13, 86, 89);
    private static readonly IBrush DrawingObjectBoundsFill = Brush(42, 11, 112, 116);
    private static readonly IBrush DrawingObjectBoundsBorder = Brush(11, 112, 116);
    private static readonly IBrush DrawingObjectBoundsForeground = Brush(5, 67, 69);

    private readonly WorkbookSessionFactory _sessionFactory = new();
    private readonly WorkbookOpenService _openService = new();
    private readonly WorkbookSaveService _saveService = new();
    private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();
    private readonly ContentControl _sheetGridHost = new();
    private readonly ContentControl _sheetTabsHost = new();
    private readonly ScrollViewer _sheetScrollViewer = new();
    private readonly ScrollBar _verticalWorksheetScrollBar = new();
    private readonly ScrollBar _horizontalWorksheetScrollBar = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _detailText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _selectionStatsText = new();
    private readonly TextBlock _zoomText = new();
    private readonly TextBlock _cellAddressText = new();
    private readonly TextBox _formulaBox = new();
    private readonly Button _openButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _saveAsButton = new();
    private readonly Button _newSheetButton = new();
    private readonly Button _undoButton = new();
    private readonly Button _redoButton = new();
    private readonly Button _cutButton = new();
    private readonly Button _copyButton = new();
    private readonly Button _pasteButton = new();
    private readonly DropDownButton _pasteSpecialButton = new();
    private readonly Button _formatPainterButton = new();
    private readonly DropDownButton _autoSumButton = new();
    private readonly MenuItem _autoSumSumFlyoutItem = new();
    private readonly MenuItem _autoSumAverageFlyoutItem = new();
    private readonly MenuItem _autoSumCountNumbersFlyoutItem = new();
    private readonly MenuItem _autoSumCountAllFlyoutItem = new();
    private readonly MenuItem _autoSumMaxFlyoutItem = new();
    private readonly MenuItem _autoSumMinFlyoutItem = new();
    private readonly DropDownButton _fillCellsButton = new();
    private readonly MenuItem _fillDownFlyoutItem = new();
    private readonly MenuItem _fillRightFlyoutItem = new();
    private readonly MenuItem _fillUpFlyoutItem = new();
    private readonly MenuItem _fillLeftFlyoutItem = new();
    private readonly DropDownButton _clearButton = new();
    private readonly MenuItem _clearAllFlyoutItem = new();
    private readonly MenuItem _clearFormatsFlyoutItem = new();
    private readonly MenuItem _clearContentsFlyoutItem = new();
    private readonly MenuItem _clearCommentsFlyoutItem = new();
    private readonly MenuItem _clearHyperlinksFlyoutItem = new();
    private readonly ToggleButton _boldButton = new();
    private readonly ToggleButton _italicButton = new();
    private readonly ToggleButton _underlineButton = new();
    private readonly ToggleButton _doubleUnderlineButton = new();
    private readonly ToggleButton _strikethroughButton = new();
    private readonly Button _increaseFontSizeButton = new();
    private readonly Button _decreaseFontSizeButton = new();
    private readonly DropDownButton _fillColorButton = new();
    private readonly DropDownButton _fontColorButton = new();
    private readonly DropDownButton _bordersButton = new();
    private readonly DropDownButton _cellStylesButton = new();
    private readonly DropDownButton _orientationButton = new();
    private readonly Button _currencyFormatButton = new();
    private readonly Button _percentFormatButton = new();
    private readonly Button _commaStyleButton = new();
    private readonly Button _increaseDecimalButton = new();
    private readonly Button _decreaseDecimalButton = new();
    private readonly ToggleButton _alignLeftButton = new();
    private readonly ToggleButton _alignCenterButton = new();
    private readonly ToggleButton _alignRightButton = new();
    private readonly ToggleButton _alignTopButton = new();
    private readonly ToggleButton _alignMiddleButton = new();
    private readonly ToggleButton _alignBottomButton = new();
    private readonly ToggleButton _wrapTextButton = new();
    private readonly Button _mergeAndCenterButton = new();
    private readonly Button _decreaseIndentButton = new();
    private readonly Button _increaseIndentButton = new();
    private readonly NativeMenuItem _newWorkbookMenuItem = new();
    private readonly NativeMenuItem _openMenuItem = new();
    private readonly NativeMenuItem _openRecentMenuItem = new();
    private readonly NativeMenuItem _saveMenuItem = new();
    private readonly NativeMenuItem _saveAsMenuItem = new();
    private readonly NativeMenuItem _closeWorkbookMenuItem = new();
    private readonly NativeMenuItem _newSheetMenuItem = new();
    private readonly NativeMenuItem _renameSheetMenuItem = new();
    private readonly NativeMenuItem _duplicateSheetMenuItem = new();
    private readonly NativeMenuItem _moveSheetLeftMenuItem = new();
    private readonly NativeMenuItem _moveSheetRightMenuItem = new();
    private readonly NativeMenuItem _tabColorMenuItem = new();
    private readonly NativeMenuItem _selectAllSheetsMenuItem = new();
    private readonly NativeMenuItem _ungroupSheetsMenuItem = new();
    private readonly NativeMenuItem _hideSheetMenuItem = new();
    private readonly NativeMenuItem _unhideSheetMenuItem = new();
    private readonly NativeMenuItem _deleteSheetMenuItem = new();
    private readonly NativeMenuItem _undoMenuItem = new();
    private readonly NativeMenuItem _redoMenuItem = new();
    private readonly NativeMenuItem _cutMenuItem = new();
    private readonly NativeMenuItem _copyMenuItem = new();
    private readonly NativeMenuItem _pasteMenuItem = new();
    private readonly NativeMenuItem _pasteSpecialMenuItem = new();
    private readonly NativeMenuItem _formatPainterMenuItem = new();
    private readonly NativeMenuItem _selectAllMenuItem = new();
    private readonly NativeMenuItem _findMenuItem = new();
    private readonly NativeMenuItem _findNextMenuItem = new();
    private readonly NativeMenuItem _replaceMenuItem = new();
    private readonly NativeMenuItem _goToMenuItem = new();
    private readonly NativeMenuItem _goToSpecialMenuItem = new();
    private readonly NativeMenuItem _autoSumMenuItem = new();
    private readonly NativeMenuItem _autoSumSumMenuItem = new();
    private readonly NativeMenuItem _autoSumAverageMenuItem = new();
    private readonly NativeMenuItem _autoSumCountNumbersMenuItem = new();
    private readonly NativeMenuItem _autoSumCountAllMenuItem = new();
    private readonly NativeMenuItem _autoSumMaxMenuItem = new();
    private readonly NativeMenuItem _autoSumMinMenuItem = new();
    private readonly NativeMenuItem _fillCellsMenuItem = new();
    private readonly NativeMenuItem _fillDownMenuItem = new();
    private readonly NativeMenuItem _fillRightMenuItem = new();
    private readonly NativeMenuItem _fillUpMenuItem = new();
    private readonly NativeMenuItem _fillLeftMenuItem = new();
    private readonly NativeMenuItem _clearMenuItem = new();
    private readonly NativeMenuItem _clearAllMenuItem = new();
    private readonly NativeMenuItem _clearFormatsMenuItem = new();
    private readonly NativeMenuItem _clearContentsMenuItem = new();
    private readonly NativeMenuItem _clearCommentsMenuItem = new();
    private readonly NativeMenuItem _clearHyperlinksMenuItem = new();
    private readonly NativeMenuItem _boldMenuItem = new();
    private readonly NativeMenuItem _italicMenuItem = new();
    private readonly NativeMenuItem _underlineMenuItem = new();
    private readonly NativeMenuItem _doubleUnderlineMenuItem = new();
    private readonly NativeMenuItem _strikethroughMenuItem = new();
    private readonly NativeMenuItem _increaseFontSizeMenuItem = new();
    private readonly NativeMenuItem _decreaseFontSizeMenuItem = new();
    private readonly NativeMenuItem _fillColorMenuItem = new();
    private readonly NativeMenuItem _clearFillMenuItem = new();
    private readonly NativeMenuItem _fontColorMenuItem = new();
    private readonly NativeMenuItem _bordersMenuItem = new();
    private readonly NativeMenuItem _cellStylesMenuItem = new();
    private readonly NativeMenuItem _horizontalTextMenuItem = new();
    private readonly NativeMenuItem _angleCounterclockwiseMenuItem = new();
    private readonly NativeMenuItem _angleClockwiseMenuItem = new();
    private readonly NativeMenuItem _verticalTextMenuItem = new();
    private readonly NativeMenuItem _rotateTextUpMenuItem = new();
    private readonly NativeMenuItem _rotateTextDownMenuItem = new();
    private readonly NativeMenuItem _currencyFormatMenuItem = new();
    private readonly NativeMenuItem _percentFormatMenuItem = new();
    private readonly NativeMenuItem _commaStyleMenuItem = new();
    private readonly NativeMenuItem _increaseDecimalMenuItem = new();
    private readonly NativeMenuItem _decreaseDecimalMenuItem = new();
    private readonly NativeMenuItem _alignLeftMenuItem = new();
    private readonly NativeMenuItem _alignCenterMenuItem = new();
    private readonly NativeMenuItem _alignRightMenuItem = new();
    private readonly NativeMenuItem _alignTopMenuItem = new();
    private readonly NativeMenuItem _alignMiddleMenuItem = new();
    private readonly NativeMenuItem _alignBottomMenuItem = new();
    private readonly NativeMenuItem _wrapTextMenuItem = new();
    private readonly NativeMenuItem _mergeAndCenterMenuItem = new();
    private readonly NativeMenuItem _unmergeCellsMenuItem = new();
    private readonly NativeMenuItem _decreaseIndentMenuItem = new();
    private readonly NativeMenuItem _increaseIndentMenuItem = new();
    private readonly NativeMenuItem _showGridlinesMenuItem = new();
    private readonly NativeMenuItem _showHeadingsMenuItem = new();
    private readonly NativeMenuItem _zoomInMenuItem = new();
    private readonly NativeMenuItem _zoomOutMenuItem = new();
    private readonly NativeMenuItem _zoom100MenuItem = new();
    private readonly NativeMenuItem _zoomToSelectionMenuItem = new();
    private readonly NativeMenuItem _freezePanesMenuItem = new();
    private readonly NativeMenuItem _freezeTopRowMenuItem = new();
    private readonly NativeMenuItem _freezeFirstColumnMenuItem = new();
    private readonly NativeMenuItem _unfreezePanesMenuItem = new();
    private readonly NativeMenuItem _showFormulasMenuItem = new();
    private readonly NativeMenuItem _helpOnlineMenuItem = new();
    private readonly NativeMenuItem _sendFeedbackMenuItem = new();
    private readonly NativeMenuItem _checkForUpdatesMenuItem = new();
    private readonly NativeMenuItem _aboutMenuItem = new();
    private readonly NativeMenuItem _legalNoticesMenuItem = new();
    private readonly NativeMenuItem _quitMenuItem = new();
    private NativeMenu? _nativeMenu;
    private WorkbookSession _session;
    private MacOsLaunchSmokeDialogSnapshot _launchSmokeDialogEvidence = MacOsLaunchSmokeDialogSnapshot.Empty;
    private string? _formulaBoxEditOriginalText;
    private bool _isOpening;
    private bool _isSaving;
    private bool _allowCloseWithoutDirtyPrompt;
    private bool _isDirtyCloseDialogOpen;
    private bool _isUpdatingWorksheetScrollBars;
    private SelectionPaneObjectKind? _selectedDrawingObjectKind;
    private Guid? _selectedDrawingObjectId;

    public MainWindow(IReadOnlyList<string> startupArguments)
    {
        var source = new StartupWorkbookLoader().Load(startupArguments);
        _session = _sessionFactory.Create(source, InitialViewportHeight, InitialViewportWidth, includeObjects: true);

        Title = $"FreeX - {_session.DisplayName}";
        Width = 1120;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;
        Background = WindowBackground;
        Content = BuildContent();
        ConfigureNativeMenu();
        RecordStartupRecentWorkbook(source);
        ConfigureWorkbookDropTarget();
        KeyDown += MainWindow_KeyDown;
        TextInput += MainWindow_TextInput;
        Closing += MainWindow_Closing;
        RefreshShell(_session.StartupStatus);
    }

    private Control BuildContent()
    {
        var root = new DockPanel();

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var sheetTabs = BuildSheetTabsChrome();
        DockPanel.SetDock(sheetTabs, Dock.Bottom);
        root.Children.Add(sheetTabs);

        root.Children.Add(BuildWorksheetViewportChrome());

        return root;
    }

    private Control BuildWorksheetViewportChrome()
    {
        _sheetGridHost.Focusable = true;
        AutomationProperties.SetName(_sheetGridHost, "Worksheet");
        AutomationProperties.SetHelpText(_sheetGridHost, "Shows the active workbook sheet.");

        _sheetScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _sheetScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _sheetScrollViewer.Content = _sheetGridHost;
        _sheetScrollViewer.SizeChanged += SheetScrollViewer_SizeChanged;
        _sheetScrollViewer.PointerWheelChanged += SheetScrollViewer_PointerWheelChanged;

        _verticalWorksheetScrollBar.Orientation = Orientation.Vertical;
        _verticalWorksheetScrollBar.Width = 16;
        _verticalWorksheetScrollBar.AllowAutoHide = false;
        _verticalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;

        _horizontalWorksheetScrollBar.Orientation = Orientation.Horizontal;
        _horizontalWorksheetScrollBar.Height = 16;
        _horizontalWorksheetScrollBar.AllowAutoHide = false;
        _horizontalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;

        var chrome = new AvaloniaGrid
        {
            Background = Brushes.White,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        AddGridChild(chrome, _sheetScrollViewer, 0, 0);
        AddGridChild(chrome, _verticalWorksheetScrollBar, 0, 1);
        AddGridChild(chrome, _horizontalWorksheetScrollBar, 1, 0);
        AddGridChild(
            chrome,
            new Border
            {
                Width = 16,
                Height = 16,
                Background = HeaderBackground,
                BorderBrush = ToolbarBorder,
                BorderThickness = new Thickness(1, 1, 0, 0),
            },
            1,
            1);

        return chrome;
    }

    private Control BuildSheetTabsChrome()
    {
        _sheetTabsHost.Content = BuildSheetTabs();
        _newSheetButton.Content = "+";
        _newSheetButton.Width = 32;
        _newSheetButton.Height = 28;
        _newSheetButton.MinWidth = 32;
        _newSheetButton.Padding = new Thickness(0);
        _newSheetButton.HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center;
        _newSheetButton.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
        _newSheetButton.Click += (_, _) => AddNewSheet();
        AutomationProperties.SetName(_newSheetButton, "New Sheet");
        AutomationProperties.SetHelpText(_newSheetButton, "Adds a worksheet to the current workbook.");

        var chrome = new DockPanel
        {
            LastChildFill = true,
        };
        DockPanel.SetDock(_newSheetButton, Dock.Right);
        chrome.Children.Add(_newSheetButton);
        chrome.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _sheetTabsHost,
        });

        return new Border
        {
            Background = Brush(249, 250, 252),
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 6),
            Child = chrome,
        };
    }

    private void ConfigureNativeMenu()
    {
        _newWorkbookMenuItem.Header = "New Workbook";
        _newWorkbookMenuItem.Gesture = new KeyGesture(Key.N, KeyModifiers.Meta);
        _newWorkbookMenuItem.Click += (_, _) => CreateNewWorkbook();

        _openMenuItem.Header = "Open...";
        _openMenuItem.Gesture = new KeyGesture(Key.O, KeyModifiers.Meta);
        _openMenuItem.Click += async (_, _) => await OpenWorkbookAsync();

        _openRecentMenuItem.Header = "Open Recent";
        _openRecentMenuItem.Menu = CreateNativeOpenRecentMenu(isIdle: true);

        _saveMenuItem.Header = "Save";
        _saveMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta);
        _saveMenuItem.Click += async (_, _) => await SaveCurrentWorkbookAsync();

        _saveAsMenuItem.Header = "Save As...";
        _saveAsMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift);
        _saveAsMenuItem.Click += async (_, _) => await SaveWorkbookAsAsync();

        _closeWorkbookMenuItem.Header = "Close Workbook";
        _closeWorkbookMenuItem.Gesture = new KeyGesture(Key.W, KeyModifiers.Meta);
        _closeWorkbookMenuItem.Click += async (_, _) => await CloseWorkbookAsync();

        _newSheetMenuItem.Header = "New Sheet";
        _newSheetMenuItem.Gesture = new KeyGesture(Key.F11, KeyModifiers.Shift);
        _newSheetMenuItem.Click += (_, _) => AddNewSheet();

        _renameSheetMenuItem.Header = "Rename Sheet...";
        _renameSheetMenuItem.Click += async (_, _) => await RenameActiveSheetAsync();

        _duplicateSheetMenuItem.Header = "Duplicate Sheet";
        _duplicateSheetMenuItem.Click += (_, _) => DuplicateActiveSheet();

        _moveSheetLeftMenuItem.Header = "Move Sheet Left";
        _moveSheetLeftMenuItem.Click += (_, _) => MoveActiveSheetLeft();

        _moveSheetRightMenuItem.Header = "Move Sheet Right";
        _moveSheetRightMenuItem.Click += (_, _) => MoveActiveSheetRight();

        _tabColorMenuItem.Header = "Tab Color";
        _tabColorMenuItem.Menu = CreateNativeSheetTabColorMenu();

        _selectAllSheetsMenuItem.Header = "Select All Sheets";
        _selectAllSheetsMenuItem.Click += (_, _) => SelectAllVisibleSheets();

        _ungroupSheetsMenuItem.Header = "Ungroup Sheets";
        _ungroupSheetsMenuItem.Click += (_, _) => UngroupSheets();

        _hideSheetMenuItem.Header = "Hide Sheet";
        _hideSheetMenuItem.Click += (_, _) => HideActiveSheet();

        _unhideSheetMenuItem.Header = "Unhide Sheet...";
        _unhideSheetMenuItem.Click += async (_, _) => await UnhideSheetAsync();

        _deleteSheetMenuItem.Header = "Delete Sheet";
        _deleteSheetMenuItem.Click += (_, _) => DeleteActiveSheet();

        _undoMenuItem.Header = "Undo";
        _undoMenuItem.Gesture = new KeyGesture(Key.Z, KeyModifiers.Meta);
        _undoMenuItem.Click += (_, _) => UndoLastEdit();

        _redoMenuItem.Header = "Redo";
        _redoMenuItem.Gesture = new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift);
        _redoMenuItem.Click += (_, _) => RedoLastEdit();

        _cutMenuItem.Header = "Cut";
        _cutMenuItem.Gesture = new KeyGesture(Key.X, KeyModifiers.Meta);
        _cutMenuItem.Click += async (_, _) => await CutSelectedRangeToClipboardAsync();

        _copyMenuItem.Header = "Copy";
        _copyMenuItem.Gesture = new KeyGesture(Key.C, KeyModifiers.Meta);
        _copyMenuItem.Click += async (_, _) => await CopySelectedRangeToClipboardAsync();

        _pasteMenuItem.Header = "Paste";
        _pasteMenuItem.Gesture = new KeyGesture(Key.V, KeyModifiers.Meta);
        _pasteMenuItem.Click += async (_, _) => await PasteClipboardTextAsync();

        _pasteSpecialMenuItem.Header = "Paste Special";
        _pasteSpecialMenuItem.Gesture = new KeyGesture(Key.V, KeyModifiers.Meta | KeyModifiers.Alt);
        _pasteSpecialMenuItem.Menu = CreateNativePasteSpecialMenu();

        _formatPainterMenuItem.Header = "Format Painter";
        _formatPainterMenuItem.Click += (_, _) => CaptureFormatPainterSource(persistent: false);

        _selectAllMenuItem.Header = "Select All";
        _selectAllMenuItem.Gesture = new KeyGesture(Key.A, KeyModifiers.Meta);
        _selectAllMenuItem.Click += (_, _) => SelectCurrentRegionOrAll();

        _findMenuItem.Header = "Find...";
        _findMenuItem.Gesture = new KeyGesture(Key.F, KeyModifiers.Meta);
        _findMenuItem.Click += async (_, _) => await ShowFindDialogAsync();

        _findNextMenuItem.Header = "Find Next";
        _findNextMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Meta);
        _findNextMenuItem.Click += (_, _) => FindNext();

        _replaceMenuItem.Header = "Replace...";
        _replaceMenuItem.Gesture = new KeyGesture(Key.H, KeyModifiers.Control);
        _replaceMenuItem.Click += async (_, _) => await ShowReplaceDialogAsync();

        _goToMenuItem.Header = "Go To...";
        _goToMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Control);
        _goToMenuItem.Click += async (_, _) => await ShowGoToDialogAsync();

        _goToSpecialMenuItem.Header = "Go To Special...";
        _goToSpecialMenuItem.Click += async (_, _) => await ShowGoToSpecialDialogAsync();

        _autoSumMenuItem.Header = "AutoSum";
        _autoSumMenuItem.Menu = CreateNativeAutoSumMenu();

        _autoSumSumMenuItem.Header = "Sum";
        _autoSumSumMenuItem.Gesture = new KeyGesture(Key.OemPlus, KeyModifiers.Alt);
        _autoSumSumMenuItem.Click += (_, _) => InsertAutoSumFormula("SUM");

        _autoSumAverageMenuItem.Header = "Average";
        _autoSumAverageMenuItem.Click += (_, _) => InsertAutoSumFormula("AVERAGE");

        _autoSumCountNumbersMenuItem.Header = "Count Numbers";
        _autoSumCountNumbersMenuItem.Click += (_, _) => InsertAutoSumFormula("COUNT");

        _autoSumCountAllMenuItem.Header = "Count All";
        _autoSumCountAllMenuItem.Click += (_, _) => InsertAutoSumFormula("COUNTA");

        _autoSumMaxMenuItem.Header = "Max";
        _autoSumMaxMenuItem.Click += (_, _) => InsertAutoSumFormula("MAX");

        _autoSumMinMenuItem.Header = "Min";
        _autoSumMinMenuItem.Click += (_, _) => InsertAutoSumFormula("MIN");

        _fillCellsMenuItem.Header = "Fill";
        _fillCellsMenuItem.Menu = CreateNativeFillCellsMenu();

        _fillDownMenuItem.Header = "Down";
        _fillDownMenuItem.Gesture = new KeyGesture(Key.D, KeyModifiers.Control);
        _fillDownMenuItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Down);

        _fillRightMenuItem.Header = "Right";
        _fillRightMenuItem.Gesture = new KeyGesture(Key.R, KeyModifiers.Control);
        _fillRightMenuItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Right);

        _fillUpMenuItem.Header = "Up";
        _fillUpMenuItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Up);

        _fillLeftMenuItem.Header = "Left";
        _fillLeftMenuItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Left);

        _clearMenuItem.Header = "Clear";
        _clearMenuItem.Menu = CreateNativeClearMenu();

        _clearAllMenuItem.Header = "Clear All";
        _clearAllMenuItem.Click += (_, _) => ClearSelectedRangeAll();

        _clearFormatsMenuItem.Header = "Clear Formats";
        _clearFormatsMenuItem.Click += (_, _) => ClearSelectedRangeFormats();

        _clearContentsMenuItem.Header = "Clear Contents";
        _clearContentsMenuItem.Gesture = new KeyGesture(Key.Delete);
        _clearContentsMenuItem.Click += (_, _) => ClearSelectedRangeContents();

        _clearCommentsMenuItem.Header = "Clear Comments and Notes";
        _clearCommentsMenuItem.Click += (_, _) => ClearSelectedRangeComments();

        _clearHyperlinksMenuItem.Header = "Clear Hyperlinks";
        _clearHyperlinksMenuItem.Click += (_, _) => ClearSelectedRangeHyperlinks();

        _boldMenuItem.Header = "Bold";
        _boldMenuItem.Gesture = new KeyGesture(Key.B, KeyModifiers.Meta);
        _boldMenuItem.Click += (_, _) => ToggleSelectedRangeBold();

        _italicMenuItem.Header = "Italic";
        _italicMenuItem.Gesture = new KeyGesture(Key.I, KeyModifiers.Meta);
        _italicMenuItem.Click += (_, _) => ToggleSelectedRangeItalic();

        _underlineMenuItem.Header = "Underline";
        _underlineMenuItem.Gesture = new KeyGesture(Key.U, KeyModifiers.Meta);
        _underlineMenuItem.Click += (_, _) => ToggleSelectedRangeUnderline();

        _doubleUnderlineMenuItem.Header = "Double Underline";
        _doubleUnderlineMenuItem.Click += (_, _) => ToggleSelectedRangeDoubleUnderline();

        _strikethroughMenuItem.Header = "Strikethrough";
        _strikethroughMenuItem.Gesture = new KeyGesture(Key.D5, KeyModifiers.Control);
        _strikethroughMenuItem.Click += (_, _) => ToggleSelectedRangeStrikethrough();

        _increaseFontSizeMenuItem.Header = "Increase Font Size";
        _increaseFontSizeMenuItem.Click += (_, _) => IncreaseSelectedRangeFontSize();

        _decreaseFontSizeMenuItem.Header = "Decrease Font Size";
        _decreaseFontSizeMenuItem.Click += (_, _) => DecreaseSelectedRangeFontSize();

        _fillColorMenuItem.Header = "Fill Color";
        _fillColorMenuItem.Menu = CreateNativeColorPaletteMenu(ColorPaletteTarget.Fill, includeClearFill: true);

        _clearFillMenuItem.Header = "No Fill";
        _clearFillMenuItem.Click += (_, _) => ClearSelectedRangeFill();

        _fontColorMenuItem.Header = "Font Color";
        _fontColorMenuItem.Menu = CreateNativeColorPaletteMenu(ColorPaletteTarget.Font, includeClearFill: false);

        _bordersMenuItem.Header = "Borders";
        _bordersMenuItem.Menu = CreateNativeBorderPresetMenu();

        _cellStylesMenuItem.Header = "Cell Styles";
        _cellStylesMenuItem.Menu = CreateNativeCellStylesMenu();

        _horizontalTextMenuItem.Header = "Horizontal";
        _horizontalTextMenuItem.Click += (_, _) =>
            ApplySelectedRangeTextRotation(0, "Set horizontal text for", "Horizontal Text failed.");

        _angleCounterclockwiseMenuItem.Header = "Angle Counterclockwise";
        _angleCounterclockwiseMenuItem.Click += (_, _) =>
            ApplySelectedRangeTextRotation(45, "Angled text counterclockwise for", "Angle Counterclockwise failed.");

        _angleClockwiseMenuItem.Header = "Angle Clockwise";
        _angleClockwiseMenuItem.Click += (_, _) =>
            ApplySelectedRangeTextRotation(-45, "Angled text clockwise for", "Angle Clockwise failed.");

        _verticalTextMenuItem.Header = "Vertical Text";
        _verticalTextMenuItem.Click += (_, _) =>
            ApplySelectedRangeTextRotation(255, "Set vertical text for", "Vertical Text failed.");

        _rotateTextUpMenuItem.Header = "Rotate Text Up";
        _rotateTextUpMenuItem.Click += (_, _) =>
            ApplySelectedRangeTextRotation(90, "Rotated text up for", "Rotate Text Up failed.");

        _rotateTextDownMenuItem.Header = "Rotate Text Down";
        _rotateTextDownMenuItem.Click += (_, _) =>
            ApplySelectedRangeTextRotation(-90, "Rotated text down for", "Rotate Text Down failed.");

        _currencyFormatMenuItem.Header = "Accounting Number Format";
        _currencyFormatMenuItem.Click += (_, _) => ApplySelectedRangeCurrencyFormat();

        _percentFormatMenuItem.Header = "Percent Style";
        _percentFormatMenuItem.Click += (_, _) => ApplySelectedRangePercentFormat();

        _commaStyleMenuItem.Header = "Comma Style";
        _commaStyleMenuItem.Click += (_, _) => ApplySelectedRangeCommaStyle();

        _increaseDecimalMenuItem.Header = "Increase Decimal Places";
        _increaseDecimalMenuItem.Click += (_, _) => IncreaseSelectedRangeDecimalPlaces();

        _decreaseDecimalMenuItem.Header = "Decrease Decimal Places";
        _decreaseDecimalMenuItem.Click += (_, _) => DecreaseSelectedRangeDecimalPlaces();

        _alignTopMenuItem.Header = "Align Top";
        _alignTopMenuItem.Click += (_, _) => ApplySelectedRangeVerticalAlignment(CellVAlign.Top);

        _alignMiddleMenuItem.Header = "Align Middle";
        _alignMiddleMenuItem.Click += (_, _) => ApplySelectedRangeVerticalAlignment(CellVAlign.Center);

        _alignBottomMenuItem.Header = "Align Bottom";
        _alignBottomMenuItem.Click += (_, _) => ApplySelectedRangeVerticalAlignment(CellVAlign.Bottom);

        _wrapTextMenuItem.Header = "Wrap Text";
        _wrapTextMenuItem.Click += (_, _) => ToggleSelectedRangeWrapText();

        _mergeAndCenterMenuItem.Header = "Merge & Center";
        _mergeAndCenterMenuItem.Click += (_, _) => MergeAndCenterSelectedRange();

        _unmergeCellsMenuItem.Header = "Unmerge Cells";
        _unmergeCellsMenuItem.Click += (_, _) => UnmergeSelectedRange();

        _decreaseIndentMenuItem.Header = "Decrease Indent";
        _decreaseIndentMenuItem.Click += (_, _) => DecreaseSelectedRangeIndent();

        _increaseIndentMenuItem.Header = "Increase Indent";
        _increaseIndentMenuItem.Click += (_, _) => IncreaseSelectedRangeIndent();

        _alignLeftMenuItem.Header = "Align Left";
        _alignLeftMenuItem.Click += (_, _) => ApplySelectedRangeHorizontalAlignment(CellHAlign.Left);

        _alignCenterMenuItem.Header = "Align Center";
        _alignCenterMenuItem.Click += (_, _) => ApplySelectedRangeHorizontalAlignment(CellHAlign.Center);

        _alignRightMenuItem.Header = "Align Right";
        _alignRightMenuItem.Click += (_, _) => ApplySelectedRangeHorizontalAlignment(CellHAlign.Right);

        _showGridlinesMenuItem.Header = "Gridlines";
        _showGridlinesMenuItem.ToggleType = MenuItemToggleType.CheckBox;
        _showGridlinesMenuItem.Click += (_, _) => ToggleShowGridlines();

        _showHeadingsMenuItem.Header = "Headings";
        _showHeadingsMenuItem.ToggleType = MenuItemToggleType.CheckBox;
        _showHeadingsMenuItem.Click += (_, _) => ToggleShowHeadings();

        _zoomInMenuItem.Header = "Zoom In";
        _zoomInMenuItem.Gesture = new KeyGesture(Key.OemPlus, KeyModifiers.Meta);
        _zoomInMenuItem.Click += (_, _) => ZoomIn();

        _zoomOutMenuItem.Header = "Zoom Out";
        _zoomOutMenuItem.Gesture = new KeyGesture(Key.OemMinus, KeyModifiers.Meta);
        _zoomOutMenuItem.Click += (_, _) => ZoomOut();

        _zoom100MenuItem.Header = "100%";
        _zoom100MenuItem.Gesture = new KeyGesture(Key.D0, KeyModifiers.Meta);
        _zoom100MenuItem.Click += (_, _) => ZoomTo100Percent();

        _zoomToSelectionMenuItem.Header = "Zoom to Selection";
        _zoomToSelectionMenuItem.Click += (_, _) => ZoomToSelection();

        _freezePanesMenuItem.Header = "Freeze Panes";
        _freezePanesMenuItem.Click += (_, _) => FreezePanesAtActiveCell();

        _freezeTopRowMenuItem.Header = "Freeze Top Row";
        _freezeTopRowMenuItem.Click += (_, _) => FreezeTopRow();

        _freezeFirstColumnMenuItem.Header = "Freeze First Column";
        _freezeFirstColumnMenuItem.Click += (_, _) => FreezeFirstColumn();

        _unfreezePanesMenuItem.Header = "Unfreeze Panes";
        _unfreezePanesMenuItem.Click += (_, _) => UnfreezePanes();

        _showFormulasMenuItem.Header = "Show Formulas";
        _showFormulasMenuItem.Gesture = new KeyGesture(Key.Oem3, KeyModifiers.Control);
        _showFormulasMenuItem.ToggleType = MenuItemToggleType.CheckBox;
        _showFormulasMenuItem.Click += (_, _) => ToggleShowFormulas();

        _helpOnlineMenuItem.Header = "Help Online";
        _helpOnlineMenuItem.Gesture = new KeyGesture(Key.F1, default);
        _helpOnlineMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, "Help Online");

        _sendFeedbackMenuItem.Header = "Send Feedback";
        _sendFeedbackMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, "Send Feedback");

        _checkForUpdatesMenuItem.Header = "Check for Updates";
        _checkForUpdatesMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, "Check for Updates");

        _aboutMenuItem.Header = "About FreeX";
        _aboutMenuItem.Click += async (_, _) => await ShowAboutDialogAsync();

        _legalNoticesMenuItem.Header = "Legal Notices";
        _legalNoticesMenuItem.Click += async (_, _) => await ShowLegalNoticesDialogAsync();

        _quitMenuItem.Header = "Quit FreeX";
        _quitMenuItem.Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta);
        _quitMenuItem.Click += async (_, _) => await TryQuitApplicationAsync();

        var fileMenu = new NativeMenu();
        fileMenu.Items.Add(_newWorkbookMenuItem);
        fileMenu.Items.Add(_openMenuItem);
        fileMenu.Items.Add(_openRecentMenuItem);
        fileMenu.Items.Add(_saveMenuItem);
        fileMenu.Items.Add(_saveAsMenuItem);
        fileMenu.Items.Add(new NativeMenuItemSeparator());
        fileMenu.Items.Add(_closeWorkbookMenuItem);
        fileMenu.Items.Add(new NativeMenuItemSeparator());
        fileMenu.Items.Add(_quitMenuItem);

        var editMenu = new NativeMenu();
        editMenu.Items.Add(_undoMenuItem);
        editMenu.Items.Add(_redoMenuItem);
        editMenu.Items.Add(new NativeMenuItemSeparator());
        editMenu.Items.Add(_cutMenuItem);
        editMenu.Items.Add(_copyMenuItem);
        editMenu.Items.Add(_pasteMenuItem);
        editMenu.Items.Add(_pasteSpecialMenuItem);
        editMenu.Items.Add(_formatPainterMenuItem);
        editMenu.Items.Add(new NativeMenuItemSeparator());
        editMenu.Items.Add(_selectAllMenuItem);
        editMenu.Items.Add(new NativeMenuItemSeparator());
        editMenu.Items.Add(_findMenuItem);
        editMenu.Items.Add(_findNextMenuItem);
        editMenu.Items.Add(_replaceMenuItem);
        editMenu.Items.Add(_goToMenuItem);
        editMenu.Items.Add(_goToSpecialMenuItem);
        editMenu.Items.Add(new NativeMenuItemSeparator());
        editMenu.Items.Add(_autoSumMenuItem);
        editMenu.Items.Add(_fillCellsMenuItem);
        editMenu.Items.Add(_clearMenuItem);

        var formatMenu = new NativeMenu();
        formatMenu.Items.Add(_boldMenuItem);
        formatMenu.Items.Add(_italicMenuItem);
        formatMenu.Items.Add(_underlineMenuItem);
        formatMenu.Items.Add(_doubleUnderlineMenuItem);
        formatMenu.Items.Add(_strikethroughMenuItem);
        formatMenu.Items.Add(_increaseFontSizeMenuItem);
        formatMenu.Items.Add(_decreaseFontSizeMenuItem);
        formatMenu.Items.Add(_fillColorMenuItem);
        formatMenu.Items.Add(_clearFillMenuItem);
        formatMenu.Items.Add(_fontColorMenuItem);
        formatMenu.Items.Add(_bordersMenuItem);
        formatMenu.Items.Add(_cellStylesMenuItem);
        formatMenu.Items.Add(new NativeMenuItemSeparator());
        formatMenu.Items.Add(_horizontalTextMenuItem);
        formatMenu.Items.Add(_angleCounterclockwiseMenuItem);
        formatMenu.Items.Add(_angleClockwiseMenuItem);
        formatMenu.Items.Add(_verticalTextMenuItem);
        formatMenu.Items.Add(_rotateTextUpMenuItem);
        formatMenu.Items.Add(_rotateTextDownMenuItem);
        formatMenu.Items.Add(new NativeMenuItemSeparator());
        formatMenu.Items.Add(_currencyFormatMenuItem);
        formatMenu.Items.Add(_percentFormatMenuItem);
        formatMenu.Items.Add(_commaStyleMenuItem);
        formatMenu.Items.Add(_increaseDecimalMenuItem);
        formatMenu.Items.Add(_decreaseDecimalMenuItem);
        formatMenu.Items.Add(new NativeMenuItemSeparator());
        formatMenu.Items.Add(_alignTopMenuItem);
        formatMenu.Items.Add(_alignMiddleMenuItem);
        formatMenu.Items.Add(_alignBottomMenuItem);
        formatMenu.Items.Add(_wrapTextMenuItem);
        formatMenu.Items.Add(_mergeAndCenterMenuItem);
        formatMenu.Items.Add(_unmergeCellsMenuItem);
        formatMenu.Items.Add(_decreaseIndentMenuItem);
        formatMenu.Items.Add(_increaseIndentMenuItem);
        formatMenu.Items.Add(_alignLeftMenuItem);
        formatMenu.Items.Add(_alignCenterMenuItem);
        formatMenu.Items.Add(_alignRightMenuItem);

        var viewMenu = new NativeMenu();
        viewMenu.Items.Add(_showGridlinesMenuItem);
        viewMenu.Items.Add(_showHeadingsMenuItem);
        viewMenu.Items.Add(new NativeMenuItemSeparator());
        viewMenu.Items.Add(_zoomInMenuItem);
        viewMenu.Items.Add(_zoomOutMenuItem);
        viewMenu.Items.Add(_zoom100MenuItem);
        viewMenu.Items.Add(_zoomToSelectionMenuItem);
        viewMenu.Items.Add(new NativeMenuItemSeparator());
        viewMenu.Items.Add(_freezePanesMenuItem);
        viewMenu.Items.Add(_freezeTopRowMenuItem);
        viewMenu.Items.Add(_freezeFirstColumnMenuItem);
        viewMenu.Items.Add(_unfreezePanesMenuItem);
        viewMenu.Items.Add(new NativeMenuItemSeparator());
        viewMenu.Items.Add(_showFormulasMenuItem);

        var sheetMenu = new NativeMenu();
        sheetMenu.Items.Add(_newSheetMenuItem);
        sheetMenu.Items.Add(_renameSheetMenuItem);
        sheetMenu.Items.Add(_duplicateSheetMenuItem);
        sheetMenu.Items.Add(_moveSheetLeftMenuItem);
        sheetMenu.Items.Add(_moveSheetRightMenuItem);
        sheetMenu.Items.Add(_tabColorMenuItem);
        sheetMenu.Items.Add(_selectAllSheetsMenuItem);
        sheetMenu.Items.Add(_ungroupSheetsMenuItem);
        sheetMenu.Items.Add(new NativeMenuItemSeparator());
        sheetMenu.Items.Add(_hideSheetMenuItem);
        sheetMenu.Items.Add(_unhideSheetMenuItem);
        sheetMenu.Items.Add(new NativeMenuItemSeparator());
        sheetMenu.Items.Add(_deleteSheetMenuItem);

        var helpMenu = new NativeMenu();
        helpMenu.Items.Add(_helpOnlineMenuItem);
        helpMenu.Items.Add(_sendFeedbackMenuItem);
        helpMenu.Items.Add(_checkForUpdatesMenuItem);
        helpMenu.Items.Add(new NativeMenuItemSeparator());
        helpMenu.Items.Add(_aboutMenuItem);
        helpMenu.Items.Add(_legalNoticesMenuItem);

        _nativeMenu = new NativeMenu();
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "File",
            Menu = fileMenu,
        });
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "Edit",
            Menu = editMenu,
        });
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "Format",
            Menu = formatMenu,
        });
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "View",
            Menu = viewMenu,
        });
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "Sheet",
            Menu = sheetMenu,
        });
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "Help",
            Menu = helpMenu,
        });
        _nativeMenu.NeedsUpdate += (_, _) => UpdateSaveButton();

        NativeMenu.SetMenu(this, _nativeMenu);
    }

    private void ConfigureWorkbookDropTarget()
    {
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, MainWindow_DragOver);
        DragDrop.AddDropHandler(this, MainWindow_Drop);
    }

    private Control BuildToolbar()
    {
        _titleText.FontSize = 14;
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.Foreground = Brush(25, 31, 40);
        _titleText.MaxWidth = 180;
        _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        _titleText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _detailText.FontSize = 12;
        _detailText.Foreground = Brush(94, 103, 116);
        _detailText.MaxWidth = 220;
        _detailText.TextTrimming = TextTrimming.CharacterEllipsis;
        _detailText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _statusText.FontSize = 12;
        _statusText.MaxWidth = 180;
        _statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        _statusText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _selectionStatsText.FontSize = 12;
        _selectionStatsText.Foreground = Brush(73, 80, 93);
        _selectionStatsText.MaxWidth = 420;
        _selectionStatsText.TextTrimming = TextTrimming.CharacterEllipsis;
        _selectionStatsText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _zoomText.FontSize = 12;
        _zoomText.FontWeight = FontWeight.SemiBold;
        _zoomText.Foreground = Brush(73, 80, 93);
        _zoomText.MinWidth = 44;
        _zoomText.TextAlignment = TextAlignment.Right;
        _zoomText.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _zoomText.Focusable = true;
        AutomationProperties.SetName(_zoomText, "Zoom");
        AutomationProperties.SetHelpText(_zoomText, "Shows the active worksheet zoom.");

        _openButton.Content = "Open";
        _openButton.Padding = new Thickness(10, 4);
        _openButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _openButton.Click += OpenButton_Click;

        _saveButton.Content = "Save";
        _saveButton.Padding = new Thickness(10, 4);
        _saveButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _saveButton.Click += SaveButton_Click;

        _saveAsButton.Content = "Save As";
        _saveAsButton.Padding = new Thickness(10, 4);
        _saveAsButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _saveAsButton.Click += SaveAsButton_Click;

        _undoButton.Content = "Undo";
        _undoButton.Padding = new Thickness(10, 4);
        _undoButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _undoButton.Click += UndoButton_Click;

        _redoButton.Content = "Redo";
        _redoButton.Padding = new Thickness(10, 4);
        _redoButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _redoButton.Click += RedoButton_Click;

        _cutButton.Content = "Cut";
        _cutButton.Padding = new Thickness(10, 4);
        _cutButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _cutButton.Click += CutButton_Click;

        _copyButton.Content = "Copy";
        _copyButton.Padding = new Thickness(10, 4);
        _copyButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _copyButton.Click += CopyButton_Click;

        _pasteButton.Content = "Paste";
        _pasteButton.Padding = new Thickness(10, 4);
        _pasteButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _pasteButton.Click += PasteButton_Click;

        _pasteSpecialButton.Content = "Paste Special";
        _pasteSpecialButton.Padding = new Thickness(10, 4);
        _pasteSpecialButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _pasteSpecialButton.Flyout = CreatePasteSpecialFlyout();

        _formatPainterButton.Content = "Format Painter";
        _formatPainterButton.Padding = new Thickness(10, 4);
        _formatPainterButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _formatPainterButton.Click += FormatPainterButton_Click;
        _formatPainterButton.DoubleTapped += (_, args) =>
        {
            CaptureFormatPainterSource(persistent: true);
            args.Handled = true;
        };
        AutomationProperties.SetAutomationId(_formatPainterButton, "HomeFormatPainterButton");
        AutomationProperties.SetName(_formatPainterButton, "Format Painter");
        AutomationProperties.SetHelpText(_formatPainterButton, "Copy formatting from the selection and apply it to another range.");

        _autoSumButton.Content = "AutoSum";
        _autoSumButton.Padding = new Thickness(10, 4);
        _autoSumButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _autoSumButton.Click += AutoSumButton_Click;
        _autoSumButton.Flyout = CreateAutoSumFlyout();
        AutomationProperties.SetAutomationId(_autoSumButton, "HomeAutoSumButton");
        AutomationProperties.SetName(_autoSumButton, "AutoSum");
        AutomationProperties.SetHelpText(_autoSumButton, "Insert a formula using nearby numeric cells.");

        _autoSumSumFlyoutItem.Header = "Sum";
        _autoSumSumFlyoutItem.Click += (_, _) => InsertAutoSumFormula("SUM");

        _autoSumAverageFlyoutItem.Header = "Average";
        _autoSumAverageFlyoutItem.Click += (_, _) => InsertAutoSumFormula("AVERAGE");

        _autoSumCountNumbersFlyoutItem.Header = "Count Numbers";
        _autoSumCountNumbersFlyoutItem.Click += (_, _) => InsertAutoSumFormula("COUNT");

        _autoSumCountAllFlyoutItem.Header = "Count All";
        _autoSumCountAllFlyoutItem.Click += (_, _) => InsertAutoSumFormula("COUNTA");

        _autoSumMaxFlyoutItem.Header = "Max";
        _autoSumMaxFlyoutItem.Click += (_, _) => InsertAutoSumFormula("MAX");

        _autoSumMinFlyoutItem.Header = "Min";
        _autoSumMinFlyoutItem.Click += (_, _) => InsertAutoSumFormula("MIN");

        _fillCellsButton.Content = "Fill Cells";
        _fillCellsButton.Padding = new Thickness(10, 4);
        _fillCellsButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _fillCellsButton.Flyout = CreateFillCellsFlyout();
        AutomationProperties.SetAutomationId(_fillCellsButton, "HomeFillCellsButton");
        AutomationProperties.SetName(_fillCellsButton, "Fill Cells");
        AutomationProperties.SetHelpText(_fillCellsButton, "Copy the edge cells across the selected range.");

        _fillDownFlyoutItem.Header = "Down";
        _fillDownFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Down);

        _fillRightFlyoutItem.Header = "Right";
        _fillRightFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Right);

        _fillUpFlyoutItem.Header = "Up";
        _fillUpFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Up);

        _fillLeftFlyoutItem.Header = "Left";
        _fillLeftFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Left);

        _clearButton.Content = "Clear";
        _clearButton.Padding = new Thickness(10, 4);
        _clearButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _clearButton.Flyout = CreateClearFlyout();
        _clearButton.Click += ClearButton_Click;
        AutomationProperties.SetAutomationId(_clearButton, "HomeClearButton");
        AutomationProperties.SetName(_clearButton, "Clear");
        AutomationProperties.SetHelpText(_clearButton, "Clear contents, formatting, comments, hyperlinks, or all cell state from the selected range.");

        _clearAllFlyoutItem.Header = "Clear All";
        _clearAllFlyoutItem.Click += (_, _) => ClearSelectedRangeAll();

        _clearFormatsFlyoutItem.Header = "Clear Formats";
        _clearFormatsFlyoutItem.Click += (_, _) => ClearSelectedRangeFormats();

        _clearContentsFlyoutItem.Header = "Clear Contents";
        _clearContentsFlyoutItem.Click += (_, _) => ClearSelectedRangeContents();

        _clearCommentsFlyoutItem.Header = "Clear Comments and Notes";
        _clearCommentsFlyoutItem.Click += (_, _) => ClearSelectedRangeComments();

        _clearHyperlinksFlyoutItem.Header = "Clear Hyperlinks";
        _clearHyperlinksFlyoutItem.Click += (_, _) => ClearSelectedRangeHyperlinks();

        _boldButton.Content = "B";
        _boldButton.FontWeight = FontWeight.Bold;
        _boldButton.Padding = new Thickness(10, 4);
        _boldButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _boldButton.Click += BoldButton_Click;

        _italicButton.Content = "I";
        _italicButton.FontStyle = FontStyle.Italic;
        _italicButton.Padding = new Thickness(10, 4);
        _italicButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _italicButton.Click += ItalicButton_Click;

        _underlineButton.Content = new TextBlock
        {
            Text = "U",
            TextDecorations = TextDecorations.Underline,
        };
        _underlineButton.Padding = new Thickness(10, 4);
        _underlineButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _underlineButton.Click += UnderlineButton_Click;

        _doubleUnderlineButton.Content = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock
                {
                    Text = "U",
                    FontWeight = FontWeight.SemiBold,
                },
                new Border
                {
                    Height = 1,
                    Width = 12,
                    Background = Brush(25, 31, 40),
                },
                new Border
                {
                    Height = 1,
                    Width = 12,
                    Background = Brush(25, 31, 40),
                },
            },
        };
        _doubleUnderlineButton.Padding = new Thickness(10, 3);
        _doubleUnderlineButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _doubleUnderlineButton.Click += DoubleUnderlineButton_Click;

        _strikethroughButton.Content = new TextBlock
        {
            Text = "S",
            TextDecorations = TextDecorations.Strikethrough,
        };
        _strikethroughButton.Padding = new Thickness(10, 4);
        _strikethroughButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _strikethroughButton.Click += StrikethroughButton_Click;

        _increaseFontSizeButton.Content = "A+";
        _increaseFontSizeButton.Padding = new Thickness(10, 4);
        _increaseFontSizeButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _increaseFontSizeButton.Click += IncreaseFontSizeButton_Click;

        _decreaseFontSizeButton.Content = "A-";
        _decreaseFontSizeButton.Padding = new Thickness(10, 4);
        _decreaseFontSizeButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _decreaseFontSizeButton.Click += DecreaseFontSizeButton_Click;

        _fillColorButton.Content = "Fill";
        _fillColorButton.Padding = new Thickness(10, 4);
        _fillColorButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _fillColorButton.Flyout = CreateColorPaletteFlyout(ColorPaletteTarget.Fill, includeClearFill: true);

        _fontColorButton.Content = "A";
        _fontColorButton.Padding = new Thickness(10, 4);
        _fontColorButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _fontColorButton.Flyout = CreateColorPaletteFlyout(ColorPaletteTarget.Font, includeClearFill: false);

        _bordersButton.Content = "Borders";
        _bordersButton.Padding = new Thickness(10, 4);
        _bordersButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _bordersButton.Flyout = CreateBorderPresetFlyout();
        AutomationProperties.SetAutomationId(_bordersButton, "HomeBordersButton");
        AutomationProperties.SetName(_bordersButton, "Borders");
        AutomationProperties.SetHelpText(_bordersButton, "Apply or change borders on the selected cells.");

        _cellStylesButton.Content = "Styles";
        _cellStylesButton.Padding = new Thickness(10, 4);
        _cellStylesButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _cellStylesButton.Flyout = CreateCellStylesFlyout();

        _orientationButton.Content = "Orient";
        _orientationButton.Padding = new Thickness(10, 4);
        _orientationButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _orientationButton.Flyout = CreateTextRotationFlyout();

        _currencyFormatButton.Content = "$";
        _currencyFormatButton.Padding = new Thickness(10, 4);
        _currencyFormatButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _currencyFormatButton.Click += CurrencyFormatButton_Click;

        _percentFormatButton.Content = "%";
        _percentFormatButton.Padding = new Thickness(10, 4);
        _percentFormatButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _percentFormatButton.Click += PercentFormatButton_Click;

        _commaStyleButton.Content = ",";
        _commaStyleButton.Padding = new Thickness(10, 4);
        _commaStyleButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _commaStyleButton.Click += CommaStyleButton_Click;

        _increaseDecimalButton.Content = "+.0";
        _increaseDecimalButton.Padding = new Thickness(10, 4);
        _increaseDecimalButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _increaseDecimalButton.Click += IncreaseDecimalButton_Click;

        _decreaseDecimalButton.Content = "-.0";
        _decreaseDecimalButton.Padding = new Thickness(10, 4);
        _decreaseDecimalButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _decreaseDecimalButton.Click += DecreaseDecimalButton_Click;

        _alignTopButton.Content = "Top";
        _alignTopButton.Padding = new Thickness(10, 4);
        _alignTopButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _alignTopButton.Click += AlignTopButton_Click;

        _alignMiddleButton.Content = "Mid";
        _alignMiddleButton.Padding = new Thickness(10, 4);
        _alignMiddleButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _alignMiddleButton.Click += AlignMiddleButton_Click;

        _alignBottomButton.Content = "Bot";
        _alignBottomButton.Padding = new Thickness(10, 4);
        _alignBottomButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _alignBottomButton.Click += AlignBottomButton_Click;

        _wrapTextButton.Content = "Wrap";
        _wrapTextButton.Padding = new Thickness(10, 4);
        _wrapTextButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _wrapTextButton.Click += WrapTextButton_Click;
        AutomationProperties.SetAutomationId(_wrapTextButton, "HomeWrapTextButton");
        AutomationProperties.SetName(_wrapTextButton, "Wrap Text");
        AutomationProperties.SetHelpText(_wrapTextButton, "Wrap text within the selected cells.");

        _mergeAndCenterButton.Content = "Merge & Center";
        _mergeAndCenterButton.Padding = new Thickness(10, 4);
        _mergeAndCenterButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _mergeAndCenterButton.Click += MergeAndCenterButton_Click;
        AutomationProperties.SetAutomationId(_mergeAndCenterButton, "HomeMergeAndCenterButton");
        AutomationProperties.SetName(_mergeAndCenterButton, "Merge & Center");
        AutomationProperties.SetHelpText(_mergeAndCenterButton, "Merge and center the selected cells.");

        _decreaseIndentButton.Content = "Out";
        _decreaseIndentButton.Padding = new Thickness(10, 4);
        _decreaseIndentButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _decreaseIndentButton.Click += DecreaseIndentButton_Click;

        _increaseIndentButton.Content = "In";
        _increaseIndentButton.Padding = new Thickness(10, 4);
        _increaseIndentButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _increaseIndentButton.Click += IncreaseIndentButton_Click;

        _alignLeftButton.Content = "L";
        _alignLeftButton.Padding = new Thickness(10, 4);
        _alignLeftButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _alignLeftButton.Click += AlignLeftButton_Click;

        _alignCenterButton.Content = "C";
        _alignCenterButton.Padding = new Thickness(10, 4);
        _alignCenterButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _alignCenterButton.Click += AlignCenterButton_Click;

        _alignRightButton.Content = "R";
        _alignRightButton.Padding = new Thickness(10, 4);
        _alignRightButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _alignRightButton.Click += AlignRightButton_Click;

        _cellAddressText.Width = 72;
        _cellAddressText.FontSize = 12;
        _cellAddressText.FontWeight = FontWeight.SemiBold;
        _cellAddressText.Foreground = Brush(28, 38, 48);
        _cellAddressText.TextAlignment = TextAlignment.Center;
        _cellAddressText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _formulaBox.MinWidth = 320;
        _formulaBox.FontSize = 12;
        _formulaBox.Padding = new Thickness(8, 4);
        _formulaBox.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _formulaBox.GotFocus += FormulaBox_GotFocus;
        _formulaBox.KeyDown += FormulaBox_KeyDown;

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    _titleText,
                    _detailText,
                    _openButton,
                    _saveButton,
                    _saveAsButton,
                    _undoButton,
                    _redoButton,
                    _cutButton,
                    _copyButton,
                    _pasteButton,
                    _pasteSpecialButton,
                    _formatPainterButton,
                    _autoSumButton,
                    _fillCellsButton,
                    _clearButton,
                    _boldButton,
                    _italicButton,
                    _underlineButton,
                    _doubleUnderlineButton,
                    _strikethroughButton,
                    _increaseFontSizeButton,
                    _decreaseFontSizeButton,
                    _fillColorButton,
                    _fontColorButton,
                    _bordersButton,
                    _cellStylesButton,
                    _orientationButton,
                    _currencyFormatButton,
                    _percentFormatButton,
                    _commaStyleButton,
                    _increaseDecimalButton,
                    _decreaseDecimalButton,
                    _alignTopButton,
                    _alignMiddleButton,
                    _alignBottomButton,
                    _wrapTextButton,
                    _mergeAndCenterButton,
                    _decreaseIndentButton,
                    _increaseIndentButton,
                    _alignLeftButton,
                    _alignCenterButton,
                    _alignRightButton,
                    _cellAddressText,
                    _formulaBox,
                    _statusText,
                    _selectionStatsText,
                    _zoomText,
                },
            },
        };
    }

    private string FormatWindowWorkbookTitle() =>
        _session.IsWorkbookGrouped
            ? $"{_session.DisplayName} [Group]"
            : _session.DisplayName;

    private void RefreshShell(string status)
    {
        var preserveFormulaEdit = _formulaBox.IsFocused && _session.FormulaEditAddress is not null;
        var formulaText = _formulaBox.Text;
        var formulaCaretIndex = _formulaBox.CaretIndex;
        var formulaSelectionStart = _formulaBox.SelectionStart;
        var formulaSelectionEnd = _formulaBox.SelectionEnd;

        _sheetGridHost.Content = BuildSheetGrid();
        _sheetTabsHost.Content = BuildSheetTabs();
        _titleText.Text = FormatWindowWorkbookTitle();
        _detailText.Text = $"{_session.ActiveSheet.Name}  |  {_session.Viewport.RowMetrics.Count} rows x {_session.Viewport.ColMetrics.Count} columns";
        _cellAddressText.Text = FormatCellReference(_session.ActiveCell);
        _formulaBox.Text = preserveFormulaEdit
            ? formulaText
            : FormatEditText(_session.ActiveSheet.GetCell(_session.ActiveCell), _session.ActiveCell);
        _boldButton.IsChecked = _session.IsSelectedRangeStartBold;
        _italicButton.IsChecked = _session.IsSelectedRangeStartItalic;
        _underlineButton.IsChecked = _session.IsSelectedRangeStartUnderline;
        _doubleUnderlineButton.IsChecked = _session.IsSelectedRangeStartDoubleUnderline;
        _strikethroughButton.IsChecked = _session.IsSelectedRangeStartStrikethrough;
        _alignLeftButton.IsChecked = _session.SelectedRangeStartHorizontalAlignment == CellHAlign.Left;
        _alignCenterButton.IsChecked = _session.SelectedRangeStartHorizontalAlignment == CellHAlign.Center;
        _alignRightButton.IsChecked = _session.SelectedRangeStartHorizontalAlignment == CellHAlign.Right;
        _alignTopButton.IsChecked = _session.SelectedRangeStartVerticalAlignment == CellVAlign.Top;
        _alignMiddleButton.IsChecked = _session.SelectedRangeStartVerticalAlignment == CellVAlign.Center;
        _alignBottomButton.IsChecked = _session.SelectedRangeStartVerticalAlignment == CellVAlign.Bottom;
        _wrapTextButton.IsChecked = _session.IsSelectedRangeStartWrapText;
        if (preserveFormulaEdit)
        {
            _formulaBox.CaretIndex = Math.Min(formulaCaretIndex, _formulaBox.Text?.Length ?? 0);
            _formulaBox.SelectionStart = Math.Min(formulaSelectionStart, _formulaBox.Text?.Length ?? 0);
            _formulaBox.SelectionEnd = Math.Min(formulaSelectionEnd, _formulaBox.Text?.Length ?? 0);
        }

        _statusText.Text = status;
        _selectionStatsText.Text = _session.SelectionStatsText;
        _zoomText.Text = FormatZoomPercent(_session.ZoomPercent);
        _statusText.Foreground = ShouldUseWarningStatusColor(status)
            ? Brush(143, 74, 18)
            : Brush(67, 113, 83);
        Title = $"FreeX - {FormatWindowWorkbookTitle()}{(_session.IsDirty ? " *" : "")}";
        UpdateViewportScrollBars();
        UpdateSaveButton();
    }

    private void UpdateViewportScrollBars()
    {
        var state = WorkbookViewportScrollPlanner.Create(_session.ActiveSheet, _session.Viewport);
        _isUpdatingWorksheetScrollBars = true;
        try
        {
            ApplyWorksheetScrollAxis(_verticalWorksheetScrollBar, state.Vertical);
            ApplyWorksheetScrollAxis(_horizontalWorksheetScrollBar, state.Horizontal);
        }
        finally
        {
            _isUpdatingWorksheetScrollBars = false;
        }
    }

    private static void ApplyWorksheetScrollAxis(ScrollBar scrollBar, WorkbookViewportScrollAxis axis)
    {
        scrollBar.Minimum = axis.Minimum;
        scrollBar.Maximum = axis.Maximum;
        scrollBar.ViewportSize = axis.ViewportSize;
        scrollBar.SmallChange = axis.SmallChange;
        scrollBar.LargeChange = axis.LargeChange;
        scrollBar.Value = Math.Clamp(axis.Value, axis.Minimum, axis.Maximum);
        scrollBar.IsEnabled = axis.IsEnabled;
    }

    private void UpdateSaveButton()
    {
        var isIdle = !_isOpening && !_isSaving;
        _openButton.IsEnabled = isIdle && StorageProvider.CanOpen;
        _saveButton.IsEnabled = isIdle && _session.CanSaveCurrentSource(out _);
        _saveButton.Content = _session.IsDirty ? "Save*" : "Save";
        _saveAsButton.IsEnabled = isIdle && StorageProvider.CanSave;
        _newSheetButton.IsEnabled = isIdle;
        _undoButton.IsEnabled = isIdle && _session.CanUndo;
        _redoButton.IsEnabled = isIdle && _session.CanRedo;
        _cutButton.IsEnabled = isIdle;
        _copyButton.IsEnabled = isIdle;
        _pasteButton.IsEnabled = isIdle;
        _pasteSpecialButton.IsEnabled = isIdle;
        _formatPainterButton.IsEnabled = isIdle;
        _autoSumButton.IsEnabled = isIdle;
        _autoSumSumFlyoutItem.IsEnabled = isIdle;
        _autoSumAverageFlyoutItem.IsEnabled = isIdle;
        _autoSumCountNumbersFlyoutItem.IsEnabled = isIdle;
        _autoSumCountAllFlyoutItem.IsEnabled = isIdle;
        _autoSumMaxFlyoutItem.IsEnabled = isIdle;
        _autoSumMinFlyoutItem.IsEnabled = isIdle;
        _fillDownFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Down);
        _fillRightFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Right);
        _fillUpFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Up);
        _fillLeftFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Left);
        _fillCellsButton.IsEnabled = _fillDownFlyoutItem.IsEnabled ||
            _fillRightFlyoutItem.IsEnabled ||
            _fillUpFlyoutItem.IsEnabled ||
            _fillLeftFlyoutItem.IsEnabled;
        _clearButton.IsEnabled = isIdle;
        _boldButton.IsEnabled = isIdle;
        _italicButton.IsEnabled = isIdle;
        _underlineButton.IsEnabled = isIdle;
        _doubleUnderlineButton.IsEnabled = isIdle;
        _strikethroughButton.IsEnabled = isIdle;
        _increaseFontSizeButton.IsEnabled = isIdle;
        _decreaseFontSizeButton.IsEnabled = isIdle;
        _fillColorButton.IsEnabled = isIdle;
        _fontColorButton.IsEnabled = isIdle;
        _bordersButton.IsEnabled = isIdle;
        _cellStylesButton.IsEnabled = isIdle;
        _orientationButton.IsEnabled = isIdle;
        _currencyFormatButton.IsEnabled = isIdle;
        _percentFormatButton.IsEnabled = isIdle;
        _commaStyleButton.IsEnabled = isIdle;
        _increaseDecimalButton.IsEnabled = isIdle;
        _decreaseDecimalButton.IsEnabled = isIdle;
        _alignLeftButton.IsEnabled = isIdle;
        _alignCenterButton.IsEnabled = isIdle;
        _alignRightButton.IsEnabled = isIdle;
        _alignTopButton.IsEnabled = isIdle;
        _alignMiddleButton.IsEnabled = isIdle;
        _alignBottomButton.IsEnabled = isIdle;
        _wrapTextButton.IsEnabled = isIdle;
        _mergeAndCenterButton.IsEnabled = isIdle;
        _decreaseIndentButton.IsEnabled = isIdle;
        _increaseIndentButton.IsEnabled = isIdle;

        _newWorkbookMenuItem.IsEnabled = isIdle;
        _openMenuItem.IsEnabled = _openButton.IsEnabled;
        _openRecentMenuItem.IsEnabled = isIdle;
        RefreshNativeOpenRecentMenu(isIdle);
        _saveMenuItem.IsEnabled = _saveButton.IsEnabled;
        _saveAsMenuItem.IsEnabled = _saveAsButton.IsEnabled;
        _closeWorkbookMenuItem.IsEnabled = isIdle;
        var activeSheetTabIndex = FindActiveSheetTabIndex();
        _newSheetMenuItem.IsEnabled = _newSheetButton.IsEnabled;
        _renameSheetMenuItem.IsEnabled = isIdle;
        _duplicateSheetMenuItem.IsEnabled = isIdle;
        _moveSheetLeftMenuItem.IsEnabled = isIdle && activeSheetTabIndex > 0;
        _moveSheetRightMenuItem.IsEnabled =
            isIdle &&
            activeSheetTabIndex >= 0 &&
            activeSheetTabIndex < _session.SheetTabs.Count - 1;
        _tabColorMenuItem.IsEnabled = isIdle;
        _selectAllSheetsMenuItem.IsEnabled = isIdle && _session.SheetTabs.Count > 1;
        _ungroupSheetsMenuItem.IsEnabled = isIdle && _session.IsWorkbookGrouped;
        _hideSheetMenuItem.IsEnabled = isIdle && _session.CanHideActiveSheet;
        _unhideSheetMenuItem.IsEnabled = isIdle && _session.HiddenSheets.Count > 0;
        _deleteSheetMenuItem.IsEnabled = isIdle;
        _undoMenuItem.IsEnabled = _undoButton.IsEnabled;
        _redoMenuItem.IsEnabled = _redoButton.IsEnabled;
        _cutMenuItem.IsEnabled = _cutButton.IsEnabled;
        _copyMenuItem.IsEnabled = _copyButton.IsEnabled;
        _pasteMenuItem.IsEnabled = _pasteButton.IsEnabled;
        _pasteSpecialMenuItem.IsEnabled = _pasteSpecialButton.IsEnabled;
        _formatPainterMenuItem.IsEnabled = _formatPainterButton.IsEnabled;
        _selectAllMenuItem.IsEnabled = isIdle;
        _findMenuItem.IsEnabled = isIdle;
        _findNextMenuItem.IsEnabled = isIdle && !string.IsNullOrWhiteSpace(_session.LastFindText);
        _replaceMenuItem.IsEnabled = isIdle;
        _goToMenuItem.IsEnabled = isIdle;
        _goToSpecialMenuItem.IsEnabled = isIdle;
        _autoSumMenuItem.IsEnabled = _autoSumButton.IsEnabled;
        _autoSumSumMenuItem.IsEnabled = _autoSumButton.IsEnabled;
        _autoSumAverageMenuItem.IsEnabled = _autoSumButton.IsEnabled;
        _autoSumCountNumbersMenuItem.IsEnabled = _autoSumButton.IsEnabled;
        _autoSumCountAllMenuItem.IsEnabled = _autoSumButton.IsEnabled;
        _autoSumMaxMenuItem.IsEnabled = _autoSumButton.IsEnabled;
        _autoSumMinMenuItem.IsEnabled = _autoSumButton.IsEnabled;
        _fillCellsMenuItem.IsEnabled = _fillCellsButton.IsEnabled;
        _fillDownMenuItem.IsEnabled = _fillDownFlyoutItem.IsEnabled;
        _fillRightMenuItem.IsEnabled = _fillRightFlyoutItem.IsEnabled;
        _fillUpMenuItem.IsEnabled = _fillUpFlyoutItem.IsEnabled;
        _fillLeftMenuItem.IsEnabled = _fillLeftFlyoutItem.IsEnabled;
        _clearMenuItem.IsEnabled = _clearButton.IsEnabled;
        _clearAllMenuItem.IsEnabled = _clearButton.IsEnabled;
        _clearFormatsMenuItem.IsEnabled = _clearButton.IsEnabled;
        _clearContentsMenuItem.IsEnabled = _clearButton.IsEnabled;
        _clearCommentsMenuItem.IsEnabled = _clearButton.IsEnabled;
        _clearHyperlinksMenuItem.IsEnabled = _clearButton.IsEnabled;
        _boldMenuItem.IsEnabled = _boldButton.IsEnabled;
        _italicMenuItem.IsEnabled = _italicButton.IsEnabled;
        _underlineMenuItem.IsEnabled = _underlineButton.IsEnabled;
        _doubleUnderlineMenuItem.IsEnabled = _doubleUnderlineButton.IsEnabled;
        _strikethroughMenuItem.IsEnabled = _strikethroughButton.IsEnabled;
        _increaseFontSizeMenuItem.IsEnabled = _increaseFontSizeButton.IsEnabled;
        _decreaseFontSizeMenuItem.IsEnabled = _decreaseFontSizeButton.IsEnabled;
        _fillColorMenuItem.IsEnabled = _fillColorButton.IsEnabled;
        _clearFillMenuItem.IsEnabled = _fillColorButton.IsEnabled;
        _fontColorMenuItem.IsEnabled = _fontColorButton.IsEnabled;
        _bordersMenuItem.IsEnabled = _bordersButton.IsEnabled;
        _cellStylesMenuItem.IsEnabled = _cellStylesButton.IsEnabled;
        _horizontalTextMenuItem.IsEnabled = isIdle;
        _angleCounterclockwiseMenuItem.IsEnabled = isIdle;
        _angleClockwiseMenuItem.IsEnabled = isIdle;
        _verticalTextMenuItem.IsEnabled = isIdle;
        _rotateTextUpMenuItem.IsEnabled = isIdle;
        _rotateTextDownMenuItem.IsEnabled = isIdle;
        _currencyFormatMenuItem.IsEnabled = _currencyFormatButton.IsEnabled;
        _percentFormatMenuItem.IsEnabled = _percentFormatButton.IsEnabled;
        _commaStyleMenuItem.IsEnabled = _commaStyleButton.IsEnabled;
        _increaseDecimalMenuItem.IsEnabled = _increaseDecimalButton.IsEnabled;
        _decreaseDecimalMenuItem.IsEnabled = _decreaseDecimalButton.IsEnabled;
        _alignLeftMenuItem.IsEnabled = _alignLeftButton.IsEnabled;
        _alignCenterMenuItem.IsEnabled = _alignCenterButton.IsEnabled;
        _alignRightMenuItem.IsEnabled = _alignRightButton.IsEnabled;
        _alignTopMenuItem.IsEnabled = _alignTopButton.IsEnabled;
        _alignMiddleMenuItem.IsEnabled = _alignMiddleButton.IsEnabled;
        _alignBottomMenuItem.IsEnabled = _alignBottomButton.IsEnabled;
        _wrapTextMenuItem.IsEnabled = _wrapTextButton.IsEnabled;
        _mergeAndCenterMenuItem.IsEnabled = _mergeAndCenterButton.IsEnabled;
        _unmergeCellsMenuItem.IsEnabled = isIdle && _session.IsSelectedRangeMerged;
        _decreaseIndentMenuItem.IsEnabled = _decreaseIndentButton.IsEnabled;
        _increaseIndentMenuItem.IsEnabled = _increaseIndentButton.IsEnabled;
        _showGridlinesMenuItem.IsEnabled = isIdle;
        _showGridlinesMenuItem.IsChecked = _session.IsShowingGridlines;
        _showHeadingsMenuItem.IsEnabled = isIdle;
        _showHeadingsMenuItem.IsChecked = _session.IsShowingHeadings;
        _zoomInMenuItem.IsEnabled = isIdle && _session.ZoomPercent < SetWorksheetZoomCommand.MaxZoomPercent;
        _zoomOutMenuItem.IsEnabled = isIdle && _session.ZoomPercent > SetWorksheetZoomCommand.MinZoomPercent;
        _zoom100MenuItem.IsEnabled = isIdle;
        _zoomToSelectionMenuItem.IsEnabled = isIdle;
        _freezePanesMenuItem.IsEnabled = isIdle;
        _freezeTopRowMenuItem.IsEnabled = isIdle;
        _freezeFirstColumnMenuItem.IsEnabled = isIdle;
        _unfreezePanesMenuItem.IsEnabled = isIdle;
        _showFormulasMenuItem.IsEnabled = isIdle;
        _showFormulasMenuItem.IsChecked = _session.IsShowingFormulas;
    }

    private int FindActiveSheetTabIndex()
        => FindSheetTabIndex(_session.ActiveSheet.Id);

    private int FindSheetTabIndex(SheetId sheetId)
    {
        for (var index = 0; index < _session.SheetTabs.Count; index++)
        {
            if (_session.SheetTabs[index].Id == sheetId)
                return index;
        }

        return -1;
    }

    private Control BuildSheetTabs()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
        };

        foreach (var tab in _session.SheetTabs)
        {
            var isGroupedTab = tab.IsGrouped && _session.IsWorkbookGrouped;
            var content = new AvaloniaGrid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                    new RowDefinition { Height = new GridLength(3) },
                },
            };
            var label = new TextBlock
            {
                Text = tab.Name,
                FontSize = 12,
                FontWeight = tab.IsActive || isGroupedTab ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = tab.IsActive ? SelectionHeaderForeground : HeaderForeground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            var tabColorRule = new Border
            {
                Background = tab.TabColor is { } tabColor ? Brush(tabColor) : Brushes.Transparent,
                IsHitTestVisible = false,
            };
            AvaloniaGrid.SetRow(label, 0);
            AvaloniaGrid.SetRow(tabColorRule, 1);
            content.Children.Add(label);
            content.Children.Add(tabColorRule);

            var button = new Button
            {
                MinWidth = 72,
                MaxWidth = 180,
                MinHeight = 28,
                Focusable = true,
                Padding = new Thickness(12, 4),
                Background = tab.IsActive
                    ? SelectionHeaderBackground
                    : isGroupedTab
                        ? Brush(236, 246, 255)
                        : Brushes.White,
                BorderBrush = tab.IsActive || isGroupedTab ? SelectionBorder : ToolbarBorder,
                BorderThickness = new Thickness(1),
                Content = content,
                Tag = tab.Id,
            };
            button.ContextMenu = CreateSheetTabContextMenu(tab);
            button.PointerPressed += (_, args) => SelectSheetFromPointer(tab.Id, args);
            button.DoubleTapped += async (_, args) => await RenameSheetFromTabAsync(tab.Id, args);
            button.KeyDown += (_, args) => HandleSheetTabKeyDown(tab.Id, button, args);
            button.Click += (_, _) => SelectSheet(tab.Id);
            AutomationProperties.SetName(button, tab.Name);
            AutomationProperties.SetHelpText(button, SheetTabContextHelpText);
            panel.Children.Add(button);
        }

        return panel;
    }

    private ContextMenu CreateSheetTabContextMenu(WorkbookSheetTab tab)
    {
        var isIdle = !_isOpening && !_isSaving;
        var sheetTabIndex = FindSheetTabIndex(tab.Id);
        var menu = new ContextMenu
        {
            ItemsSource = CreateSheetTabContextMenuItems(tab, isIdle, sheetTabIndex).ToArray(),
        };
        return menu;
    }

    private IEnumerable<Control> CreateSheetTabContextMenuItems(WorkbookSheetTab tab, bool isIdle, int sheetTabIndex)
    {
        yield return CreateSheetTabContextMenuItem(tab, "Rename...", async () => await RenameActiveSheetAsync(), isIdle);
        yield return CreateSheetTabContextMenuItem(tab, "Insert Sheet", AddNewSheet, isIdle);
        yield return CreateSheetTabContextMenuItem(tab, "Duplicate", DuplicateActiveSheet, isIdle);
        yield return CreateSheetTabContextMenuItem(tab, "Delete Sheet", DeleteActiveSheet, isIdle);
        yield return new Separator();
        yield return CreateSheetTabContextMenuItem(tab, "Hide", HideActiveSheet, isIdle && _session.SheetTabs.Count > 1);
        yield return CreateSheetTabContextMenuItem(tab, "Unhide...", async () => await UnhideSheetAsync(), isIdle && _session.HiddenSheets.Count > 0);
        yield return CreateSheetTabColorContextMenuItem(tab, isIdle);
        yield return new Separator();
        yield return CreateSheetTabContextMenuItem(tab, "Select All Sheets", SelectAllVisibleSheets, isIdle && _session.SheetTabs.Count > 1);
        yield return CreateSheetTabContextMenuItem(tab, "Ungroup Sheets", UngroupSheets, isIdle && _session.IsWorkbookGrouped);
        yield return new Separator();
        yield return CreateSheetTabContextMenuItem(tab, "Move Left", MoveActiveSheetLeft, isIdle && sheetTabIndex > 0);
        yield return CreateSheetTabContextMenuItem(
            tab,
            "Move Right",
            MoveActiveSheetRight,
            isIdle && sheetTabIndex >= 0 && sheetTabIndex < _session.SheetTabs.Count - 1);
    }

    private MenuItem CreateSheetTabContextMenuItem(WorkbookSheetTab tab, string header, Action action, bool isEnabled)
    {
        var menuItem = new MenuItem
        {
            Header = header,
            IsEnabled = isEnabled,
        };
        menuItem.Click += (_, _) =>
        {
            if (!SelectSheetForContextCommand(tab.Id))
                return;

            action();
        };
        return menuItem;
    }

    private MenuItem CreateSheetTabContextMenuItem(WorkbookSheetTab tab, string header, Func<Task> action, bool isEnabled)
    {
        var menuItem = new MenuItem
        {
            Header = header,
            IsEnabled = isEnabled,
        };
        menuItem.Click += async (_, _) =>
        {
            if (!SelectSheetForContextCommand(tab.Id))
                return;

            await action();
        };
        return menuItem;
    }

    private MenuItem CreateSheetTabColorContextMenuItem(WorkbookSheetTab tab, bool isEnabled)
    {
        var menuItem = new MenuItem
        {
            Header = "Tab Color",
            IsEnabled = isEnabled,
            ItemsSource = CreateSheetTabColorContextMenuItems(tab).ToArray(),
        };
        return menuItem;
    }

    private IEnumerable<MenuItem> CreateSheetTabColorContextMenuItems(WorkbookSheetTab tab)
    {
        var clearColorItem = new MenuItem { Header = "No Color" };
        clearColorItem.Click += (_, _) =>
        {
            if (SelectSheetForContextCommand(tab.Id))
                ApplyActiveSheetTabColor(null);
        };
        yield return clearColorItem;

        foreach (var swatch in CellColorPalettePlanner.BuildDefaultSwatches())
        {
            var menuItem = new MenuItem
            {
                Header = swatch.Hex,
                Icon = CreateColorSwatchIcon(swatch.Color),
            };
            menuItem.Click += (_, _) =>
            {
                if (SelectSheetForContextCommand(tab.Id))
                    ApplyActiveSheetTabColor(swatch.Color);
            };
            yield return menuItem;
        }
    }

    private Control BuildSheetGrid()
    {
        var viewport = _session.Viewport;
        var showHeadings = _session.ActiveSheet.ShowHeadings;
        var zoomFactor = GetActiveZoomFactor();
        var headerOffset = showHeadings ? 1 : 0;
        var cellsByAddress = viewport.Cells.ToDictionary(cell => (cell.Row, cell.Col));
        var grid = new AvaloniaGrid
        {
            Background = Brushes.White,
        };

        if (showHeadings)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeaderColumnWidth * zoomFactor) });
        foreach (var metric in viewport.ColMetrics)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GetDisplayedColumnWidth(metric, zoomFactor)) });

        if (showHeadings)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderRowHeight * zoomFactor) });
        foreach (var metric in viewport.RowMetrics)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GetDisplayedRowHeight(metric, zoomFactor)) });

        if (showHeadings)
        {
            AddGridChild(grid, CreateHeaderCell("", zoomFactor: zoomFactor), 0, 0);
            for (var colIndex = 0; colIndex < viewport.ColMetrics.Count; colIndex++)
            {
                var col = viewport.ColMetrics[colIndex].Col;
                var selected = IsSelectedColumn(col);
                AddGridChild(grid, CreateHeaderCell(CellAddress.NumberToColumnName(col), selected, zoomFactor), 0, colIndex + headerOffset);
            }
        }

        for (var rowIndex = 0; rowIndex < viewport.RowMetrics.Count; rowIndex++)
        {
            var rowMetric = viewport.RowMetrics[rowIndex];
            var row = rowMetric.Row;
            var rowHeight = GetDisplayedRowHeight(rowMetric, zoomFactor);
            if (showHeadings)
            {
                var selectedRow = IsSelectedRow(row);
                AddGridChild(grid, CreateHeaderCell(row.ToString(), selectedRow, zoomFactor), rowIndex + headerOffset, 0);
            }

            for (var colIndex = 0; colIndex < viewport.ColMetrics.Count; colIndex++)
            {
                var colMetric = viewport.ColMetrics[colIndex];
                var col = colMetric.Col;
                var colWidth = GetDisplayedColumnWidth(colMetric, zoomFactor);
                cellsByAddress.TryGetValue((row, col), out var cell);
                AddGridChild(grid, CreateCell(cell, row, col, zoomFactor, colWidth, rowHeight), rowIndex + headerOffset, colIndex + headerOffset);
            }
        }

        var overlay = BuildDrawingObjectOverlay(viewport);
        if (overlay.Children.Count == 0)
            return grid;

        return new AvaloniaGrid
        {
            ClipToBounds = true,
            Children =
            {
                grid,
                overlay,
            },
        };
    }

    private Canvas BuildDrawingObjectOverlay(ViewportModel viewport)
    {
        var showHeadings = _session.ActiveSheet.ShowHeadings;
        var zoomFactor = GetActiveZoomFactor();
        var overlay = new Canvas
        {
            Width = CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor),
            Height = CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor),
            ClipToBounds = true,
            IsHitTestVisible = true,
        };

        if (viewport.DrawingObjects is not { Count: > 0 })
            return overlay;

        foreach (var drawingObject in viewport.DrawingObjects)
        {
            if (!TryGetDisplayedDrawingObjectBounds(
                    viewport,
                    drawingObject,
                    showHeadings,
                    zoomFactor,
                    out var left,
                    out var top,
                    out var width,
                    out var height))
            {
                continue;
            }

            var visual = CreateSelectableDrawingObjectVisual(drawingObject, width, height);
            Canvas.SetLeft(visual, left);
            Canvas.SetTop(visual, top);
            overlay.Children.Add(visual);
        }

        return overlay;
    }

    private Control CreateSelectableDrawingObjectVisual(
        DrawingObjectBounds drawingObject,
        double width,
        double height)
    {
        var visual = CreateDrawingObjectVisual(drawingObject, width, height);
        var selected = IsSelectedDrawingObject(drawingObject);
        var container = new AvaloniaGrid
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = Brushes.Transparent,
            ClipToBounds = false,
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
        };

        AutomationProperties.SetAutomationId(container, $"DrawingObject{drawingObject.Kind}{drawingObject.Id:N}");
        AutomationProperties.SetName(container, $"{FormatDrawingObjectKind(drawingObject.Kind)} {drawingObject.DisplayName}");
        AutomationProperties.SetHelpText(container, "Selects this drawing object preview in the workbook viewport.");
        AutomationProperties.SetItemStatus(container, selected ? "Selected" : "Not selected");

        container.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(container).Properties.IsLeftButtonPressed)
            {
                SelectDrawingObject(drawingObject);
                args.Handled = true;
            }
        };
        container.KeyDown += (_, args) =>
        {
            if (args.Key is Key.Enter or Key.Space)
            {
                SelectDrawingObject(drawingObject);
                args.Handled = true;
            }
        };

        container.Children.Add(visual);
        if (selected)
            container.Children.Add(CreateSelectedDrawingObjectAdorner());

        return container;
    }

    private void SelectDrawingObject(DrawingObjectBounds drawingObject)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        _selectedDrawingObjectKind = drawingObject.Kind;
        _selectedDrawingObjectId = drawingObject.Id;
        RefreshShell($"Selected {FormatDrawingObjectKind(drawingObject.Kind)}: {drawingObject.DisplayName}");
    }

    private bool IsSelectedDrawingObject(DrawingObjectBounds drawingObject) =>
        _selectedDrawingObjectKind == drawingObject.Kind &&
        _selectedDrawingObjectId == drawingObject.Id;

    private void ClearSelectedDrawingObject()
    {
        _selectedDrawingObjectKind = null;
        _selectedDrawingObjectId = null;
    }

    private static Border CreateSelectedDrawingObjectAdorner() =>
        new()
        {
            BorderBrush = SelectionBorder,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
        };

    private static string FormatDrawingObjectKind(SelectionPaneObjectKind kind) =>
        kind switch
        {
            SelectionPaneObjectKind.Picture => "Picture",
            SelectionPaneObjectKind.Shape => "Shape",
            SelectionPaneObjectKind.TextBox => "Text box",
            _ => "Drawing object"
        };

    private static Control CreateDrawingObjectVisual(
        DrawingObjectBounds drawingObject,
        double width,
        double height)
    {
        var visual = drawingObject.Kind switch
        {
            SelectionPaneObjectKind.Shape => CreateDrawingShapeVisual(drawingObject, width, height),
            SelectionPaneObjectKind.Picture => CreateDrawingPictureVisual(drawingObject, width, height),
            SelectionPaneObjectKind.TextBox => CreateDrawingTextBoxVisual(drawingObject, width, height),
            _ => CreateDrawingObjectBoundsMarker(drawingObject, width, height)
        };
        ApplyDrawingObjectRotation(visual, drawingObject.RotationDegrees);
        return visual;
    }

    private static Control CreateDrawingShapeVisual(
        DrawingObjectBounds drawingObject,
        double width,
        double height)
    {
        var fill = Brush(drawingObject.FillColor ?? new CellColor(0x5B, 0x9B, 0xD5));
        var stroke = Brush(drawingObject.OutlineColor ?? new CellColor(0x2F, 0x55, 0x97));
        return drawingObject.ShapeKind switch
        {
            DrawingShapeKind.Ellipse => new AvaloniaEllipse
            {
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            },
            DrawingShapeKind.Line => CreateDrawingLineVisual(stroke, width),
            _ => new AvaloniaRectangle
            {
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            }
        };
    }

    private static Border CreateDrawingLineVisual(IBrush stroke, double width) =>
        new()
        {
            Width = Math.Max(1, width),
            Height = 2,
            Background = stroke,
            IsHitTestVisible = false,
        };

    private static Control CreateDrawingPictureVisual(
        DrawingObjectBounds drawingObject,
        double width,
        double height)
    {
        if (drawingObject.ImageBytes is { Length: > 0 } imageBytes &&
            TryCreateDrawingBitmap(imageBytes, out var bitmap))
        {
            return new Border
            {
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                BorderBrush = DrawingObjectBoundsBorder,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                IsHitTestVisible = false,
                Child = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                },
            };
        }

        return CreateDrawingObjectBoundsMarker(drawingObject, width, height);
    }

    private static Control CreateDrawingTextBoxVisual(
        DrawingObjectBounds drawingObject,
        double width,
        double height) =>
        new Border
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = Brush(drawingObject.FillColor ?? CellColor.White),
            BorderBrush = Brush(drawingObject.OutlineColor ?? new CellColor(0x70, 0x70, 0x70)),
            BorderThickness = new Thickness(1.5),
            ClipToBounds = true,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(drawingObject.Text)
                    ? drawingObject.DisplayName
                    : drawingObject.Text,
                FontSize = 12,
                Foreground = Brushes.Black,
                Margin = new Thickness(6, 4),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };

    private static Border CreateDrawingObjectBoundsMarker(
        DrawingObjectBounds drawingObject,
        double width,
        double height)
    {
        var marker = new Border
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = DrawingObjectBoundsFill,
            BorderBrush = DrawingObjectBoundsBorder,
            BorderThickness = new Thickness(1.5),
            ClipToBounds = true,
            IsHitTestVisible = false,
        };

        if (width >= 56 && height >= 24)
        {
            marker.Child = new TextBlock
            {
                Text = drawingObject.DisplayName,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = DrawingObjectBoundsForeground,
                Margin = new Thickness(6, 3),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
        }

        return marker;
    }

    private static bool TryCreateDrawingBitmap(byte[] imageBytes, out Bitmap bitmap)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes);
            bitmap = new Bitmap(stream);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException)
        {
            bitmap = null!;
            return false;
        }
    }

    private static void ApplyDrawingObjectRotation(Control visual, double rotationDegrees)
    {
        if (Math.Abs(rotationDegrees % 360) <= 0.0001)
            return;

        visual.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        visual.RenderTransform = new RotateTransform(rotationDegrees);
    }

    private static bool TryGetDisplayedDrawingObjectBounds(
        ViewportModel viewport,
        DrawingObjectBounds drawingObject,
        bool showHeadings,
        double zoomFactor,
        out double left,
        out double top,
        out double width,
        out double height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;
        if (!TryGetDisplayedColumnLeft(viewport.ColMetrics, drawingObject.AnchorCol, zoomFactor, out var columnLeft) ||
            !TryGetDisplayedRowTop(viewport.RowMetrics, drawingObject.AnchorRow, zoomFactor, out var rowTop))
        {
            return false;
        }

        left = (showHeadings ? HeaderColumnWidth * zoomFactor : 0) + columnLeft;
        top = (showHeadings ? HeaderRowHeight * zoomFactor : 0) + rowTop;
        width = Math.Max(1, drawingObject.Width * zoomFactor);
        height = Math.Max(1, drawingObject.Height * zoomFactor);
        return true;
    }

    private static bool TryGetDisplayedColumnLeft(
        IReadOnlyList<ColMetric> columns,
        uint column,
        double zoomFactor,
        out double left)
    {
        left = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var metric = columns[i];
            if (metric.Col == column)
                return true;
            left += GetDisplayedColumnWidth(metric, zoomFactor);
        }

        left = 0;
        return false;
    }

    private static bool TryGetDisplayedRowTop(
        IReadOnlyList<RowMetric> rows,
        uint row,
        double zoomFactor,
        out double top)
    {
        top = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var metric = rows[i];
            if (metric.Row == row)
                return true;
            top += GetDisplayedRowHeight(metric, zoomFactor);
        }

        top = 0;
        return false;
    }

    private static double CalculateDisplayedGridWidth(ViewportModel viewport, bool showHeadings, double zoomFactor)
    {
        var width = showHeadings ? HeaderColumnWidth * zoomFactor : 0;
        foreach (var metric in viewport.ColMetrics)
            width += GetDisplayedColumnWidth(metric, zoomFactor);

        return width;
    }

    private static double CalculateDisplayedGridHeight(ViewportModel viewport, bool showHeadings, double zoomFactor)
    {
        var height = showHeadings ? HeaderRowHeight * zoomFactor : 0;
        foreach (var metric in viewport.RowMetrics)
            height += GetDisplayedRowHeight(metric, zoomFactor);

        return height;
    }

    private static double GetDisplayedColumnWidth(ColMetric metric, double zoomFactor) =>
        Math.Max(MinimumDisplayedColumnWidth, metric.Width) * zoomFactor;

    private static double GetDisplayedRowHeight(RowMetric metric, double zoomFactor) =>
        Math.Max(MinimumDisplayedRowHeight, metric.Height) * zoomFactor;

    private bool IsSelectedColumn(uint col) =>
        _session.SelectedRanges.Any(range => range.Start.Col <= col && col <= range.End.Col);

    private bool IsSelectedRow(uint row) =>
        _session.SelectedRanges.Any(range => range.Start.Row <= row && row <= range.End.Row);

    private bool IsSelectedCell(CellAddress address) =>
        _session.SelectedRanges.Any(range => range.Contains(address));

    private Border CreateHeaderCell(string text, bool selected = false, double zoomFactor = 1) =>
        CreateCellBorder(
            text,
            selected ? SelectionHeaderBackground : HeaderBackground,
            selected ? SelectionHeaderForeground : HeaderForeground,
            TextAlignment.Center,
            AvaloniaVerticalAlignment.Center,
            TextWrapping.NoWrap,
            FontWeight.SemiBold,
            FontStyle.Normal,
            fontSize: 12,
            textDecorations: null,
            selected: false,
            zoomFactor: zoomFactor);

    private Border CreateCell(DisplayCell cell, uint row, uint col, double zoomFactor, double cellWidth, double cellHeight)
    {
        var hasCell = cell.Row != 0 && cell.Col != 0;
        var address = new CellAddress(_session.ActiveSheet.Id, row, col);
        var selected = IsSelectedCell(address);

        if (!hasCell)
            return CreateInteractiveCellBorder(
                "",
                Brushes.White,
                Brushes.Black,
                TextAlignment.Left,
                AvaloniaVerticalAlignment.Center,
                TextWrapping.NoWrap,
                FontWeight.Normal,
                FontStyle.Normal,
                fontSize: 12,
                textDecorations: null,
                selected,
                address,
                zoomFactor: zoomFactor,
                cellWidth: cellWidth,
                cellHeight: cellHeight);

        var style = cell.Style;
        var background = style?.ResolveFillColor(_session.Workbook.Theme) is { } fillColor
            ? Brush(fillColor)
            : Brushes.White;
        var foreground = style is null
            ? Brushes.Black
            : Brush(style.ResolveFontColor(_session.Workbook.Theme));
        var horizontalAlignment = style?.HorizontalAlignment ?? CellHAlign.General;
        var verticalAlignmentModel = style?.VerticalAlignment ?? CellVAlign.Bottom;
        var isNumeric = cell.RawValue is NumberValue or DateTimeValue;
        var alignment = MapCellTextAlignment(horizontalAlignment, isNumeric);
        var verticalAlignment = MapCellVerticalAlignment(verticalAlignmentModel);
        var textWrapping = style?.WrapText == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
        var weight = style?.Bold == true ? FontWeight.SemiBold : FontWeight.Normal;
        var fontStyle = style?.Italic == true ? FontStyle.Italic : FontStyle.Normal;
        var fontSize = style?.FontSize ?? CellStyle.Default.FontSize;
        var textDecorations = BuildTextDecorations(style);
        var indentPadding = GetCellIndentPadding(style);
        var textRotation = style?.TextRotation ?? CellStyle.Default.TextRotation;

        return CreateInteractiveCellBorder(
            cell.DisplayText,
            background,
            foreground,
            alignment,
            verticalAlignment,
            textWrapping,
            weight,
            fontStyle,
            fontSize,
            textDecorations,
            selected,
            address,
            indentPadding,
            textRotation,
            style,
            zoomFactor,
            cellWidth,
            cellHeight,
            horizontalAlignment,
            verticalAlignmentModel,
            isNumeric);
    }

    private Border CreateInteractiveCellBorder(
        string text,
        IBrush background,
        IBrush foreground,
        TextAlignment textAlignment,
        AvaloniaVerticalAlignment verticalAlignment,
        TextWrapping textWrapping,
        FontWeight fontWeight,
        FontStyle fontStyle,
        double fontSize,
        TextDecorationCollection? textDecorations,
        bool selected,
        CellAddress address,
        double indentPadding = 0,
        int textRotation = 0,
        CellStyle? style = null,
        double zoomFactor = 1,
        double cellWidth = 80,
        double cellHeight = 20,
        CellHAlign horizontalAlignment = CellHAlign.General,
        CellVAlign? verticalAlignmentModel = null,
        bool isNumeric = false)
    {
        var border = CreateCellBorder(
            text,
            background,
            foreground,
            textAlignment,
            verticalAlignment,
            textWrapping,
            fontWeight,
            fontStyle,
            fontSize,
            textDecorations,
            selected,
            indentPadding,
            textRotation,
            style,
            _session.ActiveSheet.ShowGridlines,
            zoomFactor,
            cellWidth,
            cellHeight,
            horizontalAlignment,
            verticalAlignmentModel,
            isNumeric);
        border.Cursor = new Cursor(StandardCursorType.Hand);
        border.PointerPressed += (_, args) =>
        {
            if (args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                SelectRange(address);
            else
                SelectCell(address);
            args.Handled = true;
        };
        border.DoubleTapped += (_, args) =>
        {
            BeginFormulaEdit(address);
            args.Handled = true;
        };
        return border;
    }

    private static Border CreateCellBorder(
        string text,
        IBrush background,
        IBrush foreground,
        TextAlignment textAlignment,
        AvaloniaVerticalAlignment verticalAlignment,
        TextWrapping textWrapping,
        FontWeight fontWeight,
        FontStyle fontStyle,
        double fontSize,
        TextDecorationCollection? textDecorations,
        bool selected,
        double indentPadding = 0,
        int textRotation = 0,
        CellStyle? style = null,
        bool showGridlines = true,
        double zoomFactor = 1,
        double cellWidth = 80,
        double cellHeight = 20,
        CellHAlign horizontalAlignment = CellHAlign.General,
        CellVAlign? verticalAlignmentModel = null,
        bool isNumeric = false)
    {
        var effectiveText = FormatTextForRotation(text, textRotation);
        var effectiveTextWrapping = textRotation == 255 ? TextWrapping.NoWrap : textWrapping;
        var scaledFontSize = Math.Max(1, fontSize * zoomFactor);
        var scaledHorizontalPadding = 8 * zoomFactor;
        var scaledIndentPadding = indentPadding * zoomFactor;
        var textBlock = new TextBlock
        {
            Text = effectiveText,
            FontSize = scaledFontSize,
            FontWeight = fontWeight,
            FontStyle = fontStyle,
            TextDecorations = textDecorations,
            Foreground = foreground,
            TextAlignment = textRotation == 255 ? TextAlignment.Center : textAlignment,
            TextWrapping = effectiveTextWrapping,
            TextTrimming = effectiveTextWrapping == TextWrapping.Wrap || textRotation == 255
                ? TextTrimming.None
                : TextTrimming.CharacterEllipsis,
            VerticalAlignment = verticalAlignment,
            Margin = new Thickness(scaledHorizontalPadding + scaledIndentPadding, 0, scaledHorizontalPadding, 0),
        };

        var content = CellTextOrientationLayoutPlanner.HasTextOrientation(textRotation)
            ? CreateOrientedCellContent(
                textBlock,
                cellWidth,
                cellHeight,
                horizontalAlignment,
                verticalAlignmentModel,
                isNumeric,
                scaledIndentPadding,
                textRotation,
                effectiveTextWrapping,
                style)
            : CreateDefaultCellContent(textBlock, style);

        return new Border
        {
            Background = background,
            BorderBrush = selected ? SelectionBorder : showGridlines ? GridLine : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = content,
        };
    }

    private static AvaloniaGrid CreateDefaultCellContent(TextBlock textBlock, CellStyle? style)
    {
        var content = new AvaloniaGrid { ClipToBounds = true };
        content.Children.Add(textBlock);
        AddStyledCellBorderOverlay(content, style);
        return content;
    }

    private static AvaloniaGrid CreateOrientedCellContent(
        TextBlock textBlock,
        double cellWidth,
        double cellHeight,
        CellHAlign horizontalAlignment,
        CellVAlign? verticalAlignment,
        bool isNumeric,
        double indentPixels,
        int textRotation,
        TextWrapping textWrapping,
        CellStyle? style)
    {
        var content = new AvaloniaGrid { ClipToBounds = true };
        var canvas = new Canvas { ClipToBounds = true };
        var measureWidth = textWrapping == TextWrapping.Wrap
            ? Math.Max(1, cellWidth - 4)
            : double.PositiveInfinity;

        textBlock.Margin = default;
        textBlock.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        textBlock.VerticalAlignment = AvaloniaVerticalAlignment.Top;
        textBlock.Measure(new Size(measureWidth, double.PositiveInfinity));
        var desired = textBlock.DesiredSize;
        textBlock.Width = Math.Max(0, desired.Width);
        textBlock.Height = Math.Max(0, desired.Height);

        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(0, 0, cellWidth, cellHeight),
            desired.Width,
            desired.Height,
            horizontalAlignment,
            verticalAlignment,
            isNumeric,
            indentPixels,
            textRotation);
        if (CreateTextRotationTransform(layout.TransformAngle) is { } transform)
        {
            textBlock.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
            textBlock.RenderTransform = transform;
        }

        Canvas.SetLeft(textBlock, layout.TextPoint.X);
        Canvas.SetTop(textBlock, layout.TextPoint.Y);
        canvas.Children.Add(textBlock);
        content.Children.Add(canvas);
        AddStyledCellBorderOverlay(content, style);
        return content;
    }

    private static void AddStyledCellBorderOverlay(AvaloniaGrid content, CellStyle? style)
    {
        if (style is not { } visibleStyle || !HasVisibleCellBorder(visibleStyle))
            return;

        AddStyledCellBorderEdge(content, visibleStyle.BorderTop, CellBorderEdge.Top);
        AddStyledCellBorderEdge(content, visibleStyle.BorderRight, CellBorderEdge.Right);
        AddStyledCellBorderEdge(content, visibleStyle.BorderBottom, CellBorderEdge.Bottom);
        AddStyledCellBorderEdge(content, visibleStyle.BorderLeft, CellBorderEdge.Left);
    }

    private static bool HasVisibleCellBorder(CellStyle? style) =>
        style is not null &&
        (style.BorderTop.Style != BorderStyle.None ||
         style.BorderRight.Style != BorderStyle.None ||
         style.BorderBottom.Style != BorderStyle.None ||
         style.BorderLeft.Style != BorderStyle.None);

    private static void AddStyledCellBorderEdge(AvaloniaGrid content, CellBorder border, CellBorderEdge edge)
    {
        if (border.Style == BorderStyle.None)
            return;

        var thickness = GetDisplayedCellBorderThickness(border.Style);
        var edgeStrip = new Border
        {
            Background = Brush(border.Color),
            IsHitTestVisible = false,
        };

        switch (edge)
        {
            case CellBorderEdge.Top:
                edgeStrip.Height = thickness;
                edgeStrip.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
                edgeStrip.VerticalAlignment = AvaloniaVerticalAlignment.Top;
                break;
            case CellBorderEdge.Right:
                edgeStrip.Width = thickness;
                edgeStrip.HorizontalAlignment = AvaloniaHorizontalAlignment.Right;
                edgeStrip.VerticalAlignment = AvaloniaVerticalAlignment.Stretch;
                break;
            case CellBorderEdge.Bottom:
                edgeStrip.Height = thickness;
                edgeStrip.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
                edgeStrip.VerticalAlignment = AvaloniaVerticalAlignment.Bottom;
                break;
            case CellBorderEdge.Left:
                edgeStrip.Width = thickness;
                edgeStrip.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
                edgeStrip.VerticalAlignment = AvaloniaVerticalAlignment.Stretch;
                break;
        }

        content.Children.Add(edgeStrip);
    }

    private static double GetDisplayedCellBorderThickness(BorderStyle style) =>
        style switch
        {
            BorderStyle.Medium => 1.5,
            BorderStyle.Thick => 2.5,
            _ => 1
        };

    private static string FormatTextForRotation(string text, int textRotation) =>
        CellTextOrientationLayoutPlanner.PrepareDisplayText(text, textRotation);

    private static int NormalizeTextRotationForDisplay(int textRotation) =>
        CellTextOrientationLayoutPlanner.NormalizeRotationForDisplay(textRotation);

    private static RotateTransform? CreateTextRotationTransform(double transformAngle) =>
        Math.Abs(transformAngle) <= 0.001 ? null : new RotateTransform(transformAngle);

    private MenuFlyout CreatePasteSpecialFlyout() =>
        new()
        {
            ItemsSource = CreatePasteSpecialMenuItems().ToArray(),
        };

    private MenuFlyout CreateAutoSumFlyout() =>
        new()
        {
            ItemsSource = new[]
            {
                _autoSumSumFlyoutItem,
                _autoSumAverageFlyoutItem,
                _autoSumCountNumbersFlyoutItem,
                _autoSumCountAllFlyoutItem,
                _autoSumMaxFlyoutItem,
                _autoSumMinFlyoutItem,
            },
        };

    private MenuFlyout CreateClearFlyout() =>
        new()
        {
            ItemsSource = new[]
            {
                _clearAllFlyoutItem,
                _clearFormatsFlyoutItem,
                _clearContentsFlyoutItem,
                _clearCommentsFlyoutItem,
                _clearHyperlinksFlyoutItem,
            },
        };

    private MenuFlyout CreateFillCellsFlyout() =>
        new()
        {
            ItemsSource = new[]
            {
                _fillDownFlyoutItem,
                _fillRightFlyoutItem,
                _fillUpFlyoutItem,
                _fillLeftFlyoutItem,
            },
        };

    private NativeMenu CreateNativeAutoSumMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(_autoSumSumMenuItem);
        menu.Items.Add(_autoSumAverageMenuItem);
        menu.Items.Add(_autoSumCountNumbersMenuItem);
        menu.Items.Add(_autoSumCountAllMenuItem);
        menu.Items.Add(_autoSumMaxMenuItem);
        menu.Items.Add(_autoSumMinMenuItem);
        return menu;
    }

    private NativeMenu CreateNativeClearMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(_clearAllMenuItem);
        menu.Items.Add(_clearFormatsMenuItem);
        menu.Items.Add(_clearContentsMenuItem);
        menu.Items.Add(_clearCommentsMenuItem);
        menu.Items.Add(_clearHyperlinksMenuItem);
        return menu;
    }

    private NativeMenu CreateNativeFillCellsMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(_fillDownMenuItem);
        menu.Items.Add(_fillRightMenuItem);
        menu.Items.Add(_fillUpMenuItem);
        menu.Items.Add(_fillLeftMenuItem);
        return menu;
    }

    private IEnumerable<MenuItem> CreatePasteSpecialMenuItems()
    {
        yield return CreatePasteSpecialMenuItem("Values", PasteCellsMode.Values, default);
        yield return CreatePasteSpecialMenuItem("Formulas", PasteCellsMode.Formulas, default);
        yield return CreatePasteSpecialMenuItem("Formats", PasteCellsMode.Formats, default);
        yield return CreatePasteCommentsMenuItem("Comments and Notes");
        yield return CreatePasteDataValidationMenuItem("Validation");
        yield return CreatePasteSpecialMenuItem("All Except Borders", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders));
        yield return CreatePasteSpecialMenuItem("All Merging Conditional Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats));
        yield return CreatePasteColumnWidthsMenuItem("Column Widths");
        yield return CreatePasteSpecialMenuItem("Formulas and Number Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats));
        yield return CreatePasteSpecialMenuItem("Values and Number Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats));
        yield return CreatePasteSpecialMenuItem("Values and Source Formatting", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting));
        yield return CreatePasteSpecialMenuItem("Keep Source Column Widths", PasteCellsMode.All, default, keepSourceColumnWidths: true);
        yield return CreatePasteLinkMenuItem("Paste Link");
        yield return CreatePasteSpecialTextMenuItem("Text");
        yield return CreatePasteSpecialTextMenuItem("Unicode Text");
        yield return CreatePastePictureMenuItem("Picture", linkedPicture: false);
        yield return CreatePastePictureMenuItem("Linked Picture", linkedPicture: true);
        yield return CreatePasteSpecialMenuItem("Transpose", PasteCellsMode.All, new PasteSpecialOptions(Transpose: true));
        yield return CreatePasteSpecialMenuItem("Skip Blanks", PasteCellsMode.All, new PasteSpecialOptions(SkipBlanks: true));
        yield return CreatePasteSpecialMenuItem("Add", PasteCellsMode.All, new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));
        yield return CreatePasteSpecialMenuItem("Subtract", PasteCellsMode.All, new PasteSpecialOptions(Operation: PasteSpecialOperation.Subtract));
        yield return CreatePasteSpecialMenuItem("Multiply", PasteCellsMode.All, new PasteSpecialOptions(Operation: PasteSpecialOperation.Multiply));
        yield return CreatePasteSpecialMenuItem("Divide", PasteCellsMode.All, new PasteSpecialOptions(Operation: PasteSpecialOperation.Divide));
    }

    private MenuItem CreatePasteSpecialMenuItem(
        string header,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths = false)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += async (_, _) =>
            await PasteSpecialClipboardTextAsync(mode, options, header, keepSourceColumnWidths);
        return menuItem;
    }

    private MenuItem CreatePasteColumnWidthsMenuItem(string header)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteColumnWidthsFromClipboardAsync(header);
        return menuItem;
    }

    private MenuItem CreatePasteCommentsMenuItem(string header)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteCommentsFromClipboardAsync(header);
        return menuItem;
    }

    private MenuItem CreatePasteDataValidationMenuItem(string header)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteDataValidationFromClipboardAsync(header);
        return menuItem;
    }

    private MenuItem CreatePasteLinkMenuItem(string header)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteLinkFromClipboardAsync(header);
        return menuItem;
    }

    private MenuItem CreatePasteSpecialTextMenuItem(string header)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteSpecialExternalTextFromClipboardAsync(header);
        return menuItem;
    }

    private MenuItem CreatePastePictureMenuItem(string header, bool linkedPicture)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += async (_, _) => await PastePictureFromClipboardAsync(header, linkedPicture);
        return menuItem;
    }

    private NativeMenu CreateNativePasteSpecialMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Values", PasteCellsMode.Values, default));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Formulas", PasteCellsMode.Formulas, default));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Formats", PasteCellsMode.Formats, default));
        menu.Items.Add(CreateNativePasteCommentsMenuItem("Comments and Notes"));
        menu.Items.Add(CreateNativePasteDataValidationMenuItem("Validation"));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("All Except Borders", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders)));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("All Merging Conditional Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats)));
        menu.Items.Add(CreateNativePasteColumnWidthsMenuItem("Column Widths"));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Formulas and Number Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats)));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Values and Number Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats)));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Values and Source Formatting", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting)));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Keep Source Column Widths", PasteCellsMode.All, default, keepSourceColumnWidths: true));
        menu.Items.Add(CreateNativePasteLinkMenuItem("Paste Link"));
        menu.Items.Add(CreateNativePasteSpecialTextMenuItem("Text"));
        menu.Items.Add(CreateNativePasteSpecialTextMenuItem("Unicode Text"));
        menu.Items.Add(CreateNativePastePictureMenuItem("Picture", linkedPicture: false));
        menu.Items.Add(CreateNativePastePictureMenuItem("Linked Picture", linkedPicture: true));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Transpose", PasteCellsMode.All, new PasteSpecialOptions(Transpose: true)));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Skip Blanks", PasteCellsMode.All, new PasteSpecialOptions(SkipBlanks: true)));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Add", PasteCellsMode.All, new PasteSpecialOptions(Operation: PasteSpecialOperation.Add)));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Subtract", PasteCellsMode.All, new PasteSpecialOptions(Operation: PasteSpecialOperation.Subtract)));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Multiply", PasteCellsMode.All, new PasteSpecialOptions(Operation: PasteSpecialOperation.Multiply)));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Divide", PasteCellsMode.All, new PasteSpecialOptions(Operation: PasteSpecialOperation.Divide)));
        return menu;
    }

    private NativeMenuItem CreateNativePasteSpecialMenuItem(
        string header,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths = false)
    {
        var menuItem = new NativeMenuItem { Header = header };
        menuItem.Click += async (_, _) =>
            await PasteSpecialClipboardTextAsync(mode, options, header, keepSourceColumnWidths);
        return menuItem;
    }

    private NativeMenuItem CreateNativePasteColumnWidthsMenuItem(string header)
    {
        var menuItem = new NativeMenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteColumnWidthsFromClipboardAsync(header);
        return menuItem;
    }

    private NativeMenuItem CreateNativePasteCommentsMenuItem(string header)
    {
        var menuItem = new NativeMenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteCommentsFromClipboardAsync(header);
        return menuItem;
    }

    private NativeMenuItem CreateNativePasteDataValidationMenuItem(string header)
    {
        var menuItem = new NativeMenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteDataValidationFromClipboardAsync(header);
        return menuItem;
    }

    private NativeMenuItem CreateNativePasteLinkMenuItem(string header)
    {
        var menuItem = new NativeMenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteLinkFromClipboardAsync(header);
        return menuItem;
    }

    private NativeMenuItem CreateNativePasteSpecialTextMenuItem(string header)
    {
        var menuItem = new NativeMenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteSpecialExternalTextFromClipboardAsync(header);
        return menuItem;
    }

    private NativeMenuItem CreateNativePastePictureMenuItem(string header, bool linkedPicture)
    {
        var menuItem = new NativeMenuItem { Header = header };
        menuItem.Click += async (_, _) => await PastePictureFromClipboardAsync(header, linkedPicture);
        return menuItem;
    }

    private MenuFlyout CreateColorPaletteFlyout(ColorPaletteTarget target, bool includeClearFill)
    {
        var items = new List<MenuItem>();
        if (includeClearFill)
        {
            var clearFillItem = new MenuItem { Header = "No Fill" };
            clearFillItem.Click += (_, _) => ClearSelectedRangeFill();
            items.Add(clearFillItem);
        }

        items.AddRange(CellColorPalettePlanner.BuildDefaultSwatches().Select(swatch => CreateColorSwatchMenuItem(swatch, target)));
        return new MenuFlyout { ItemsSource = items };
    }

    private MenuItem CreateColorSwatchMenuItem(CellColorSwatch swatch, ColorPaletteTarget target)
    {
        var menuItem = new MenuItem
        {
            Header = swatch.Hex,
            Icon = CreateColorSwatchIcon(swatch.Color),
        };
        menuItem.Click += (_, _) => ApplySelectedRangePaletteColor(swatch.Color, target);
        return menuItem;
    }

    private NativeMenu CreateNativeColorPaletteMenu(ColorPaletteTarget target, bool includeClearFill)
    {
        var menu = new NativeMenu();
        if (includeClearFill)
        {
            var clearFillItem = new NativeMenuItem { Header = "No Fill" };
            clearFillItem.Click += (_, _) => ClearSelectedRangeFill();
            menu.Items.Add(clearFillItem);
        }

        foreach (var swatch in CellColorPalettePlanner.BuildDefaultSwatches())
            menu.Items.Add(CreateNativeColorSwatchMenuItem(swatch, target));

        return menu;
    }

    private NativeMenuItem CreateNativeColorSwatchMenuItem(CellColorSwatch swatch, ColorPaletteTarget target)
    {
        var menuItem = new NativeMenuItem { Header = swatch.Hex };
        menuItem.Click += (_, _) => ApplySelectedRangePaletteColor(swatch.Color, target);
        return menuItem;
    }

    private NativeMenu CreateNativeSheetTabColorMenu()
    {
        var menu = new NativeMenu();
        var clearColorItem = new NativeMenuItem { Header = "No Color" };
        clearColorItem.Click += (_, _) => ApplyActiveSheetTabColor(null);
        menu.Items.Add(clearColorItem);

        foreach (var swatch in CellColorPalettePlanner.BuildDefaultSwatches())
            menu.Items.Add(CreateNativeSheetTabColorSwatchMenuItem(swatch));

        return menu;
    }

    private NativeMenuItem CreateNativeSheetTabColorSwatchMenuItem(CellColorSwatch swatch)
    {
        var menuItem = new NativeMenuItem { Header = swatch.Hex };
        menuItem.Click += (_, _) => ApplyActiveSheetTabColor(swatch.Color);
        return menuItem;
    }

    private void ApplySelectedRangePaletteColor(CellColor color, ColorPaletteTarget target)
    {
        switch (target)
        {
            case ColorPaletteTarget.Fill:
                ApplySelectedRangeFillColor(color);
                break;
            case ColorPaletteTarget.Font:
                ApplySelectedRangeFontColor(color);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    private void ApplyActiveSheetTabColor(CellColor? color)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var sheetName = _session.ActiveSheet.Name;
        var result = _session.SetActiveSheetTabColor(color);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Tab Color failed.");
            return;
        }

        RefreshShell(color is null
            ? $"Cleared tab color for {sheetName}"
            : $"Colored tab {sheetName}");
    }

    private static Border CreateColorSwatchIcon(CellColor color) =>
        new()
        {
            Width = 16,
            Height = 16,
            Background = Brush(color),
            BorderBrush = GridLine,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0),
            IsHitTestVisible = false,
        };

    private MenuFlyout CreateCellStylesFlyout() =>
        new()
        {
            ItemsSource = Enum
                .GetValues<CellStylePreset>()
                .Select(CreateCellStyleMenuItem)
                .ToArray(),
        };

    private MenuItem CreateCellStyleMenuItem(CellStylePreset preset)
    {
        var menuItem = new MenuItem { Header = CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset) };
        menuItem.Click += (_, _) => ApplySelectedRangeCellStylePreset(preset);
        return menuItem;
    }

    private MenuFlyout CreateBorderPresetFlyout() =>
        new()
        {
            ItemsSource = Enum
                .GetValues<CellBorderPreset>()
                .Select(CreateBorderPresetMenuItem)
                .ToArray(),
        };

    private MenuItem CreateBorderPresetMenuItem(CellBorderPreset preset)
    {
        var displayName = CellBorderPresetPlanner.GetDisplayName(preset);
        var menuItem = new MenuItem { Header = displayName };
        AutomationProperties.SetAutomationId(menuItem, $"HomeBorders{preset}MenuItem");
        AutomationProperties.SetName(menuItem, displayName);
        menuItem.Click += (_, _) => ApplySelectedRangeBorderPreset(preset);
        return menuItem;
    }

    private NativeMenu CreateNativeBorderPresetMenu()
    {
        var menu = new NativeMenu();
        foreach (var preset in Enum.GetValues<CellBorderPreset>())
            menu.Items.Add(CreateNativeBorderPresetMenuItem(preset));

        return menu;
    }

    private NativeMenuItem CreateNativeBorderPresetMenuItem(CellBorderPreset preset)
    {
        var menuItem = new NativeMenuItem
        {
            Header = CellBorderPresetPlanner.GetDisplayName(preset),
        };
        menuItem.Click += (_, _) => ApplySelectedRangeBorderPreset(preset);
        return menuItem;
    }

    private NativeMenu CreateNativeCellStylesMenu()
    {
        var menu = new NativeMenu();
        foreach (var preset in Enum.GetValues<CellStylePreset>())
            menu.Items.Add(CreateNativeCellStyleMenuItem(preset));

        return menu;
    }

    private NativeMenuItem CreateNativeCellStyleMenuItem(CellStylePreset preset)
    {
        var menuItem = new NativeMenuItem
        {
            Header = CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset),
        };
        menuItem.Click += (_, _) => ApplySelectedRangeCellStylePreset(preset);
        return menuItem;
    }

    private MenuFlyout CreateTextRotationFlyout() =>
        new()
        {
            ItemsSource = new[]
            {
                CreateTextRotationMenuItem("Horizontal", 0, "Set horizontal text for", "Horizontal Text failed."),
                CreateTextRotationMenuItem("Angle Counterclockwise", 45, "Angled text counterclockwise for", "Angle Counterclockwise failed."),
                CreateTextRotationMenuItem("Angle Clockwise", -45, "Angled text clockwise for", "Angle Clockwise failed."),
                CreateTextRotationMenuItem("Vertical Text", 255, "Set vertical text for", "Vertical Text failed."),
                CreateTextRotationMenuItem("Rotate Text Up", 90, "Rotated text up for", "Rotate Text Up failed."),
                CreateTextRotationMenuItem("Rotate Text Down", -90, "Rotated text down for", "Rotate Text Down failed."),
            },
        };

    private MenuItem CreateTextRotationMenuItem(
        string header,
        int textRotation,
        string successAction,
        string failureMessage)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += (_, _) => ApplySelectedRangeTextRotation(textRotation, successAction, failureMessage);
        return menuItem;
    }

    private static double GetCellIndentPadding(CellStyle? style) =>
        style is null ? 0 : Math.Clamp(style.IndentLevel, 0, 15) * CellIndentLevelWidth;

    private static TextDecorationCollection? BuildTextDecorations(CellStyle? style)
    {
        if (style is null || (!style.Underline && !style.DoubleUnderline && !style.Strikethrough))
            return null;

        var decorations = new TextDecorationCollection();
        if (style.Underline || style.DoubleUnderline)
        {
            foreach (var decoration in TextDecorations.Underline)
                decorations.Add(decoration);

            if (style.DoubleUnderline)
            {
                decorations.Add(new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    StrokeThickness = 1,
                    StrokeThicknessUnit = TextDecorationUnit.Pixel,
                    StrokeOffset = DoubleUnderlineSecondStrokeOffset,
                    StrokeOffsetUnit = TextDecorationUnit.Pixel,
                });
            }
        }

        if (style.Strikethrough)
        {
            foreach (var decoration in TextDecorations.Strikethrough)
                decorations.Add(decoration);
        }

        return decorations;
    }

    private static TextAlignment MapCellTextAlignment(CellHAlign horizontalAlignment, bool isNumericOrDate) =>
        horizontalAlignment switch
        {
            CellHAlign.Left => TextAlignment.Left,
            CellHAlign.Center or CellHAlign.Justify or CellHAlign.Distributed => TextAlignment.Center,
            CellHAlign.Right => TextAlignment.Right,
            CellHAlign.General when isNumericOrDate => TextAlignment.Right,
            _ => TextAlignment.Left
        };

    private static AvaloniaVerticalAlignment MapCellVerticalAlignment(CellVAlign verticalAlignment) =>
        verticalAlignment switch
        {
            CellVAlign.Top => AvaloniaVerticalAlignment.Top,
            CellVAlign.Bottom => AvaloniaVerticalAlignment.Bottom,
            _ => AvaloniaVerticalAlignment.Center
        };

    private void SelectCell(CellAddress address)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        _session.SelectCell(address);
        ApplyFormatPainterAfterTargetSelection();
    }

    private void SelectRange(CellAddress address)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        _session.SelectRange(new GridRange(_session.ActiveCell, address));
        ApplyFormatPainterAfterTargetSelection();
    }

    private void SelectSheet(SheetId sheetId)
        => SelectSheet(sheetId, selectRange: false, toggle: false);

    private bool SelectSheetForContextCommand(SheetId sheetId)
    {
        if (!TryCommitPendingFormulaEdit())
            return false;

        if (_session.SelectSheet(sheetId))
        {
            ClearSelectedDrawingObject();
            RefreshShell($"Selected {_session.ActiveSheet.Name}");
        }

        return true;
    }

    private void SelectSheetFromPointer(SheetId sheetId, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var modifiers = args.KeyModifiers;
        var selectRange = modifiers.HasFlag(KeyModifiers.Shift);
        var toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        if (!selectRange && !toggle)
            return;

        args.Handled = true;
        SelectSheet(sheetId, selectRange, toggle);
    }

    private async Task RenameSheetFromTabAsync(SheetId sheetId, TappedEventArgs args)
    {
        args.Handled = true;
        if (!SelectSheetForContextCommand(sheetId))
            return;

        await RenameActiveSheetAsync();
    }

    private void HandleSheetTabKeyDown(SheetId sheetId, Button button, KeyEventArgs args)
    {
        OpenSheetTabContextMenuFromKeyboard(sheetId, button, args);
        if (args.Handled)
            return;

        NavigateSheetTabFromKeyboard(sheetId, args);
    }

    private void OpenSheetTabContextMenuFromKeyboard(SheetId sheetId, Button button, KeyEventArgs args)
    {
        if (!IsSheetTabContextMenuKey(args))
            return;

        args.Handled = true;
        if (!SelectSheetForContextCommand(sheetId))
            return;

        if (FindSheetTabButton(sheetId) is { } refreshedButton)
            button = refreshedButton;

        if (button.ContextMenu is { } contextMenu)
        {
            contextMenu.Opened -= SheetTabContextMenu_Opened;
            contextMenu.Opened += SheetTabContextMenu_Opened;
            contextMenu.Open(button);
        }
    }

    private static bool IsSheetTabContextMenuKey(KeyEventArgs args) =>
        args.Key == Key.Apps ||
        args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.Shift;

    private void NavigateSheetTabFromKeyboard(SheetId sheetId, KeyEventArgs args)
    {
        if (args.KeyModifiers != KeyModifiers.None)
            return;

        var targetSheetId = args.Key switch
        {
            Key.Left => GetAdjacentSheetTabId(sheetId, direction: -1),
            Key.Right => GetAdjacentSheetTabId(sheetId, direction: 1),
            Key.Home => GetEdgeSheetTabId(first: true),
            Key.End => GetEdgeSheetTabId(first: false),
            _ => null
        };
        if (targetSheetId is null)
            return;

        args.Handled = true;
        SelectSheetTabFromKeyboard(targetSheetId.Value, selectRange: false);
    }

    private bool SelectAdjacentVisibleSheetFromKeyboard(int direction, bool selectRange)
    {
        var targetSheetId = GetAdjacentSheetTabId(_session.ActiveSheet.Id, direction);
        if (targetSheetId is null)
            return false;

        SelectSheetTabFromKeyboard(targetSheetId.Value, selectRange);
        return true;
    }

    private void SelectSheetTabFromKeyboard(SheetId sheetId, bool selectRange)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var changed = _session.SelectSheetFromTab(sheetId, selectRange, toggle: false);
        if (changed)
        {
            ClearSelectedDrawingObject();
            RefreshShell($"Selected {_session.ActiveSheet.Name}");
        }

        FocusActiveSheetTab();
    }

    private SheetId? GetAdjacentSheetTabId(SheetId sheetId, int direction)
    {
        if (_session.SheetTabs.Count == 0)
            return null;

        var index = FindSheetTabIndex(sheetId);
        if (index < 0)
            index = direction switch
            {
                < 0 => _session.SheetTabs.Count,
                0 => 0,
                _ => -1
            };

        var targetIndex = index + Math.Sign(direction);
        targetIndex = Math.Clamp(targetIndex, 0, _session.SheetTabs.Count - 1);
        return _session.SheetTabs[targetIndex].Id;
    }

    private SheetId? GetEdgeSheetTabId(bool first)
    {
        if (_session.SheetTabs.Count == 0)
            return null;

        return _session.SheetTabs[first ? 0 : _session.SheetTabs.Count - 1].Id;
    }

    private bool FocusActiveSheetTab()
        => FocusSheetTab(_session.ActiveSheet.Id);

    private bool FocusSheetTab(SheetId sheetId)
    {
        if (FindSheetTabButton(sheetId) is not { } button)
            return false;

        button.Focus();
        return button.IsFocused;
    }

    private static void SheetTabContextMenu_Opened(object? sender, RoutedEventArgs args)
    {
        if (sender is not ContextMenu contextMenu ||
            contextMenu.ItemsSource is not IEnumerable<Control> items)
        {
            return;
        }

        items
            .OfType<MenuItem>()
            .FirstOrDefault(item => item.IsEnabled)?
            .Focus();
    }

    private Button? FindSheetTabButton(SheetId sheetId) =>
        _sheetTabsHost.Content is StackPanel panel
            ? panel.Children
                .OfType<Button>()
                .FirstOrDefault(button => button.Tag is SheetId tag && tag == sheetId)
            : null;

    private void SelectSheet(SheetId sheetId, bool selectRange, bool toggle)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        if (!_session.SelectSheetFromTab(sheetId, selectRange, toggle))
            return;

        ClearSelectedDrawingObject();
        RefreshShell($"Selected {_session.ActiveSheet.Name}");
    }

    private void SelectAllVisibleSheets()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var changed = _session.SelectAllVisibleSheets();
        if (!changed)
            return;

        ClearSelectedDrawingObject();
        RefreshShell("Selected all visible sheets");
    }

    private void UngroupSheets()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var changed = _session.UngroupSheets();
        if (!changed)
            return;

        ClearSelectedDrawingObject();
        RefreshShell($"Ungrouped sheets to {_session.ActiveSheet.Name}");
    }

    private void AddNewSheet()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var result = _session.AddSheet();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "New Sheet failed.");
            return;
        }

        RefreshShell($"Inserted {_session.ActiveSheet.Name}");
    }

    private void CreateNewWorkbook()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (_session.IsDirty)
        {
            ShowOpenIssue("Save changes before creating a new workbook.");
            return;
        }

        var (viewportHeight, viewportWidth) = GetCurrentSheetViewportSize();
        _session = _sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true);
        RefreshViewportSizeForZoom();
        ClearSelectedDrawingObject();
        RefreshShell(_session.StartupStatus);
    }

    private async Task CloseWorkbookAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (!await ConfirmDirtyWorkbookCloseAsync("Close Workbook", "Discard and Close"))
            return;

        ResetToNewWorkbook("Closed workbook.");
    }

    private void ResetToNewWorkbook(string status)
    {
        var (viewportHeight, viewportWidth) = GetCurrentSheetViewportSize();
        _session = _sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true);
        RefreshViewportSizeForZoom();
        ClearSelectedDrawingObject();
        RefreshShell(status);
    }

    private async Task RenameActiveSheetAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var currentName = _session.ActiveSheet.Name;
        var newName = await ShowRenameSheetDialogAsync(currentName);
        if (newName is null)
            return;

        ClearSelectedDrawingObject();
        var result = _session.RenameActiveSheet(newName);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Rename Sheet failed.");
            return;
        }

        RefreshShell(string.Equals(currentName, _session.ActiveSheet.Name, StringComparison.Ordinal)
            ? $"Selected {currentName}"
            : $"Renamed {currentName} to {_session.ActiveSheet.Name}");
    }

    private void DuplicateActiveSheet()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var sourceName = _session.ActiveSheet.Name;
        ClearSelectedDrawingObject();
        var result = _session.DuplicateActiveSheet();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Duplicate Sheet failed.");
            return;
        }

        RefreshShell($"Duplicated {sourceName}");
    }

    private void MoveActiveSheetLeft()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var sheetName = _session.ActiveSheet.Name;
        ClearSelectedDrawingObject();
        var result = _session.MoveActiveSheetLeft();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Move Sheet Left failed.");
            return;
        }

        RefreshShell($"Moved {sheetName} left");
    }

    private void MoveActiveSheetRight()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var sheetName = _session.ActiveSheet.Name;
        ClearSelectedDrawingObject();
        var result = _session.MoveActiveSheetRight();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Move Sheet Right failed.");
            return;
        }

        RefreshShell($"Moved {sheetName} right");
    }

    private void ToggleShowGridlines()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var showGridlines = !_session.IsShowingGridlines;
        var result = _session.SetShowGridlines(showGridlines);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Gridlines failed.");
            return;
        }

        RefreshShell(showGridlines ? "Showing gridlines" : "Hiding gridlines");
    }

    private void ToggleShowHeadings()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var showHeadings = !_session.IsShowingHeadings;
        var result = _session.SetShowHeadings(showHeadings);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Headings failed.");
            return;
        }

        RefreshViewportSizeForZoom();
        RefreshShell(showHeadings ? "Showing headings" : "Hiding headings");
    }

    private void ZoomIn() =>
        ApplyZoomPercent(_session.ZoomPercent + ZoomStepPercent, "Zoom In failed.");

    private void ZoomOut() =>
        ApplyZoomPercent(_session.ZoomPercent - ZoomStepPercent, "Zoom Out failed.");

    private void ZoomTo100Percent() =>
        ApplyZoomPercent(100, "100% Zoom failed.");

    private void ZoomToSelection()
    {
        var zoomPercent = CalculateZoomToSelectionPercent();
        ApplyZoomPercent(zoomPercent, "Zoom to Selection failed.");
    }

    private void ApplyZoomPercent(int zoomPercent, string errorMessage)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        zoomPercent = ClampZoomPercent(zoomPercent);
        var result = _session.SetZoomPercent(zoomPercent);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? errorMessage);
            return;
        }

        RefreshViewportSizeForZoom();
        RefreshShell($"Zoom {FormatZoomPercent(_session.ZoomPercent)}");
    }

    private int CalculateZoomToSelectionPercent()
    {
        if (!TryGetSheetViewportDisplaySize(out var viewportHeight, out var viewportWidth))
            return 100;

        var range = _session.SelectedRange;
        var widthFit = CalculateZoomAxisFitPercent(
            viewportWidth,
            range.ColCount,
            ZoomToSelectionDefaultColumnWidth);
        var heightFit = CalculateZoomAxisFitPercent(
            viewportHeight,
            range.RowCount,
            ZoomToSelectionDefaultRowHeight);
        return ClampZoomPercent((int)Math.Round(Math.Min(widthFit, heightFit)));
    }

    private static double CalculateZoomAxisFitPercent(double viewportPixels, uint selectedCount, double defaultCellPixels)
    {
        var selectionPixels = Math.Max(1, selectedCount * defaultCellPixels);
        return viewportPixels / selectionPixels * 100;
    }

    private void ToggleShowFormulas()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var showFormulas = !_session.IsShowingFormulas;
        var result = _session.SetShowFormulas(showFormulas);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Show Formulas failed.");
            return;
        }

        RefreshShell(showFormulas ? "Showing formulas" : "Showing values");
    }

    private void HideActiveSheet()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var sheetName = _session.ActiveSheet.Name;
        ClearSelectedDrawingObject();
        var result = _session.HideActiveSheet();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Hide Sheet failed.");
            return;
        }

        RefreshShell($"Hid {sheetName}");
    }

    private async Task UnhideSheetAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var hiddenSheets = _session.HiddenSheets;
        if (hiddenSheets.Count == 0)
        {
            ShowEditIssue("No hidden sheets.");
            return;
        }

        var sheet = await ShowUnhideSheetDialogAsync(hiddenSheets);
        if (sheet is null)
            return;

        ClearSelectedDrawingObject();
        var result = _session.UnhideSheet(sheet.Id);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Unhide Sheet failed.");
            return;
        }

        RefreshShell($"Unhid {sheet.Name}");
    }

    private void DeleteActiveSheet()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var sheetName = _session.ActiveSheet.Name;
        ClearSelectedDrawingObject();
        var result = _session.DeleteActiveSheet();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Delete Sheet failed.");
            return;
        }

        RefreshShell($"Deleted {sheetName}");
    }

    private async Task<WorkbookHiddenSheet?> ShowUnhideSheetDialogAsync(IReadOnlyList<WorkbookHiddenSheet> hiddenSheets)
    {
        WorkbookHiddenSheet? result = null;
        var dialog = new Window
        {
            Title = "Unhide Sheet",
            Width = 380,
            Height = 190,
            MinWidth = 340,
            MinHeight = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var sheetBox = new ComboBox
        {
            ItemsSource = hiddenSheets,
            MinWidth = 280,
            SelectedIndex = hiddenSheets.Count > 0 ? 0 : -1,
        };
        AutomationProperties.SetName(sheetBox, "Hidden sheet");
        AutomationProperties.SetAutomationId(sheetBox, "UnhideSheetList");
        AutomationProperties.SetHelpText(sheetBox, "Select the hidden worksheet to make visible.");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(okButton, "UnhideSheetOkButton");
        AutomationProperties.SetAutomationId(cancelButton, "UnhideSheetCancelButton");

        void Accept()
        {
            if (sheetBox.SelectedItem is not WorkbookHiddenSheet selected)
                return;

            result = selected;
            dialog.Close();
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        sheetBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                okButton,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Hidden sheet" },
                sheetBox,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) => sheetBox.Focus();

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<string?> ShowRenameSheetDialogAsync(string currentName)
    {
        string? result = null;
        var dialog = new Window
        {
            Title = "Rename Sheet",
            Width = 380,
            Height = 190,
            MinWidth = 340,
            MinHeight = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var nameBox = new TextBox
        {
            Text = currentName,
            MinWidth = 280,
        };
        AutomationProperties.SetName(nameBox, "Sheet name");
        AutomationProperties.SetAutomationId(nameBox, "RenameSheetNameBox");
        AutomationProperties.SetHelpText(nameBox, "Enter a worksheet name up to 31 characters.");

        var validationText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };

        void Accept()
        {
            var proposedName = (nameBox.Text ?? "").Trim();
            var validationError = _session.Workbook.ValidateSheetName(proposedName, _session.ActiveSheet.Id);
            if (validationError is not null)
            {
                validationText.Text = validationError;
                validationText.IsVisible = true;
                nameBox.Focus();
                nameBox.SelectAll();
                return;
            }

            result = proposedName;
            dialog.Close();
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                okButton,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Sheet name" },
                nameBox,
                validationText,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task ShowFindDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var search = await ShowFindInputDialogAsync();
        if (search is null)
            return;

        if (search.Action == FindDialogAction.FindNext)
        {
            FindNext(search.FindText, search.Options, search.MatchCase, search.MatchEntireCell);
            return;
        }

        var result = _session.FindAll(search.FindText, search.Options, search.MatchCase, search.MatchEntireCell);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Find All failed.");
            return;
        }

        RefreshShell(result.MatchCount == 0
            ? "No matches found."
            : $"Found {result.MatchCount} cells");
        await ShowFindAllResultsDialogAsync(search.FindText, result.Matches);
    }

    private async Task<FindDialogResult?> ShowFindInputDialogAsync(Action<FindDialogSmokeProbe>? launchSmokeProbe = null)
    {
        FindDialogResult? result = null;
        var dialog = new Window
        {
            Title = "Find",
            Width = 420,
            Height = 430,
            MinWidth = 360,
            MinHeight = 390,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var findBox = new TextBox
        {
            Text = _session.LastFindText,
            MinWidth = 300,
        };
        AutomationProperties.SetName(findBox, "Find what");
        AutomationProperties.SetAutomationId(findBox, "FindTextBox");

        var findNextButton = new Button
        {
            Content = "Find Next",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(findNextButton, "FindNextButton");

        var findAllButton = new Button
        {
            Content = "Find All",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(findAllButton, "FindAllButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(cancelButton, "FindCancelButton");

        var optionsControls = CreateFindOptionsControls("Find", defaultLookInIndex: 0);
        StyleDiff? findFormat = null;
        var chooseFormatButton = CreateFindReplaceFormatButton("FindChooseFormatFromCellButton", "Choose From Cell");
        var clearFormatButton = CreateFindReplaceFormatButton("FindClearFormatButton", "Clear Format");
        UpdateFindReplaceFormatState(findFormat, chooseFormatButton, clearFormatButton);
        chooseFormatButton.Click += (_, _) =>
        {
            findFormat = _session.CreateFormatDiffFromActiveCell();
            UpdateFindReplaceFormatState(findFormat, chooseFormatButton, clearFormatButton);
        };
        clearFormatButton.Click += (_, _) =>
        {
            findFormat = null;
            UpdateFindReplaceFormatState(findFormat, chooseFormatButton, clearFormatButton);
        };
        var findFormatRow = CreateFindReplaceFormatRow("Find format", chooseFormatButton, clearFormatButton);

        void Accept(FindDialogAction action)
        {
            result = new FindDialogResult(
                findBox.Text ?? "",
                action,
                CreateFindOptions(optionsControls, findFormat),
                optionsControls.MatchCaseBox.IsChecked == true,
                optionsControls.MatchEntireCellBox.IsChecked == true);
            dialog.Close();
        }

        findNextButton.Click += (_, _) => Accept(FindDialogAction.FindNext);
        findAllButton.Click += (_, _) => Accept(FindDialogAction.FindAll);
        cancelButton.Click += (_, _) => dialog.Close();
        findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Accept(FindDialogAction.FindNext);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                findNextButton,
                findAllButton,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Find what" },
                findBox,
                findFormatRow,
                optionsControls.Panel,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) =>
        {
            findBox.Focus();
            findBox.SelectAll();
        };
        if (launchSmokeProbe is not null)
        {
            dialog.Opened += (_, _) =>
            {
                RunLaunchSmokeDialogProbe(
                    dialog,
                    () => launchSmokeProbe(new FindDialogSmokeProbe(
                        dialog,
                        findBox,
                        findNextButton,
                        findAllButton,
                        cancelButton,
                        optionsControls,
                        chooseFormatButton,
                        clearFormatButton)));
            };
        }

        await dialog.ShowDialog(this);
        return result;
    }

    private void FindNext(
        string? searchText = null,
        FindOptions? options = null,
        bool matchCase = false,
        bool matchEntireCell = false)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = _session.FindNext(searchText, options, matchCase, matchEntireCell);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Find failed.");
            return;
        }

        RefreshShell($"Found {FormatRangeReference(result.SelectedRange!.Value)} ({result.MatchIndex} of {result.MatchCount})");
    }

    private async Task ShowFindAllResultsDialogAsync(string searchText, IReadOnlyList<WorkbookFindAllMatch> matches)
    {
        var dialog = new Window
        {
            Title = "Find All",
            Width = 720,
            Height = 420,
            MinWidth = 520,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var statusText = new TextBlock
        {
            Text = matches.Count == 0
                ? "No matches found."
                : $"{matches.Count} {(matches.Count == 1 ? "cell" : "cells")} found for \"{searchText}\"",
        };
        AutomationProperties.SetAutomationId(statusText, "FindAllResultsStatusText");

        var resultsList = new ListBox
        {
            ItemsSource = matches,
            MinHeight = 240,
        };
        AutomationProperties.SetName(resultsList, "Find all results");
        AutomationProperties.SetAutomationId(resultsList, "FindAllResultsList");
        resultsList.SelectionChanged += (_, _) =>
        {
            if (resultsList.SelectedItem is WorkbookFindAllMatch match)
                NavigateToFindAllMatch(match);
        };

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        AutomationProperties.SetAutomationId(closeButton, "FindAllCloseButton");
        closeButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                statusText,
                resultsList,
                closeButton,
            },
        };
        dialog.Opened += (_, _) =>
        {
            if (matches.Count > 0)
                resultsList.Focus();
            else
                closeButton.Focus();
        };

        await dialog.ShowDialog(this);
    }

    private void NavigateToFindAllMatch(WorkbookFindAllMatch match)
    {
        var result = _session.GoToCell(match.Address);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Find result could not be selected.");
            return;
        }

        RefreshShell($"Found {match.Sheet}!{match.Cell}");
    }

    private async Task ShowReplaceDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var replacement = await ShowReplaceInputDialogAsync();
        if (replacement is null)
            return;

        var result = replacement.Action == ReplaceDialogAction.ReplaceAll
            ? _session.ReplaceAllValues(
                replacement.FindText,
                replacement.ReplaceText,
                replacement.Options,
                replacement.MatchCase,
                replacement.MatchEntireCell,
                replacement.ReplacementFormat)
            : _session.ReplaceNextValue(
                replacement.FindText,
                replacement.ReplaceText,
                replacement.Options,
                replacement.MatchCase,
                replacement.MatchEntireCell,
                replacement.ReplacementFormat);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Replace failed.");
            return;
        }

        if (replacement.Action == ReplaceDialogAction.ReplaceAll)
        {
            RefreshShell(result.ReplacedCount == 0
                ? result.MatchCount == 0 ? "No matches found." : "No replaceable match found."
                : $"Replaced {result.ReplacedCount} cells");
            return;
        }

        RefreshShell(result.ReplacedCount == 0
            ? result.MatchCount == 0 ? "No matches found." : "No replaceable match found."
            : $"Replaced {FormatRangeReference(result.ReplacedRange!.Value)} ({result.MatchIndex} of {result.MatchCount})");
    }

    private async Task<ReplaceDialogResult?> ShowReplaceInputDialogAsync(Action<ReplaceDialogSmokeProbe>? launchSmokeProbe = null)
    {
        ReplaceDialogResult? result = null;
        var dialog = new Window
        {
            Title = "Replace",
            Width = 420,
            Height = 520,
            MinWidth = 360,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var findBox = new TextBox
        {
            Text = _session.LastFindText,
            MinWidth = 300,
        };
        AutomationProperties.SetName(findBox, "Find what");
        AutomationProperties.SetAutomationId(findBox, "ReplaceFindTextBox");

        var replaceBox = new TextBox
        {
            Text = "",
            MinWidth = 300,
        };
        AutomationProperties.SetName(replaceBox, "Replace with");
        AutomationProperties.SetAutomationId(replaceBox, "ReplaceWithTextBox");

        var replaceButton = new Button
        {
            Content = "Replace",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(replaceButton, "ReplaceButton");

        var replaceAllButton = new Button
        {
            Content = "Replace All",
            MinWidth = 96,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(replaceAllButton, "ReplaceAllButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(cancelButton, "ReplaceCancelButton");

        var optionsControls = CreateFindOptionsControls("Replace", defaultLookInIndex: 1);
        StyleDiff? findFormat = null;
        StyleDiff? replacementFormat = null;
        var chooseFindFormatButton = CreateFindReplaceFormatButton("ReplaceFindChooseFormatFromCellButton", "Choose From Cell");
        var clearFindFormatButton = CreateFindReplaceFormatButton("ReplaceFindClearFormatButton", "Clear Format");
        var chooseReplaceFormatButton = CreateFindReplaceFormatButton("ReplaceWithChooseFormatFromCellButton", "Choose From Cell");
        var clearReplaceFormatButton = CreateFindReplaceFormatButton("ReplaceWithClearFormatButton", "Clear Format");
        UpdateFindReplaceFormatState(findFormat, chooseFindFormatButton, clearFindFormatButton);
        UpdateFindReplaceFormatState(replacementFormat, chooseReplaceFormatButton, clearReplaceFormatButton);
        chooseFindFormatButton.Click += (_, _) =>
        {
            findFormat = _session.CreateFormatDiffFromActiveCell();
            UpdateFindReplaceFormatState(findFormat, chooseFindFormatButton, clearFindFormatButton);
        };
        clearFindFormatButton.Click += (_, _) =>
        {
            findFormat = null;
            UpdateFindReplaceFormatState(findFormat, chooseFindFormatButton, clearFindFormatButton);
        };
        chooseReplaceFormatButton.Click += (_, _) =>
        {
            replacementFormat = _session.CreateFormatDiffFromActiveCell();
            UpdateFindReplaceFormatState(replacementFormat, chooseReplaceFormatButton, clearReplaceFormatButton);
        };
        clearReplaceFormatButton.Click += (_, _) =>
        {
            replacementFormat = null;
            UpdateFindReplaceFormatState(replacementFormat, chooseReplaceFormatButton, clearReplaceFormatButton);
        };
        var findFormatRow = CreateFindReplaceFormatRow("Find format", chooseFindFormatButton, clearFindFormatButton);
        var replaceFormatRow = CreateFindReplaceFormatRow("Replace format", chooseReplaceFormatButton, clearReplaceFormatButton);

        void Accept(ReplaceDialogAction action)
        {
            result = new ReplaceDialogResult(
                findBox.Text ?? "",
                replaceBox.Text ?? "",
                action,
                CreateFindOptions(optionsControls, findFormat),
                optionsControls.MatchCaseBox.IsChecked == true,
                optionsControls.MatchEntireCellBox.IsChecked == true,
                replacementFormat);
            dialog.Close();
        }

        replaceButton.Click += (_, _) => Accept(ReplaceDialogAction.Replace);
        replaceAllButton.Click += (_, _) => Accept(ReplaceDialogAction.ReplaceAll);
        cancelButton.Click += (_, _) => dialog.Close();
        findBox.KeyDown += (_, e) => HandleReplaceDialogKey(e, () => Accept(ReplaceDialogAction.Replace), () => dialog.Close());
        replaceBox.KeyDown += (_, e) => HandleReplaceDialogKey(e, () => Accept(ReplaceDialogAction.Replace), () => dialog.Close());

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                replaceButton,
                replaceAllButton,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Find what" },
                findBox,
                findFormatRow,
                new TextBlock { Text = "Replace with" },
                replaceBox,
                replaceFormatRow,
                optionsControls.Panel,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) =>
        {
            findBox.Focus();
            findBox.SelectAll();
        };
        if (launchSmokeProbe is not null)
        {
            dialog.Opened += (_, _) =>
            {
                RunLaunchSmokeDialogProbe(
                    dialog,
                    () => launchSmokeProbe(new ReplaceDialogSmokeProbe(
                        dialog,
                        findBox,
                        replaceBox,
                        replaceButton,
                        replaceAllButton,
                        cancelButton,
                        optionsControls,
                        chooseFindFormatButton,
                        clearFindFormatButton,
                        chooseReplaceFormatButton,
                        clearReplaceFormatButton)));
            };
        }

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task ShowGoToDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var reference = await ShowSingleInputDialogAsync(
            "Go To",
            "Reference",
            FormatRangeReference(_session.SelectedRange),
            "Go",
            "GoToReferenceBox");
        if (reference is null)
            return;

        var result = _session.GoToReference(reference);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Go To failed.");
            return;
        }

        RefreshShell($"Selected {FormatRangeReference(result.SelectedRange!.Value)}");
    }

    private async Task ShowGoToSpecialDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var selection = await ShowGoToSpecialInputDialogAsync();
        if (selection is null)
            return;

        SelectGoToSpecial(selection.Kind, selection.Options);
    }

    private async Task<GoToSpecialDialogResult?> ShowGoToSpecialInputDialogAsync(Action<GoToSpecialDialogSmokeProbe>? launchSmokeProbe = null)
    {
        GoToSpecialDialogResult? result = null;
        var dialog = new Window
        {
            Title = "Go To Special",
            Width = 420,
            Height = 310,
            MinWidth = 360,
            MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var choices = CreateGoToSpecialChoices();
        var kindBox = new ComboBox
        {
            ItemsSource = choices,
            SelectedIndex = 0,
            MinWidth = 300,
        };
        AutomationProperties.SetName(kindBox, "Go to");
        AutomationProperties.SetAutomationId(kindBox, "GoToSpecialKindBox");

        var numbersBox = CreateGoToSpecialValueTypeBox("Numbers", "GoToSpecialNumbersBox");
        var textBox = CreateGoToSpecialValueTypeBox("Text", "GoToSpecialTextBox");
        var logicalsBox = CreateGoToSpecialValueTypeBox("Logicals", "GoToSpecialLogicalsBox");
        var errorsBox = CreateGoToSpecialValueTypeBox("Errors", "GoToSpecialErrorsBox");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(okButton, "GoToSpecialOkButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(cancelButton, "GoToSpecialCancelButton");

        void RefreshValueTypeState()
        {
            var enabled = kindBox.SelectedItem is GoToSpecialChoice choice &&
                UsesGoToSpecialValueTypeOptions(choice.Kind);
            numbersBox.IsEnabled = enabled;
            textBox.IsEnabled = enabled;
            logicalsBox.IsEnabled = enabled;
            errorsBox.IsEnabled = enabled;
        }

        GoToSpecialValueTypes GetValueTypes()
        {
            var valueTypes = GoToSpecialValueTypes.None;
            if (numbersBox.IsChecked == true)
                valueTypes |= GoToSpecialValueTypes.Numbers;
            if (textBox.IsChecked == true)
                valueTypes |= GoToSpecialValueTypes.Text;
            if (logicalsBox.IsChecked == true)
                valueTypes |= GoToSpecialValueTypes.Logicals;
            if (errorsBox.IsChecked == true)
                valueTypes |= GoToSpecialValueTypes.Errors;
            return valueTypes;
        }

        void Accept()
        {
            var choice = kindBox.SelectedItem as GoToSpecialChoice ?? choices[0];
            var options = UsesGoToSpecialValueTypeOptions(choice.Kind)
                ? new GoToSpecialOptions(GetValueTypes())
                : new GoToSpecialOptions();
            result = new GoToSpecialDialogResult(choice.Kind, options);
            dialog.Close();
        }

        kindBox.SelectionChanged += (_, _) => RefreshValueTypeState();
        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Accept();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        var valueTypeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                numbersBox,
                textBox,
                logicalsBox,
                errorsBox,
            },
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                okButton,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Go to" },
                kindBox,
                new TextBlock { Text = "Value types" },
                valueTypeRow,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) =>
        {
            RefreshValueTypeState();
            kindBox.Focus();
        };
        if (launchSmokeProbe is not null)
        {
            dialog.Opened += (_, _) =>
            {
                RunLaunchSmokeDialogProbe(
                    dialog,
                    () => launchSmokeProbe(new GoToSpecialDialogSmokeProbe(
                        dialog,
                        kindBox,
                        numbersBox,
                        textBox,
                        logicalsBox,
                        errorsBox,
                        okButton,
                        cancelButton)));
            };
        }

        await dialog.ShowDialog(this);
        return result;
    }

    private static CheckBox CreateGoToSpecialValueTypeBox(string label, string automationId)
    {
        var checkBox = new CheckBox
        {
            Content = label,
            IsChecked = true,
        };
        AutomationProperties.SetAutomationId(checkBox, automationId);
        return checkBox;
    }

    private static GoToSpecialChoice[] CreateGoToSpecialChoices() =>
    [
        new(GoToSpecialKind.Blanks, "Blanks"),
        new(GoToSpecialKind.Constants, "Constants"),
        new(GoToSpecialKind.Formulas, "Formulas"),
        new(GoToSpecialKind.Comments, "Comments"),
        new(GoToSpecialKind.CurrentRegion, "Current region"),
        new(GoToSpecialKind.RowDifferences, "Row differences"),
        new(GoToSpecialKind.ColumnDifferences, "Column differences"),
        new(GoToSpecialKind.LastCell, "Last cell"),
        new(GoToSpecialKind.ConditionalFormats, "Conditional formats"),
        new(GoToSpecialKind.Objects, "Objects"),
        new(GoToSpecialKind.Precedents, "Precedents"),
        new(GoToSpecialKind.Dependents, "Dependents"),
        new(GoToSpecialKind.DataValidation, "Data validation"),
        new(GoToSpecialKind.VisibleCellsOnly, "Visible cells only"),
    ];

    private static bool UsesGoToSpecialValueTypeOptions(GoToSpecialKind kind) =>
        kind is GoToSpecialKind.Constants or GoToSpecialKind.Formulas;

    private bool SelectGoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)
    {
        if (!TryCommitPendingFormulaEdit())
            return false;

        ClearSelectedDrawingObject();
        var result = _session.GoToSpecial(kind, options);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Go To Special failed.");
            return false;
        }

        var selectedText = result.SelectedRanges.Count == 1
            ? FormatRangeReference(result.SelectedRange!.Value)
            : $"{result.MatchCount} cells";
        RefreshShell($"Selected {selectedText}");
        return true;
    }

    private async Task<string?> ShowSingleInputDialogAsync(
        string title,
        string label,
        string initialText,
        string acceptText,
        string automationId,
        Action<SingleInputDialogSmokeProbe>? launchSmokeProbe = null)
    {
        string? result = null;
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            Height = 165,
            MinWidth = 340,
            MinHeight = 155,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var inputBox = new TextBox
        {
            Text = initialText,
            MinWidth = 280,
        };
        AutomationProperties.SetName(inputBox, label);
        AutomationProperties.SetAutomationId(inputBox, automationId);

        var acceptButton = new Button
        {
            Content = acceptText,
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(acceptButton, $"{automationId}AcceptButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(cancelButton, $"{automationId}CancelButton");

        void Accept()
        {
            result = inputBox.Text ?? "";
            dialog.Close();
        }

        acceptButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        inputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                acceptButton,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label },
                inputBox,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) =>
        {
            inputBox.Focus();
            inputBox.SelectAll();
        };
        if (launchSmokeProbe is not null)
        {
            dialog.Opened += (_, _) =>
            {
                RunLaunchSmokeDialogProbe(
                    dialog,
                    () => launchSmokeProbe(new SingleInputDialogSmokeProbe(
                        dialog,
                        inputBox,
                        acceptButton,
                        cancelButton)));
            };
        }

        await dialog.ShowDialog(this);
        return result;
    }

    private FindOptions CreateFindOptions(FindOptionsControls controls, StyleDiff? requiredFormat = null) =>
        new(
            Within: controls.WithinBox.SelectedIndex == 1 ? FindWithin.Workbook : FindWithin.Sheet,
            CurrentSheetId: _session.ActiveSheet.Id,
            SearchOrder: controls.SearchBox.SelectedIndex == 1 ? FindSearchOrder.ByColumns : FindSearchOrder.ByRows,
            LookIn: controls.LookInBox.SelectedIndex switch
            {
                0 => FindLookIn.Formulas,
                2 => FindLookIn.Notes,
                3 => FindLookIn.Comments,
                _ => FindLookIn.Values
            },
            RequiredFormat: requiredFormat);

    private static FindOptionsControls CreateFindOptionsControls(string automationPrefix, int defaultLookInIndex)
    {
        var withinBox = CreateFindOptionComboBox(
            $"{automationPrefix}WithinBox",
            "Within",
            ["Sheet", "Workbook"],
            selectedIndex: 0);
        var searchBox = CreateFindOptionComboBox(
            $"{automationPrefix}SearchBox",
            "Search",
            ["By Rows", "By Columns"],
            selectedIndex: 0);
        var lookInBox = CreateFindOptionComboBox(
            $"{automationPrefix}LookInBox",
            "Look in",
            ["Formulas", "Values", "Notes", "Comments"],
            selectedIndex: defaultLookInIndex);
        var matchCaseBox = CreateFindOptionCheckBox(
            "Match case",
            $"{automationPrefix}MatchCaseBox");
        var matchEntireCellBox = CreateFindOptionCheckBox(
            "Match entire cell contents",
            $"{automationPrefix}MatchEntireCellBox");

        var matchRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                matchCaseBox,
                matchEntireCellBox,
            },
        };

        var panel = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Within" },
                withinBox,
                new TextBlock { Text = "Search" },
                searchBox,
                new TextBlock { Text = "Look in" },
                lookInBox,
                matchRow,
            },
        };
        AutomationProperties.SetAutomationId(panel, $"{automationPrefix}OptionsPanel");

        return new FindOptionsControls(
            withinBox,
            searchBox,
            lookInBox,
            matchCaseBox,
            matchEntireCellBox,
            panel);
    }

    private static ComboBox CreateFindOptionComboBox(
        string automationId,
        string automationName,
        IReadOnlyList<string> values,
        int selectedIndex)
    {
        var comboBox = new ComboBox
        {
            ItemsSource = values,
            SelectedIndex = selectedIndex,
            MinWidth = 160,
        };
        AutomationProperties.SetName(comboBox, automationName);
        AutomationProperties.SetAutomationId(comboBox, automationId);
        return comboBox;
    }

    private static Button CreateFindReplaceFormatButton(string automationId, string content)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 112,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    private static StackPanel CreateFindReplaceFormatRow(string label, Button chooseButton, Button clearButton) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        chooseButton,
                        clearButton,
                    },
                },
            },
        };

    private static void UpdateFindReplaceFormatState(StyleDiff? format, Button chooseButton, Button clearButton)
    {
        chooseButton.Content = format is null ? "Choose From Cell" : "Format Set";
        clearButton.IsVisible = format is not null;
    }

    private static CheckBox CreateFindOptionCheckBox(string label, string automationId)
    {
        var checkBox = new CheckBox
        {
            Content = label,
        };
        AutomationProperties.SetAutomationId(checkBox, automationId);
        return checkBox;
    }

    private static void HandleReplaceDialogKey(KeyEventArgs e, Action accept, Action cancel)
    {
        if (e.Key == Key.Enter)
        {
            accept();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            cancel();
            e.Handled = true;
        }
    }

    private void BeginFormulaEdit(CellAddress address, string? initialText = null)
    {
        ClearSelectedDrawingObject();
        _session.BeginFormulaEdit(address);
        RefreshShell("Ready");
        var originalText = _formulaBox.Text ?? "";
        if (initialText is not null)
            _formulaBox.Text = initialText;

        _formulaBox.Focus();
        _formulaBoxEditOriginalText = originalText;
        MoveFormulaBoxCaretToEnd();
    }

    private void FormulaBox_GotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_session.FormulaEditAddress is not null)
            return;

        _session.BeginFormulaEdit(_session.ActiveCell);
        _formulaBoxEditOriginalText = _formulaBox.Text ?? "";
    }

    private void FormulaBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitFormulaBox();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            var colDelta = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            if (CommitFormulaBox())
            {
                _session.MoveActiveCell(0, colDelta);
                RefreshShell("Ready");
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _session.CancelFormulaEdit();
            _formulaBoxEditOriginalText = null;
            RefreshShell("Ready");
            e.Handled = true;
        }
    }

    private bool CommitFormulaBox()
    {
        var address = _session.FormulaEditAddress ?? _session.ActiveCell;
        var result = _session.CommitCellText(_formulaBox.Text ?? "");

        if (!result.Success)
        {
            _statusText.Text = result.ErrorMessage ?? "Edit failed";
            _statusText.Foreground = Brush(143, 74, 18);
            return false;
        }

        _formulaBoxEditOriginalText = null;
        RefreshShell($"Edited {FormatCellReference(address)}");
        return true;
    }

    private bool TryCommitPendingFormulaEdit()
    {
        if (_session.FormulaEditAddress is null)
            return true;

        if (!HasPendingFormulaEditText())
        {
            _session.CancelFormulaEdit();
            _formulaBoxEditOriginalText = null;
            return true;
        }

        return CommitFormulaBox();
    }

    private bool HasPendingFormulaEditText() =>
        !string.Equals(
            _formulaBox.Text ?? "",
            _formulaBoxEditOriginalText ?? "",
            StringComparison.Ordinal);

    private void MoveFormulaBoxCaretToEnd()
    {
        _formulaBox.CaretIndex = _formulaBox.Text?.Length ?? 0;
        _formulaBox.SelectionStart = _formulaBox.CaretIndex;
        _formulaBox.SelectionEnd = _formulaBox.CaretIndex;
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        await SaveCurrentWorkbookAsync();
    }

    private async void SaveAsButton_Click(object? sender, RoutedEventArgs e)
    {
        await SaveWorkbookAsAsync();
    }

    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        await OpenWorkbookAsync();
    }

    private void UndoButton_Click(object? sender, RoutedEventArgs e)
    {
        UndoLastEdit();
    }

    private void RedoButton_Click(object? sender, RoutedEventArgs e)
    {
        RedoLastEdit();
    }

    private async void CutButton_Click(object? sender, RoutedEventArgs e)
    {
        await CutSelectedRangeToClipboardAsync();
    }

    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        await CopySelectedRangeToClipboardAsync();
    }

    private async void PasteButton_Click(object? sender, RoutedEventArgs e)
    {
        await PasteClipboardTextAsync();
    }

    private void FormatPainterButton_Click(object? sender, RoutedEventArgs e)
    {
        CaptureFormatPainterSource(persistent: false);
    }

    private void AutoSumButton_Click(object? sender, RoutedEventArgs e)
    {
        InsertAutoSumFormula("SUM");
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        ClearSelectedRangeContents();
    }

    private void InsertAutoSumFormula(string functionName)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var targetReference = FormatCellReference(_session.SelectedRange.Start);
        var result = _session.InsertAutoSumFormula(functionName);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "AutoSum failed.");
            return;
        }

        RefreshShell($"Inserted {functionName.ToUpperInvariant()} at {targetReference}");
    }

    private void FillSelectedRange(FillCellsDirection direction)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.FillSelectedRange(direction);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? $"{FormatFillCellsAction(direction)} failed.");
            return;
        }

        RefreshShell($"{FormatFillCellsAction(direction)} in {rangeReference}");
    }

    private void BoldButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeBold(_boldButton.IsChecked == true);
    }

    private void ItalicButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeItalic(_italicButton.IsChecked == true);
    }

    private void UnderlineButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeUnderline(_underlineButton.IsChecked == true);
    }

    private void DoubleUnderlineButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeDoubleUnderline(_doubleUnderlineButton.IsChecked == true);
    }

    private void StrikethroughButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeStrikethrough(_strikethroughButton.IsChecked == true);
    }

    private void IncreaseFontSizeButton_Click(object? sender, RoutedEventArgs e)
    {
        IncreaseSelectedRangeFontSize();
    }

    private void DecreaseFontSizeButton_Click(object? sender, RoutedEventArgs e)
    {
        DecreaseSelectedRangeFontSize();
    }

    private void CurrencyFormatButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeCurrencyFormat();
    }

    private void PercentFormatButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangePercentFormat();
    }

    private void CommaStyleButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeCommaStyle();
    }

    private void IncreaseDecimalButton_Click(object? sender, RoutedEventArgs e)
    {
        IncreaseSelectedRangeDecimalPlaces();
    }

    private void DecreaseDecimalButton_Click(object? sender, RoutedEventArgs e)
    {
        DecreaseSelectedRangeDecimalPlaces();
    }

    private void AlignTopButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeVerticalAlignment(CellVAlign.Top);
    }

    private void AlignMiddleButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeVerticalAlignment(CellVAlign.Center);
    }

    private void AlignBottomButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeVerticalAlignment(CellVAlign.Bottom);
    }

    private void WrapTextButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeWrapText(_wrapTextButton.IsChecked == true);
    }

    private void MergeAndCenterButton_Click(object? sender, RoutedEventArgs e)
    {
        MergeAndCenterSelectedRange();
    }

    private void DecreaseIndentButton_Click(object? sender, RoutedEventArgs e)
    {
        DecreaseSelectedRangeIndent();
    }

    private void IncreaseIndentButton_Click(object? sender, RoutedEventArgs e)
    {
        IncreaseSelectedRangeIndent();
    }

    private void AlignLeftButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeHorizontalAlignment(CellHAlign.Left);
    }

    private void AlignCenterButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeHorizontalAlignment(CellHAlign.Center);
    }

    private void AlignRightButton_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedRangeHorizontalAlignment(CellHAlign.Right);
    }

    private void UndoLastEdit()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ApplyEditHistoryResult(_session.UndoLastEdit(), "Undid last edit");
    }

    private void RedoLastEdit()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ApplyEditHistoryResult(_session.RedoLastEdit(), "Redid last edit");
    }

    private void ApplyEditHistoryResult(WorkbookCellEditResult result, string successStatus)
    {
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Edit history unavailable.");
            return;
        }

        RefreshShell(successStatus);
    }

    private async Task CutSelectedRangeToClipboardAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        await clipboard.SetTextAsync(_session.CutSelectedRangeText());
        RefreshShell($"Cut {rangeReference}");
    }

    private async Task CopySelectedRangeToClipboardAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        await clipboard.SetTextAsync(_session.CopySelectedRangeText());
        RefreshShell($"Copied {rangeReference}");
    }

    private void SelectCurrentRegionOrAll()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var range = _session.SelectCurrentRegionOrAll();
        RefreshShell($"Selected {FormatRangeReference(range)}");
    }

    private async Task PasteClipboardTextAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        var destination = _session.ActiveCell;
        if (_session.ShouldPreferExternalClipboardImage(text) &&
            await TryPasteClipboardImageAsync(clipboard, destination))
        {
            return;
        }

        var result = _session.PasteClipboardTextAtActiveCell(text);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste failed.");
            return;
        }

        RefreshShell($"Pasted at {FormatCellReference(destination)}");
    }

    private async Task<bool> TryPasteClipboardImageAsync(IClipboard clipboard, CellAddress destination)
    {
        byte[] pngBytes;
        int pixelWidth;
        int pixelHeight;
        try
        {
            using var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is null)
                return false;

            var pixelSize = bitmap.PixelSize;
            pixelWidth = pixelSize.Width;
            pixelHeight = pixelSize.Height;
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            pngBytes = stream.ToArray();
        }
        catch (Exception)
        {
            return false;
        }

        var result = _session.PasteClipboardImageAtActiveCell(pngBytes, pixelWidth, pixelHeight);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste Picture failed.");
            return true;
        }

        RefreshShell($"Pasted picture at {FormatCellReference(destination)}");
        return true;
    }

    internal async Task<bool> TryPasteLaunchSmokeClipboardImageAsync()
    {
        if (_isOpening || _isSaving)
            return false;

        if (!TryCommitPendingFormulaEdit())
            return false;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return false;

        var text = await clipboard.TryGetTextAsync();
        if (!_session.ShouldPreferExternalClipboardImage(text))
            return false;

        return await TryPasteClipboardImageAsync(clipboard, _session.ActiveCell);
    }

    private async Task PasteSpecialClipboardTextAsync(
        PasteCellsMode mode,
        PasteSpecialOptions options,
        string label,
        bool keepSourceColumnWidths = false)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        var destination = _session.ActiveCell;
        var result = keepSourceColumnWidths
            ? _session.PasteSpecialClipboardAtActiveCell(text, mode, options, keepSourceColumnWidths: true)
            : _session.PasteSpecialClipboardAtActiveCell(text, mode, options);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste Special failed.");
            return;
        }

        RefreshShell($"Pasted {label} at {FormatCellReference(destination)}");
    }

    private async Task PasteColumnWidthsFromClipboardAsync(string label)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        var destination = _session.ActiveCell;
        var result = _session.PasteColumnWidthsFromClipboardAtActiveCell(text);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste Column Widths failed.");
            return;
        }

        RefreshShell($"Pasted {label} at {FormatCellReference(destination)}");
    }

    private async Task PasteCommentsFromClipboardAsync(string label)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        var destination = _session.ActiveCell;
        var result = _session.PasteCommentsFromClipboardAtActiveCell(text);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste Comments failed.");
            return;
        }

        RefreshShell($"Pasted {label} at {FormatCellReference(destination)}");
    }

    private async Task PasteDataValidationFromClipboardAsync(string label)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        var destination = _session.ActiveCell;
        var result = _session.PasteDataValidationFromClipboardAtActiveCell(text);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste Validation failed.");
            return;
        }

        RefreshShell($"Pasted {label} at {FormatCellReference(destination)}");
    }

    private async Task PasteLinkFromClipboardAsync(string label)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        var destination = _session.ActiveCell;
        var result = _session.PasteLinkFromClipboardAtActiveCell(text);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste Link failed.");
            return;
        }

        RefreshShell($"Pasted {label} at {FormatCellReference(destination)}");
    }

    private async Task PasteSpecialExternalTextFromClipboardAsync(string label)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        var destination = _session.ActiveCell;
        var result = _session.PasteClipboardTextAtActiveCell(text, preserveText: true);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste Special Text failed.");
            return;
        }

        RefreshShell($"Pasted {label} at {FormatCellReference(destination)}");
    }

    private async Task PastePictureFromClipboardAsync(string label, bool linkedPicture)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        var destination = _session.ActiveCell;
        var result = _session.PastePictureFromClipboardAtActiveCell(text, linkedPicture);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste Picture failed.");
            return;
        }

        RefreshShell($"Pasted {label} at {FormatCellReference(destination)}");
    }

    private void ClearSelectedRangeContents()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.ClearSelectedRangeContents();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Clear Contents failed.");
            return;
        }

        RefreshShell($"Cleared {rangeReference}");
    }

    private void ClearSelectedRangeAll()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.ClearSelectedRangeAll();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Clear All failed.");
            return;
        }

        RefreshShell($"Cleared all from {rangeReference}");
    }

    private void ClearSelectedRangeFormats()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.ClearSelectedRangeFormats();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Clear Formats failed.");
            return;
        }

        RefreshShell($"Cleared formats from {rangeReference}");
    }

    private void ClearSelectedRangeComments()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.ClearSelectedRangeComments();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Clear Comments and Notes failed.");
            return;
        }

        RefreshShell($"Cleared comments and notes from {rangeReference}");
    }

    private void ClearSelectedRangeHyperlinks()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.ClearSelectedRangeHyperlinks();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Clear Hyperlinks failed.");
            return;
        }

        RefreshShell($"Cleared hyperlinks from {rangeReference}");
    }

    private void CaptureFormatPainterSource(bool persistent)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        _session.CaptureFormatPainterSource(persistent);
        RefreshShell(persistent
            ? $"Format Painter locked on {rangeReference}"
            : $"Format Painter copied {rangeReference}");
    }

    private void CancelFormatPainter()
    {
        if (!_session.IsFormatPainterActive)
            return;

        _session.CancelFormatPainter();
        RefreshShell("Format Painter canceled");
    }

    private void ApplyFormatPainterAfterTargetSelection()
    {
        if (!_session.IsFormatPainterActive)
        {
            RefreshShell("Ready");
            return;
        }

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.ApplyFormatPainterToSelectedRange();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Format Painter failed.");
            return;
        }

        RefreshShell($"Applied Format Painter to {rangeReference}");
    }

    private void ToggleSelectedRangeBold()
    {
        ApplySelectedRangeBold(!_session.IsSelectedRangeStartBold);
    }

    private void ToggleSelectedRangeItalic()
    {
        ApplySelectedRangeItalic(!_session.IsSelectedRangeStartItalic);
    }

    private void ToggleSelectedRangeUnderline()
    {
        ApplySelectedRangeUnderline(!_session.IsSelectedRangeStartUnderline);
    }

    private void ToggleSelectedRangeDoubleUnderline()
    {
        ApplySelectedRangeDoubleUnderline(!_session.IsSelectedRangeStartDoubleUnderline);
    }

    private void ToggleSelectedRangeStrikethrough()
    {
        ApplySelectedRangeStrikethrough(!_session.IsSelectedRangeStartStrikethrough);
    }

    private void ToggleSelectedRangeWrapText()
    {
        ApplySelectedRangeWrapText(!_session.IsSelectedRangeStartWrapText);
    }

    private void DecreaseSelectedRangeIndent()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.DecreaseSelectedRangeIndent();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Decrease Indent failed.");
            return;
        }

        RefreshShell($"Decreased indent for {rangeReference}");
    }

    private void IncreaseSelectedRangeIndent()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.IncreaseSelectedRangeIndent();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Increase Indent failed.");
            return;
        }

        RefreshShell($"Increased indent for {rangeReference}");
    }

    private void ApplySelectedRangeBold(bool enabled)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeBold(enabled);
        if (!result.Success)
        {
            _boldButton.IsChecked = _session.IsSelectedRangeStartBold;
            ShowEditIssue(result.ErrorMessage ?? "Bold failed.");
            return;
        }

        RefreshShell($"{(enabled ? "Bolded" : "Unbolded")} {rangeReference}");
    }

    private void ApplySelectedRangeItalic(bool enabled)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeItalic(enabled);
        if (!result.Success)
        {
            _italicButton.IsChecked = _session.IsSelectedRangeStartItalic;
            ShowEditIssue(result.ErrorMessage ?? "Italic failed.");
            return;
        }

        RefreshShell($"{(enabled ? "Italicized" : "Unitalicized")} {rangeReference}");
    }

    private void ApplySelectedRangeUnderline(bool enabled)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeUnderline(enabled);
        if (!result.Success)
        {
            _underlineButton.IsChecked = _session.IsSelectedRangeStartUnderline;
            ShowEditIssue(result.ErrorMessage ?? "Underline failed.");
            return;
        }

        RefreshShell($"{(enabled ? "Underlined" : "Removed underline from")} {rangeReference}");
    }

    private void ApplySelectedRangeDoubleUnderline(bool enabled)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeDoubleUnderline(enabled);
        if (!result.Success)
        {
            _doubleUnderlineButton.IsChecked = _session.IsSelectedRangeStartDoubleUnderline;
            ShowEditIssue(result.ErrorMessage ?? "Double Underline failed.");
            return;
        }

        RefreshShell($"{(enabled ? "Double underlined" : "Removed double underline from")} {rangeReference}");
    }

    private void ApplySelectedRangeStrikethrough(bool enabled)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeStrikethrough(enabled);
        if (!result.Success)
        {
            _strikethroughButton.IsChecked = _session.IsSelectedRangeStartStrikethrough;
            ShowEditIssue(result.ErrorMessage ?? "Strikethrough failed.");
            return;
        }

        RefreshShell($"{(enabled ? "Struck through" : "Removed strikethrough from")} {rangeReference}");
    }

    private void IncreaseSelectedRangeFontSize()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.IncreaseSelectedRangeFontSize();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Increase Font Size failed.");
            return;
        }

        RefreshShell($"Increased font size for {rangeReference}");
    }

    private void DecreaseSelectedRangeFontSize()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.DecreaseSelectedRangeFontSize();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Decrease Font Size failed.");
            return;
        }

        RefreshShell($"Decreased font size for {rangeReference}");
    }

    private void ApplySelectedRangeFillColor(CellColor fillColor)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeFillColor(fillColor);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Fill Color failed.");
            return;
        }

        RefreshShell($"Applied fill color to {rangeReference}");
    }

    private void ClearSelectedRangeFill()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.ClearSelectedRangeFill();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "No Fill failed.");
            return;
        }

        RefreshShell($"Cleared fill from {rangeReference}");
    }

    private void ApplySelectedRangeFontColor(CellColor fontColor)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeFontColor(fontColor);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Font Color failed.");
            return;
        }

        RefreshShell($"Applied font color to {rangeReference}");
    }

    private void ApplySelectedRangeCellStylePreset(CellStylePreset preset)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var presetName = CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset);
        var result = _session.SetSelectedRangeCellStylePreset(preset);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Cell Style failed.");
            return;
        }

        RefreshShell($"Applied {presetName} style to {rangeReference}");
    }

    private void ApplySelectedRangeBorderPreset(CellBorderPreset preset)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var presetName = CellBorderPresetPlanner.GetDisplayName(preset);
        var result = _session.SetSelectedRangeBorderPreset(preset);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Borders failed.");
            return;
        }

        RefreshShell($"Applied {presetName} to {rangeReference}");
    }

    private void MergeAndCenterSelectedRange()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.MergeAndCenterSelectedRange();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Merge & Center failed.");
            return;
        }

        RefreshShell($"Merged and centered {rangeReference}");
    }

    private void UnmergeSelectedRange()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.UnmergeSelectedRange();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Unmerge Cells failed.");
            return;
        }

        RefreshShell($"Unmerged cells in {rangeReference}");
    }

    private void ApplySelectedRangeCurrencyFormat()
    {
        ApplySelectedRangeNumberFormat(CurrencyNumberFormat, "Applied currency format to", "Currency format failed.");
    }

    private void ApplySelectedRangePercentFormat()
    {
        ApplySelectedRangeNumberFormat(PercentNumberFormat, "Applied percent format to", "Percent format failed.");
    }

    private void ApplySelectedRangeCommaStyle()
    {
        ApplySelectedRangeNumberFormat(CommaNumberFormat, "Applied comma style to", "Comma style failed.");
    }

    private void ApplySelectedRangeNumberFormat(string numberFormat, string successAction, string failureMessage)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeNumberFormat(numberFormat);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? failureMessage);
            return;
        }

        RefreshShell($"{successAction} {rangeReference}");
    }

    private void ApplySelectedRangeTextRotation(int textRotation, string successAction, string failureMessage)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeTextRotation(textRotation);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? failureMessage);
            return;
        }

        RefreshShell($"{successAction} {rangeReference}");
    }

    private void IncreaseSelectedRangeDecimalPlaces()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.IncreaseSelectedRangeDecimalPlaces();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Increase Decimal failed.");
            return;
        }

        RefreshShell($"Increased decimals for {rangeReference}");
    }

    private void DecreaseSelectedRangeDecimalPlaces()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.DecreaseSelectedRangeDecimalPlaces();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Decrease Decimal failed.");
            return;
        }

        RefreshShell($"Decreased decimals for {rangeReference}");
    }

    private void ApplySelectedRangeWrapText(bool enabled)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeWrapText(enabled);
        if (!result.Success)
        {
            _wrapTextButton.IsChecked = _session.IsSelectedRangeStartWrapText;
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Wrap Text failed.");
            return;
        }

        RefreshShell($"{(enabled ? "Wrapped" : "Unwrapped")} {rangeReference}");
    }

    private void FreezePanesAtActiveCell()
    {
        ApplyFreezePaneCommand(_session.FreezePanesAtActiveCell, "Froze panes at", "Freeze Panes failed.");
    }

    private void FreezeTopRow()
    {
        ApplyFreezePaneCommand(_session.FreezeTopRow, "Froze top row for", "Freeze Top Row failed.");
    }

    private void FreezeFirstColumn()
    {
        ApplyFreezePaneCommand(_session.FreezeFirstColumn, "Froze first column for", "Freeze First Column failed.");
    }

    private void UnfreezePanes()
    {
        ApplyFreezePaneCommand(_session.UnfreezePanes, "Unfroze panes for", "Unfreeze Panes failed.");
    }

    private void ApplyFreezePaneCommand(Func<WorkbookCellEditResult> execute, string successAction, string failureMessage)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var sheetName = _session.ActiveSheet.Name;
        var result = execute();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? failureMessage);
            return;
        }

        RefreshShell($"{successAction} {sheetName}");
    }

    private void ApplySelectedRangeHorizontalAlignment(CellHAlign alignment)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeHorizontalAlignment(alignment);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Alignment failed.");
            return;
        }

        RefreshShell($"Aligned {rangeReference} {FormatHorizontalAlignmentStatus(alignment)}");
    }

    private void ApplySelectedRangeVerticalAlignment(CellVAlign alignment)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeVerticalAlignment(alignment);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Vertical alignment failed.");
            return;
        }

        RefreshShell($"Aligned {rangeReference} {FormatVerticalAlignmentStatus(alignment)}");
    }

    private void MainWindow_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TrySelectDroppedWorkbookPath(e, out _, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void MainWindow_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!TrySelectDroppedWorkbookPath(e, out var path, out var message))
        {
            ShowOpenIssue(message);
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        await OpenWorkbookPathAsync(path!);
    }

    public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files)
    {
        if (!TrySelectOpenableLocalWorkbookPath(files, out var path, out var message))
        {
            ShowOpenIssue(message);
            return;
        }

        await OpenWorkbookPathAsync(path!);
    }

    internal async Task<MacOsLaunchSmokeDialogSnapshot> CaptureLaunchSmokeDialogEvidenceAsync()
    {
        _launchSmokeDialogEvidence = MacOsLaunchSmokeDialogSnapshot.Empty;

        var hasFindDialog = false;
        var hasFindDialogTextBox = false;
        var hasFindDialogActionButtons = false;
        var hasFindDialogOptions = false;
        var hasFindDialogFormatControls = false;
        var hasFindDialogCompactLayout = false;
        var findDialogResult = await ShowFindInputDialogAsync(probe =>
        {
            hasFindDialog = HasLaunchSmokeDialog(probe.Dialog, "Find");
            hasFindDialogTextBox = HasLaunchSmokeAutomationId(probe.FindBox, "FindTextBox") &&
                probe.FindBox.MinWidth >= 300;
            hasFindDialogActionButtons =
                HasLaunchSmokeButton(probe.FindNextButton, "FindNextButton", "Find Next") &&
                HasLaunchSmokeButton(probe.FindAllButton, "FindAllButton", "Find All") &&
                HasLaunchSmokeButton(probe.CancelButton, "FindCancelButton", "Cancel");
            hasFindDialogOptions = HasLaunchSmokeFindOptions(probe.OptionsControls, "Find", defaultLookInIndex: 0);
            hasFindDialogFormatControls =
                HasLaunchSmokeButton(probe.ChooseFormatButton, "FindChooseFormatFromCellButton", "Choose From Cell") &&
                HasLaunchSmokeButton(probe.ClearFormatButton, "FindClearFormatButton", "Clear Format") &&
                !probe.ClearFormatButton.IsVisible;
            hasFindDialogCompactLayout = HasLaunchSmokeCompactDialog(probe.Dialog, width: 420, height: 430, minWidth: 360, minHeight: 390);
        });

        var hasReplaceDialog = false;
        var hasReplaceDialogTextBoxes = false;
        var hasReplaceDialogActionButtons = false;
        var hasReplaceDialogOptions = false;
        var hasReplaceDialogFormatControls = false;
        var hasReplaceDialogCompactLayout = false;
        var replaceDialogResult = await ShowReplaceInputDialogAsync(probe =>
        {
            hasReplaceDialog = HasLaunchSmokeDialog(probe.Dialog, "Replace");
            hasReplaceDialogTextBoxes =
                HasLaunchSmokeAutomationId(probe.FindBox, "ReplaceFindTextBox") &&
                HasLaunchSmokeAutomationId(probe.ReplaceBox, "ReplaceWithTextBox") &&
                probe.FindBox.MinWidth >= 300 &&
                probe.ReplaceBox.MinWidth >= 300;
            hasReplaceDialogActionButtons =
                HasLaunchSmokeButton(probe.ReplaceButton, "ReplaceButton", "Replace") &&
                HasLaunchSmokeButton(probe.ReplaceAllButton, "ReplaceAllButton", "Replace All") &&
                HasLaunchSmokeButton(probe.CancelButton, "ReplaceCancelButton", "Cancel");
            hasReplaceDialogOptions = HasLaunchSmokeFindOptions(probe.OptionsControls, "Replace", defaultLookInIndex: 1);
            hasReplaceDialogFormatControls =
                HasLaunchSmokeButton(probe.ChooseFindFormatButton, "ReplaceFindChooseFormatFromCellButton", "Choose From Cell") &&
                HasLaunchSmokeButton(probe.ClearFindFormatButton, "ReplaceFindClearFormatButton", "Clear Format") &&
                !probe.ClearFindFormatButton.IsVisible &&
                HasLaunchSmokeButton(probe.ChooseReplaceFormatButton, "ReplaceWithChooseFormatFromCellButton", "Choose From Cell") &&
                HasLaunchSmokeButton(probe.ClearReplaceFormatButton, "ReplaceWithClearFormatButton", "Clear Format") &&
                !probe.ClearReplaceFormatButton.IsVisible;
            hasReplaceDialogCompactLayout = HasLaunchSmokeCompactDialog(probe.Dialog, width: 420, height: 520, minWidth: 360, minHeight: 480);
        });

        var hasGoToDialog = false;
        var hasGoToDialogReferenceControls = false;
        var hasGoToDialogCompactLayout = false;
        var goToDialogResult = await ShowSingleInputDialogAsync(
            "Go To",
            "Reference",
            FormatRangeReference(_session.SelectedRange),
            "Go",
            "GoToReferenceBox",
            probe =>
            {
                hasGoToDialog = HasLaunchSmokeDialog(probe.Dialog, "Go To");
                hasGoToDialogReferenceControls =
                    HasLaunchSmokeAutomationId(probe.InputBox, "GoToReferenceBox") &&
                    HasLaunchSmokeButton(probe.AcceptButton, "GoToReferenceBoxAcceptButton", "Go") &&
                    HasLaunchSmokeButton(probe.CancelButton, "GoToReferenceBoxCancelButton", "Cancel");
                hasGoToDialogCompactLayout = HasLaunchSmokeCompactDialog(probe.Dialog, width: 380, height: 165, minWidth: 340, minHeight: 155);
            });

        var hasGoToSpecialDialog = false;
        var hasGoToSpecialKindControls = false;
        var hasGoToSpecialValueTypeControls = false;
        var hasGoToSpecialDialogCompactLayout = false;
        var goToSpecialDialogResult = await ShowGoToSpecialInputDialogAsync(probe =>
        {
            hasGoToSpecialDialog = HasLaunchSmokeDialog(probe.Dialog, "Go To Special");
            hasGoToSpecialKindControls =
                HasLaunchSmokeAutomationId(probe.KindBox, "GoToSpecialKindBox") &&
                probe.KindBox.SelectedIndex == 0 &&
                HasLaunchSmokeButton(probe.OkButton, "GoToSpecialOkButton", "OK") &&
                HasLaunchSmokeButton(probe.CancelButton, "GoToSpecialCancelButton", "Cancel");
            hasGoToSpecialValueTypeControls =
                HasLaunchSmokeCheckBox(probe.NumbersBox, "GoToSpecialNumbersBox", "Numbers") &&
                HasLaunchSmokeCheckBox(probe.TextBox, "GoToSpecialTextBox", "Text") &&
                HasLaunchSmokeCheckBox(probe.LogicalsBox, "GoToSpecialLogicalsBox", "Logicals") &&
                HasLaunchSmokeCheckBox(probe.ErrorsBox, "GoToSpecialErrorsBox", "Errors");
            hasGoToSpecialDialogCompactLayout = HasLaunchSmokeCompactDialog(probe.Dialog, width: 420, height: 310, minWidth: 360, minHeight: 280);
        });

        _launchSmokeDialogEvidence = new MacOsLaunchSmokeDialogSnapshot(
            hasFindDialog,
            hasFindDialogTextBox,
            hasFindDialogActionButtons,
            hasFindDialogOptions,
            hasFindDialogFormatControls,
            hasFindDialogCompactLayout,
            hasReplaceDialog,
            hasReplaceDialogTextBoxes,
            hasReplaceDialogActionButtons,
            hasReplaceDialogOptions,
            hasReplaceDialogFormatControls,
            hasReplaceDialogCompactLayout,
            hasGoToDialog,
            hasGoToDialogReferenceControls,
            hasGoToDialogCompactLayout,
            hasGoToSpecialDialog,
            hasGoToSpecialKindControls,
            hasGoToSpecialValueTypeControls,
            hasGoToSpecialDialogCompactLayout,
            findDialogResult is null,
            replaceDialogResult is null,
            goToDialogResult is null,
            goToSpecialDialogResult is null);
        return _launchSmokeDialogEvidence;
    }

    private static void RunLaunchSmokeDialogProbe(Window dialog, Action probe)
    {
        try
        {
            probe();
        }
        finally
        {
            Dispatcher.UIThread.Post(() => dialog.Close());
        }
    }

    private static bool HasLaunchSmokeDialog(Window dialog, string title) =>
        dialog.IsVisible &&
        string.Equals(dialog.Title, title, StringComparison.Ordinal);

    private static bool HasLaunchSmokeCompactDialog(
        Window dialog,
        double width,
        double height,
        double minWidth,
        double minHeight) =>
        dialog.Width <= width &&
        dialog.Height <= height &&
        dialog.MinWidth <= minWidth &&
        dialog.MinHeight <= minHeight;

    private static bool HasLaunchSmokeButton(Button button, string automationId, string content) =>
        HasLaunchSmokeAutomationId(button, automationId) &&
        string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal);

    private static bool HasLaunchSmokeCheckBox(CheckBox checkBox, string automationId, string content) =>
        HasLaunchSmokeAutomationId(checkBox, automationId) &&
        string.Equals(checkBox.Content?.ToString(), content, StringComparison.Ordinal);

    private static bool HasLaunchSmokeAutomationId(Control control, string automationId) =>
        string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal);

    private static bool HasLaunchSmokeFindOptions(
        FindOptionsControls controls,
        string automationPrefix,
        int defaultLookInIndex) =>
        HasLaunchSmokeAutomationId(controls.Panel, $"{automationPrefix}OptionsPanel") &&
        HasLaunchSmokeAutomationId(controls.WithinBox, $"{automationPrefix}WithinBox") &&
        HasLaunchSmokeAutomationId(controls.SearchBox, $"{automationPrefix}SearchBox") &&
        HasLaunchSmokeAutomationId(controls.LookInBox, $"{automationPrefix}LookInBox") &&
        HasLaunchSmokeAutomationId(controls.MatchCaseBox, $"{automationPrefix}MatchCaseBox") &&
        HasLaunchSmokeAutomationId(controls.MatchEntireCellBox, $"{automationPrefix}MatchEntireCellBox") &&
        controls.WithinBox.SelectedIndex == 0 &&
        controls.SearchBox.SelectedIndex == 0 &&
        controls.LookInBox.SelectedIndex == defaultLookInIndex;

    internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()
    {
        var hasNativeFileMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "File", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var hasNativeEditMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "Edit", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var hasNativeFormatMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "Format", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var hasNativeViewMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "View", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var hasNativeSheetMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "Sheet", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var hasNativeHelpMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "Help", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var nativeCellStylesPresetCount = _cellStylesMenuItem.Menu?
            .Items
            .OfType<NativeMenuItem>()
            .Count(item => item.Header is not null) ?? 0;
        var nativeOpenRecentItemCount = CountNativeOpenRecentItems(_openRecentMenuItem.Menu);
        var nativeFillColorSwatchCount = CountNativeColorPaletteSwatches(_fillColorMenuItem.Menu);
        var nativeFontColorSwatchCount = CountNativeColorPaletteSwatches(_fontColorMenuItem.Menu);
        var nativeBordersPresetCount = _bordersMenuItem.Menu?
            .Items
            .OfType<NativeMenuItem>()
            .Count(item => item.Header is not null) ?? 0;
        var nativeTabColorSwatchCount = CountNativeColorPaletteSwatches(_tabColorMenuItem.Menu);
        var externalImageClipboardPictures = _session.ActiveSheet.Pictures
            .Where(static picture =>
                picture.Kind == PictureKind.Image &&
                string.Equals(picture.ContentType, "image/png", StringComparison.OrdinalIgnoreCase) &&
                picture.ImageBytes is { Length: > 0 })
            .ToArray();

        return new MacOsLaunchSmokeSnapshot(
            WindowShown: IsVisible,
            WindowTitle: Title ?? "",
            DisplayName: _session.DisplayName,
            ActiveSheetName: _session.ActiveSheet.Name,
            SheetTabCount: _session.SheetTabs.Count,
            ViewportRowCount: _session.Viewport.RowMetrics.Count,
            ViewportColumnCount: _session.Viewport.ColMetrics.Count,
            ExternalImageClipboardPictureCount: externalImageClipboardPictures.Length,
            ExternalImageClipboardPicturePngByteCount: externalImageClipboardPictures.Sum(static picture => picture.ImageBytes!.Length),
            DialogEvidence: _launchSmokeDialogEvidence,
            OpenedSourcePath: _session.CurrentFilePath,
            IsOpening: _isOpening,
            HasNewSheetButton: _newSheetButton.Content?.ToString() == "+",
            HasFormatPainterButton: _formatPainterButton.Content?.ToString() == "Format Painter" &&
                string.Equals(AutomationProperties.GetAutomationId(_formatPainterButton), "HomeFormatPainterButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_formatPainterButton), "Copy formatting from the selection and apply it to another range.", StringComparison.Ordinal),
            HasAutoSumButton: _autoSumButton.Content?.ToString() == "AutoSum" &&
                _autoSumButton.Flyout is MenuFlyout &&
                string.Equals(AutomationProperties.GetAutomationId(_autoSumButton), "HomeAutoSumButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_autoSumButton), "Insert a formula using nearby numeric cells.", StringComparison.Ordinal),
            HasAutoSumSumMenuItem: HasToolbarMenuItem(_autoSumSumFlyoutItem, "Sum"),
            HasAutoSumAverageMenuItem: HasToolbarMenuItem(_autoSumAverageFlyoutItem, "Average"),
            HasAutoSumCountNumbersMenuItem: HasToolbarMenuItem(_autoSumCountNumbersFlyoutItem, "Count Numbers"),
            HasAutoSumCountAllMenuItem: HasToolbarMenuItem(_autoSumCountAllFlyoutItem, "Count All"),
            HasAutoSumMaxMenuItem: HasToolbarMenuItem(_autoSumMaxFlyoutItem, "Max"),
            HasAutoSumMinMenuItem: HasToolbarMenuItem(_autoSumMinFlyoutItem, "Min"),
            HasFillCellsButton: _fillCellsButton.Content?.ToString() == "Fill Cells" &&
                _fillCellsButton.Flyout is MenuFlyout &&
                string.Equals(AutomationProperties.GetAutomationId(_fillCellsButton), "HomeFillCellsButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_fillCellsButton), "Copy the edge cells across the selected range.", StringComparison.Ordinal),
            HasFillDownMenuItem: HasToolbarMenuItem(_fillDownFlyoutItem, "Down"),
            HasFillRightMenuItem: HasToolbarMenuItem(_fillRightFlyoutItem, "Right"),
            HasFillUpMenuItem: HasToolbarMenuItem(_fillUpFlyoutItem, "Up"),
            HasFillLeftMenuItem: HasToolbarMenuItem(_fillLeftFlyoutItem, "Left"),
            HasClearButton: _clearButton.Content?.ToString() == "Clear" &&
                _clearButton.Flyout is MenuFlyout &&
                string.Equals(AutomationProperties.GetAutomationId(_clearButton), "HomeClearButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_clearButton), "Clear contents, formatting, comments, hyperlinks, or all cell state from the selected range.", StringComparison.Ordinal),
            HasClearAllMenuItem: HasToolbarMenuItem(_clearAllFlyoutItem, "Clear All"),
            HasClearFormatsMenuItem: HasToolbarMenuItem(_clearFormatsFlyoutItem, "Clear Formats"),
            HasClearContentsMenuItem: HasToolbarMenuItem(_clearContentsFlyoutItem, "Clear Contents"),
            HasClearCommentsMenuItem: HasToolbarMenuItem(_clearCommentsFlyoutItem, "Clear Comments and Notes"),
            HasClearHyperlinksMenuItem: HasToolbarMenuItem(_clearHyperlinksFlyoutItem, "Clear Hyperlinks"),
            HasBordersButton: _bordersButton.Content?.ToString() == "Borders" &&
                string.Equals(AutomationProperties.GetAutomationId(_bordersButton), "HomeBordersButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_bordersButton), "Apply or change borders on the selected cells.", StringComparison.Ordinal),
            HasWrapTextButton: _wrapTextButton.Content?.ToString() == "Wrap" &&
                string.Equals(AutomationProperties.GetAutomationId(_wrapTextButton), "HomeWrapTextButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_wrapTextButton), "Wrap text within the selected cells.", StringComparison.Ordinal),
            HasMergeAndCenterButton: _mergeAndCenterButton.Content?.ToString() == "Merge & Center" &&
                string.Equals(AutomationProperties.GetAutomationId(_mergeAndCenterButton), "HomeMergeAndCenterButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_mergeAndCenterButton), "Merge and center the selected cells.", StringComparison.Ordinal),
            HasFocusableSheetTab: HasSheetTabButton(button => button.Focusable),
            HasFocusableActiveSheetTab: FindSheetTabButton(_session.ActiveSheet.Id)?.Focusable == true,
            HasShellFocusCycleTargets: _sheetGridHost.Focusable &&
                GetToolbarFocusTargets().Any(control => control.Focusable) &&
                _formulaBox.Focusable &&
                FindSheetTabButton(_session.ActiveSheet.Id)?.Focusable == true &&
                _zoomText.Focusable,
            HasSheetTabContextKeyboardHelp: HasSheetTabButton(button =>
                string.Equals(AutomationProperties.GetHelpText(button), SheetTabContextHelpText, StringComparison.Ordinal)),
            HasSheetTabContextRenameMenuItem: HasSheetTabContextMenuItem("Rename..."),
            HasSheetTabContextTabColorMenuItem: HasSheetTabContextMenuItem("Tab Color"),
            HasSheetTabContextNoColorMenuItem: HasSheetTabContextSubmenuItem("Tab Color", "No Color"),
            HasSheetTabContextSelectAllSheetsMenuItem: HasSheetTabContextMenuItem("Select All Sheets"),
            HasSheetTabContextUngroupSheetsMenuItem: HasSheetTabContextMenuItem("Ungroup Sheets"),
            HasNativeFileMenu: hasNativeFileMenu,
            HasNativeEditMenu: hasNativeEditMenu,
            HasNativeFormatMenu: hasNativeFormatMenu,
            HasNativeViewMenu: hasNativeViewMenu,
            HasNativeSheetMenu: hasNativeSheetMenu,
            HasNativeHelpMenu: hasNativeHelpMenu,
            HasNativeNewWorkbookMenuItem: HasNativeMenuItem(_newWorkbookMenuItem, "New Workbook"),
            HasNativeOpenMenuItem: HasNativeMenuItem(_openMenuItem, "Open..."),
            HasNativeOpenRecentMenuItem: HasNativeMenuItem(_openRecentMenuItem, "Open Recent", requireGesture: false),
            NativeOpenRecentItemCount: nativeOpenRecentItemCount,
            HasNativeSaveMenuItem: HasNativeMenuItem(_saveMenuItem, "Save"),
            HasNativeSaveAsMenuItem: HasNativeMenuItem(_saveAsMenuItem, "Save As..."),
            HasNativeCloseWorkbookMenuItem: HasNativeMenuItem(_closeWorkbookMenuItem, "Close Workbook"),
            HasNativeNewSheetMenuItem: HasNativeMenuItem(_newSheetMenuItem, "New Sheet"),
            HasNativeRenameSheetMenuItem: HasNativeMenuItem(_renameSheetMenuItem, "Rename Sheet...", requireGesture: false),
            HasNativeDuplicateSheetMenuItem: HasNativeMenuItem(_duplicateSheetMenuItem, "Duplicate Sheet", requireGesture: false),
            HasNativeMoveSheetLeftMenuItem: HasNativeMenuItem(_moveSheetLeftMenuItem, "Move Sheet Left", requireGesture: false),
            HasNativeMoveSheetRightMenuItem: HasNativeMenuItem(_moveSheetRightMenuItem, "Move Sheet Right", requireGesture: false),
            HasNativeTabColorMenuItem: HasNativeMenuItem(_tabColorMenuItem, "Tab Color", requireGesture: false),
            HasNativeClearTabColorMenuItem: HasNativeSubmenuItem(_tabColorMenuItem.Menu, "No Color"),
            NativeTabColorSwatchCount: nativeTabColorSwatchCount,
            HasNativeSelectAllSheetsMenuItem: HasNativeMenuItem(_selectAllSheetsMenuItem, "Select All Sheets", requireGesture: false),
            HasNativeUngroupSheetsMenuItem: HasNativeMenuItem(_ungroupSheetsMenuItem, "Ungroup Sheets", requireGesture: false),
            HasNativeHideSheetMenuItem: HasNativeMenuItem(_hideSheetMenuItem, "Hide Sheet", requireGesture: false),
            HasNativeUnhideSheetMenuItem: HasNativeMenuItem(_unhideSheetMenuItem, "Unhide Sheet...", requireGesture: false),
            HasNativeDeleteSheetMenuItem: HasNativeMenuItem(_deleteSheetMenuItem, "Delete Sheet", requireGesture: false),
            HasNativeUndoMenuItem: HasNativeMenuItem(_undoMenuItem, "Undo"),
            HasNativeRedoMenuItem: HasNativeMenuItem(_redoMenuItem, "Redo"),
            HasNativeCutMenuItem: HasNativeMenuItem(_cutMenuItem, "Cut"),
            HasNativeCopyMenuItem: HasNativeMenuItem(_copyMenuItem, "Copy"),
            HasNativePasteMenuItem: HasNativeMenuItem(_pasteMenuItem, "Paste"),
            HasNativePasteSpecialMenuItem: HasNativeMenuItem(_pasteSpecialMenuItem, "Paste Special"),
            HasNativeFormatPainterMenuItem: HasNativeMenuItem(_formatPainterMenuItem, "Format Painter", requireGesture: false),
            HasNativePasteSpecialCommentsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Comments and Notes"),
            HasNativePasteSpecialValidationMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Validation"),
            HasNativePasteSpecialAllExceptBordersMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "All Except Borders"),
            HasNativePasteSpecialAllMergingConditionalFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "All Merging Conditional Formats"),
            HasNativePasteSpecialColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Column Widths"),
            HasNativePasteSpecialFormulasAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Formulas and Number Formats"),
            HasNativePasteSpecialValuesAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Values and Number Formats"),
            HasNativePasteSpecialValuesAndSourceFormattingMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Values and Source Formatting"),
            HasNativePasteSpecialKeepSourceColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Keep Source Column Widths"),
            HasNativePasteSpecialPasteLinkMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Paste Link"),
            HasNativePasteSpecialTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Text"),
            HasNativePasteSpecialUnicodeTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Unicode Text"),
            HasNativePasteSpecialPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Picture"),
            HasNativePasteSpecialLinkedPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Linked Picture"),
            HasNativeSelectAllMenuItem: HasNativeMenuItem(_selectAllMenuItem, "Select All"),
            HasNativeFindMenuItem: HasNativeMenuItem(_findMenuItem, "Find..."),
            HasNativeFindNextMenuItem: HasNativeMenuItem(_findNextMenuItem, "Find Next"),
            HasNativeReplaceMenuItem: HasNativeMenuItem(_replaceMenuItem, "Replace..."),
            HasNativeGoToMenuItem: HasNativeMenuItem(_goToMenuItem, "Go To..."),
            HasNativeGoToSpecialMenuItem: HasNativeMenuItem(_goToSpecialMenuItem, "Go To Special...", requireGesture: false),
            HasNativeAutoSumMenuItem: HasNativeMenuItem(_autoSumMenuItem, "AutoSum", requireGesture: false),
            HasNativeAutoSumSumMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, "Sum"),
            HasNativeAutoSumAverageMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, "Average"),
            HasNativeAutoSumCountNumbersMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, "Count Numbers"),
            HasNativeAutoSumCountAllMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, "Count All"),
            HasNativeAutoSumMaxMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, "Max"),
            HasNativeAutoSumMinMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, "Min"),
            HasNativeFillCellsMenuItem: HasNativeMenuItem(_fillCellsMenuItem, "Fill", requireGesture: false),
            HasNativeFillDownMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, "Down"),
            HasNativeFillRightMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, "Right"),
            HasNativeFillUpMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, "Up"),
            HasNativeFillLeftMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, "Left"),
            HasNativeClearMenuItem: HasNativeMenuItem(_clearMenuItem, "Clear", requireGesture: false),
            HasNativeClearAllMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, "Clear All"),
            HasNativeClearFormatsMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, "Clear Formats"),
            HasNativeClearContentsMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, "Clear Contents"),
            HasNativeClearCommentsMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, "Clear Comments and Notes"),
            HasNativeClearHyperlinksMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, "Clear Hyperlinks"),
            HasNativeBoldMenuItem: HasNativeMenuItem(_boldMenuItem, "Bold"),
            HasNativeItalicMenuItem: HasNativeMenuItem(_italicMenuItem, "Italic"),
            HasNativeUnderlineMenuItem: HasNativeMenuItem(_underlineMenuItem, "Underline"),
            HasNativeDoubleUnderlineMenuItem: HasNativeMenuItem(_doubleUnderlineMenuItem, "Double Underline", requireGesture: false),
            HasNativeStrikethroughMenuItem: HasNativeMenuItem(_strikethroughMenuItem, "Strikethrough"),
            HasNativeIncreaseFontSizeMenuItem: HasNativeMenuItem(_increaseFontSizeMenuItem, "Increase Font Size", requireGesture: false),
            HasNativeDecreaseFontSizeMenuItem: HasNativeMenuItem(_decreaseFontSizeMenuItem, "Decrease Font Size", requireGesture: false),
            HasNativeFillColorMenuItem: HasNativeMenuItem(_fillColorMenuItem, "Fill Color", requireGesture: false),
            HasNativeClearFillMenuItem: HasNativeMenuItem(_clearFillMenuItem, "No Fill", requireGesture: false),
            HasNativeFontColorMenuItem: HasNativeMenuItem(_fontColorMenuItem, "Font Color", requireGesture: false),
            NativeFillColorSwatchCount: nativeFillColorSwatchCount,
            NativeFontColorSwatchCount: nativeFontColorSwatchCount,
            HasNativeBordersMenuItem: HasNativeMenuItem(_bordersMenuItem, "Borders", requireGesture: false),
            NativeBordersPresetCount: nativeBordersPresetCount,
            HasNativeCellStylesMenuItem: HasNativeMenuItem(_cellStylesMenuItem, "Cell Styles", requireGesture: false),
            NativeCellStylesPresetCount: nativeCellStylesPresetCount,
            HasNativeHorizontalTextMenuItem: HasNativeMenuItem(_horizontalTextMenuItem, "Horizontal", requireGesture: false),
            HasNativeAngleCounterclockwiseMenuItem: HasNativeMenuItem(_angleCounterclockwiseMenuItem, "Angle Counterclockwise", requireGesture: false),
            HasNativeAngleClockwiseMenuItem: HasNativeMenuItem(_angleClockwiseMenuItem, "Angle Clockwise", requireGesture: false),
            HasNativeVerticalTextMenuItem: HasNativeMenuItem(_verticalTextMenuItem, "Vertical Text", requireGesture: false),
            HasNativeRotateTextUpMenuItem: HasNativeMenuItem(_rotateTextUpMenuItem, "Rotate Text Up", requireGesture: false),
            HasNativeRotateTextDownMenuItem: HasNativeMenuItem(_rotateTextDownMenuItem, "Rotate Text Down", requireGesture: false),
            HasNativeCurrencyFormatMenuItem: HasNativeMenuItem(_currencyFormatMenuItem, "Accounting Number Format", requireGesture: false),
            HasNativePercentFormatMenuItem: HasNativeMenuItem(_percentFormatMenuItem, "Percent Style", requireGesture: false),
            HasNativeCommaStyleMenuItem: HasNativeMenuItem(_commaStyleMenuItem, "Comma Style", requireGesture: false),
            HasNativeIncreaseDecimalMenuItem: HasNativeMenuItem(_increaseDecimalMenuItem, "Increase Decimal Places", requireGesture: false),
            HasNativeDecreaseDecimalMenuItem: HasNativeMenuItem(_decreaseDecimalMenuItem, "Decrease Decimal Places", requireGesture: false),
            HasNativeAlignTopMenuItem: HasNativeMenuItem(_alignTopMenuItem, "Align Top", requireGesture: false),
            HasNativeAlignMiddleMenuItem: HasNativeMenuItem(_alignMiddleMenuItem, "Align Middle", requireGesture: false),
            HasNativeAlignBottomMenuItem: HasNativeMenuItem(_alignBottomMenuItem, "Align Bottom", requireGesture: false),
            HasNativeWrapTextMenuItem: HasNativeMenuItem(_wrapTextMenuItem, "Wrap Text", requireGesture: false),
            HasNativeMergeAndCenterMenuItem: HasNativeMenuItem(_mergeAndCenterMenuItem, "Merge & Center", requireGesture: false),
            HasNativeUnmergeCellsMenuItem: HasNativeMenuItem(_unmergeCellsMenuItem, "Unmerge Cells", requireGesture: false),
            HasNativeShowGridlinesMenuItem: HasNativeMenuItem(_showGridlinesMenuItem, "Gridlines", requireGesture: false),
            HasNativeShowHeadingsMenuItem: HasNativeMenuItem(_showHeadingsMenuItem, "Headings", requireGesture: false),
            HasNativeZoomInMenuItem: HasNativeMenuItem(_zoomInMenuItem, "Zoom In"),
            HasNativeZoomOutMenuItem: HasNativeMenuItem(_zoomOutMenuItem, "Zoom Out"),
            HasNativeZoom100MenuItem: HasNativeMenuItem(_zoom100MenuItem, "100%"),
            HasNativeZoomToSelectionMenuItem: HasNativeMenuItem(_zoomToSelectionMenuItem, "Zoom to Selection", requireGesture: false),
            HasNativeFreezePanesMenuItem: HasNativeMenuItem(_freezePanesMenuItem, "Freeze Panes", requireGesture: false),
            HasNativeFreezeTopRowMenuItem: HasNativeMenuItem(_freezeTopRowMenuItem, "Freeze Top Row", requireGesture: false),
            HasNativeFreezeFirstColumnMenuItem: HasNativeMenuItem(_freezeFirstColumnMenuItem, "Freeze First Column", requireGesture: false),
            HasNativeUnfreezePanesMenuItem: HasNativeMenuItem(_unfreezePanesMenuItem, "Unfreeze Panes", requireGesture: false),
            HasNativeDecreaseIndentMenuItem: HasNativeMenuItem(_decreaseIndentMenuItem, "Decrease Indent", requireGesture: false),
            HasNativeIncreaseIndentMenuItem: HasNativeMenuItem(_increaseIndentMenuItem, "Increase Indent", requireGesture: false),
            HasNativeAlignLeftMenuItem: HasNativeMenuItem(_alignLeftMenuItem, "Align Left", requireGesture: false),
            HasNativeAlignCenterMenuItem: HasNativeMenuItem(_alignCenterMenuItem, "Align Center", requireGesture: false),
            HasNativeAlignRightMenuItem: HasNativeMenuItem(_alignRightMenuItem, "Align Right", requireGesture: false),
            HasNativeShowFormulasMenuItem: HasNativeMenuItem(_showFormulasMenuItem, "Show Formulas"),
            HasNativeHelpOnlineMenuItem: HasNativeMenuItem(_helpOnlineMenuItem, "Help Online"),
            HasNativeSendFeedbackMenuItem: HasNativeMenuItem(_sendFeedbackMenuItem, "Send Feedback", requireGesture: false),
            HasNativeCheckForUpdatesMenuItem: HasNativeMenuItem(_checkForUpdatesMenuItem, "Check for Updates", requireGesture: false),
            HasNativeAboutMenuItem: HasNativeMenuItem(_aboutMenuItem, "About FreeX", requireGesture: false),
            HasNativeLegalNoticesMenuItem: HasNativeMenuItem(_legalNoticesMenuItem, "Legal Notices", requireGesture: false),
            HasNativeQuitMenuItem: HasNativeMenuItem(_quitMenuItem, "Quit FreeX"));
    }

    private static bool HasToolbarMenuItem(MenuItem item, string expectedHeader) =>
        string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal);

    private static bool HasNativeMenuItem(NativeMenuItem item, string expectedHeader, bool requireGesture = true) =>
        string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal) &&
        (!requireGesture || item.Gesture is not null);

    private bool HasSheetTabButton(Func<Button, bool> predicate) =>
        _sheetTabsHost.Content is StackPanel panel &&
        panel.Children
            .OfType<Button>()
            .Any(predicate);

    private bool HasSheetTabContextMenuItem(string expectedHeader) =>
        _sheetTabsHost.Content is StackPanel panel &&
        panel.Children
            .OfType<Button>()
            .Any(button =>
                button.ContextMenu?.ItemsSource is IEnumerable<Control> items &&
                items
                    .OfType<MenuItem>()
                    .Any(item => string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal)));

    private bool HasSheetTabContextSubmenuItem(string parentHeader, string expectedHeader) =>
        _sheetTabsHost.Content is StackPanel panel &&
        panel.Children
            .OfType<Button>()
            .Any(button =>
                button.ContextMenu?.ItemsSource is IEnumerable<Control> items &&
                items
                    .OfType<MenuItem>()
                    .Where(item => string.Equals(item.Header?.ToString(), parentHeader, StringComparison.Ordinal))
                    .Any(item =>
                        item.ItemsSource is IEnumerable<MenuItem> submenuItems &&
                        submenuItems.Any(submenuItem => string.Equals(submenuItem.Header?.ToString(), expectedHeader, StringComparison.Ordinal))));

    private static bool HasNativeSubmenuItem(NativeMenu? menu, string expectedHeader) =>
        menu?
            .Items
            .OfType<NativeMenuItem>()
            .Any(item => string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal)) == true;

    private static int CountNativeColorPaletteSwatches(NativeMenu? menu) =>
        menu?
            .Items
            .OfType<NativeMenuItem>()
            .Count(item => item.Header?.ToString()?.StartsWith("#", StringComparison.Ordinal) == true) ?? 0;

    private static int CountNativeOpenRecentItems(NativeMenu? menu) =>
        menu?
            .Items
            .OfType<NativeMenuItem>()
            .Count(item => item.IsEnabled && !string.Equals(item.Header?.ToString(), "(No Recent Workbooks)", StringComparison.Ordinal)) ?? 0;

    private void RefreshNativeOpenRecentMenu(bool isIdle)
    {
        _openRecentMenuItem.Menu = CreateNativeOpenRecentMenu(isIdle);
    }

    private NativeMenu CreateNativeOpenRecentMenu(bool isIdle)
    {
        var menu = new NativeMenu();
        var entries = GetOpenableRecentWorkbookEntries();
        if (entries.Count == 0)
        {
            menu.Items.Add(new NativeMenuItem
            {
                Header = "(No Recent Workbooks)",
                IsEnabled = false,
            });
            return menu;
        }

        foreach (var entry in entries)
        {
            var path = entry.Path;
            var item = new NativeMenuItem
            {
                Header = FormatRecentWorkbookMenuHeader(entry),
                IsEnabled = isIdle,
            };
            item.Click += async (_, _) => await OpenRecentWorkbookAsync(path);
            menu.Items.Add(item);
        }

        return menu;
    }

    private List<RecentFileEntry> GetOpenableRecentWorkbookEntries()
    {
        var entries = new List<RecentFileEntry>();
        foreach (var entry in _recentFiles.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path) ||
                !File.Exists(entry.Path) ||
                !_session.TryResolveOpenTarget(entry.Path, out _, out _))
            {
                continue;
            }

            entries.Add(entry);
        }

        entries.Sort(static (left, right) => right.LastOpened.CompareTo(left.LastOpened));
        if (entries.Count > 10)
            entries.RemoveRange(10, entries.Count - 10);

        return entries;
    }

    private static string FormatRecentWorkbookMenuHeader(RecentFileEntry entry)
    {
        var fileName = Path.GetFileName(entry.Path);
        if (string.IsNullOrWhiteSpace(fileName))
            return entry.Path;

        var directory = Path.GetDirectoryName(entry.Path);
        return string.IsNullOrWhiteSpace(directory)
            ? fileName
            : $"{fileName} - {directory}";
    }

    private async Task OpenRecentWorkbookAsync(string path)
    {
        if (!File.Exists(path))
        {
            _recentFiles.Remove(path);
            RefreshNativeOpenRecentMenu(!_isOpening && !_isSaving);
            ShowOpenIssue($"Recent workbook no longer exists: {path}");
            return;
        }

        await OpenWorkbookPathAsync(path);
    }

    private void RecordStartupRecentWorkbook(StartupWorkbookLoadResult source)
    {
        if (!source.IsFallback && !string.IsNullOrWhiteSpace(source.SourcePath))
            RecordRecentWorkbook(source.SourcePath);
    }

    private void RecordRecentWorkbook(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        _recentFiles.AddOrUpdate(path);
        RefreshNativeOpenRecentMenu(!_isOpening && !_isSaving);
    }

    private static bool HasOnlyCommandModifier(KeyModifiers modifiers)
    {
        const KeyModifiers commandModifiers = KeyModifiers.Control | KeyModifiers.Meta;
        return (modifiers & commandModifiers) != 0 &&
            (modifiers & ~commandModifiers) == 0;
    }

    private static bool HasCommandAndShiftModifiers(KeyModifiers modifiers)
    {
        const KeyModifiers commandModifiers = KeyModifiers.Control | KeyModifiers.Meta;
        return modifiers.HasFlag(KeyModifiers.Shift) &&
            (modifiers & commandModifiers) != 0 &&
            (modifiers & ~(commandModifiers | KeyModifiers.Shift)) == 0;
    }

    private static bool IsShellFocusCycleKey(KeyEventArgs args) =>
        args.Key == Key.F6 &&
        (args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift);

    private static bool HasOnlyControlModifier(KeyModifiers modifiers) =>
        modifiers == KeyModifiers.Control;

    private static bool IsAutoSumShortcut(KeyEventArgs args) =>
        args.Key == Key.OemPlus && args.KeyModifiers == KeyModifiers.Alt;

    private static bool IsSelectVisibleCellsOnlyShortcut(KeyEventArgs args) =>
        args.Key == Key.Oem1 && args.KeyModifiers == KeyModifiers.Alt;

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsShellFocusCycleKey(e))
        {
            e.Handled = true;
            CycleShellFocus(reverse: e.KeyModifiers == KeyModifiers.Shift);
            return;
        }

        if (IsAutoSumShortcut(e))
        {
            e.Handled = true;
            InsertAutoSumFormula("SUM");
            return;
        }

        if (IsSelectVisibleCellsOnlyShortcut(e))
        {
            e.Handled = true;
            SelectGoToSpecial(GoToSpecialKind.VisibleCellsOnly);
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                await ShowGoToDialogAsync();
                return;
            }

            if (_formulaBox.IsFocused)
                return;

            if (e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.Shift)
            {
                e.Handled = true;
                AddNewSheet();
                return;
            }

            if (e.Key == Key.F1)
            {
                e.Handled = true;
                await OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, "Help Online");
                return;
            }

            if (e.Key == Key.Delete)
            {
                e.Handled = true;
                ClearSelectedRangeContents();
                return;
            }

            if (e.Key == Key.Escape && _session.IsFormatPainterActive)
            {
                e.Handled = true;
                CancelFormatPainter();
                return;
            }

            NavigateActiveCell(e);
            return;
        }

        if (_formulaBox.IsFocused &&
            e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A or Key.B or Key.D or Key.I or Key.R or Key.U or Key.D4 or Key.NumPad4 or Key.D5 or Key.NumPad5)
        {
            return;
        }

        if (e.Key == Key.PageUp && HasCommandAndShiftModifiers(e.KeyModifiers))
        {
            e.Handled = SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: true);
        }
        else if (e.Key == Key.PageDown && HasCommandAndShiftModifiers(e.KeyModifiers))
        {
            e.Handled = SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: true);
        }
        else if (e.Key == Key.PageUp && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: false);
        }
        else if (e.Key == Key.PageDown && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: false);
        }
        else if (e.Key == Key.F && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            await ShowFindDialogAsync();
        }
        else if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Meta)
        {
            e.Handled = true;
            FindNext();
        }
        else if (e.Key == Key.H && HasOnlyControlModifier(e.KeyModifiers))
        {
            e.Handled = true;
            await ShowReplaceDialogAsync();
        }
        else if (e.Key == Key.G && HasOnlyControlModifier(e.KeyModifiers))
        {
            e.Handled = true;
            await ShowGoToDialogAsync();
        }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            RedoLastEdit();
        }
        else if (e.Key == Key.Z)
        {
            e.Handled = true;
            UndoLastEdit();
        }
        else if (e.Key == Key.Y)
        {
            e.Handled = true;
            RedoLastEdit();
        }
        else if (e.Key == Key.X)
        {
            e.Handled = true;
            await CutSelectedRangeToClipboardAsync();
        }
        else if (e.Key == Key.C)
        {
            e.Handled = true;
            await CopySelectedRangeToClipboardAsync();
        }
        else if (e.Key == Key.V)
        {
            e.Handled = true;
            await PasteClipboardTextAsync();
        }
        else if (e.Key == Key.A && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            SelectCurrentRegionOrAll();
        }
        else if (e.Key == Key.B && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            ToggleSelectedRangeBold();
        }
        else if (e.Key == Key.I && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            ToggleSelectedRangeItalic();
        }
        else if (e.Key == Key.U && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            ToggleSelectedRangeUnderline();
        }
        else if (e.Key == Key.D && HasOnlyControlModifier(e.KeyModifiers))
        {
            e.Handled = true;
            FillSelectedRange(FillCellsDirection.Down);
        }
        else if (e.Key == Key.R && HasOnlyControlModifier(e.KeyModifiers))
        {
            e.Handled = true;
            FillSelectedRange(FillCellsDirection.Right);
        }
        else if (e.Key is Key.D4 or Key.NumPad4 && HasOnlyControlModifier(e.KeyModifiers))
        {
            e.Handled = true;
            ToggleSelectedRangeUnderline();
        }
        else if (e.Key is Key.D5 or Key.NumPad5 && HasOnlyControlModifier(e.KeyModifiers))
        {
            e.Handled = true;
            ToggleSelectedRangeStrikethrough();
        }
        else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            await SaveWorkbookAsAsync();
        }
        else if (e.Key == Key.S)
        {
            e.Handled = true;
            await SaveCurrentWorkbookAsync();
        }
        else if (e.Key == Key.N)
        {
            e.Handled = true;
            CreateNewWorkbook();
        }
        else if (e.Key == Key.W && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            await CloseWorkbookAsync();
        }
        else if (e.Key == Key.O)
        {
            e.Handled = true;
            await OpenWorkbookAsync();
        }
    }

    private void CycleShellFocus(bool reverse)
    {
        var current = GetCurrentShellFocusRegion();
        for (var attempt = 0; attempt < ShellFocusCycle.Length; attempt++)
        {
            current = GetNextShellFocusRegion(current, reverse);
            if (FocusShellRegion(current))
                return;
        }
    }

    private static ShellFocusRegion GetNextShellFocusRegion(ShellFocusRegion current, bool reverse)
    {
        var index = Array.IndexOf(ShellFocusCycle, current);
        if (index < 0)
            index = 0;

        var offset = reverse ? -1 : 1;
        var nextIndex = (index + offset + ShellFocusCycle.Length) % ShellFocusCycle.Length;
        return ShellFocusCycle[nextIndex];
    }

    private ShellFocusRegion GetCurrentShellFocusRegion()
    {
        if (_formulaBox.IsFocused)
            return ShellFocusRegion.FormulaBar;

        if (IsAnySheetTabFocused())
            return ShellFocusRegion.SheetTabs;

        if (_zoomText.IsFocused)
            return ShellFocusRegion.StatusBar;

        if (IsAnyToolbarControlFocused())
            return ShellFocusRegion.Toolbar;

        return ShellFocusRegion.Worksheet;
    }

    private bool FocusShellRegion(ShellFocusRegion region) =>
        region switch
        {
            ShellFocusRegion.Toolbar => FocusFirstEnabledToolbarControl(),
            ShellFocusRegion.FormulaBar => FocusControl(_formulaBox),
            ShellFocusRegion.SheetTabs => FocusActiveSheetTab(),
            ShellFocusRegion.StatusBar => FocusControl(_zoomText),
            _ => FocusControl(_sheetGridHost)
        };

    private bool FocusFirstEnabledToolbarControl()
    {
        foreach (var control in GetToolbarFocusTargets())
        {
            if (FocusControl(control))
                return true;
        }

        return false;
    }

    private IReadOnlyList<Control> GetToolbarFocusTargets() =>
    [
        _openButton,
        _saveButton,
        _saveAsButton,
        _undoButton,
        _redoButton,
        _cutButton,
        _copyButton,
        _pasteButton,
        _pasteSpecialButton,
        _formatPainterButton,
        _autoSumButton,
        _fillCellsButton,
        _clearButton,
        _boldButton,
        _italicButton,
        _underlineButton,
        _doubleUnderlineButton,
        _strikethroughButton,
        _increaseFontSizeButton,
        _decreaseFontSizeButton,
        _fillColorButton,
        _fontColorButton,
        _bordersButton,
        _cellStylesButton,
        _orientationButton,
        _currencyFormatButton,
        _percentFormatButton,
        _commaStyleButton,
        _increaseDecimalButton,
        _decreaseDecimalButton,
        _alignTopButton,
        _alignMiddleButton,
        _alignBottomButton,
        _wrapTextButton,
        _mergeAndCenterButton,
        _decreaseIndentButton,
        _increaseIndentButton,
        _alignLeftButton,
        _alignCenterButton,
        _alignRightButton
    ];

    private bool IsAnyToolbarControlFocused() =>
        GetToolbarFocusTargets().Any(control => control.IsFocused);

    private bool IsAnySheetTabFocused() =>
        _sheetTabsHost.Content is StackPanel panel &&
        panel.Children.OfType<Button>().Any(button => button.IsFocused);

    private static bool FocusControl(Control control)
    {
        if (!control.Focusable ||
            !control.IsEnabled ||
            !control.IsVisible)
        {
            return false;
        }

        control.Focus();
        return control.IsFocused;
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowCloseWithoutDirtyPrompt)
            return;

        if (_isOpening || _isSaving)
        {
            e.Cancel = true;
            ShowOpenIssue("Finish opening or saving before closing FreeX.");
            return;
        }

        if (!TryCommitPendingFormulaEdit())
        {
            e.Cancel = true;
            return;
        }

        if (!_session.IsDirty)
            return;

        e.Cancel = true;
        if (_isDirtyCloseDialogOpen)
            return;

        if (await ConfirmDirtyWorkbookCloseAsync("Close FreeX", "Discard and Close"))
        {
            _allowCloseWithoutDirtyPrompt = true;
            Close();
        }
    }

    private async Task TryQuitApplicationAsync()
    {
        if (_isOpening || _isSaving)
        {
            ShowOpenIssue("Finish opening or saving before quitting FreeX.");
            return;
        }

        if (!TryCommitPendingFormulaEdit())
            return;

        if (!await ConfirmDirtyWorkbookCloseAsync("Quit FreeX", "Discard and Quit"))
            return;

        _allowCloseWithoutDirtyPrompt = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown(0);
            return;
        }

        Close();
    }

    private void NavigateActiveCell(KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            e.Handled = true;
            BeginFormulaEdit(_session.ActiveCell);
            return;
        }

        var pageRows = Math.Max(1, _session.Viewport.RowMetrics.Count - 1);
        var pageCols = Math.Max(1, _session.Viewport.ColMetrics.Count - 1);
        var handled = true;
        switch (e.Key)
        {
            case Key.Up:
                _session.MoveActiveCell(-1, 0);
                break;
            case Key.Down:
                _session.MoveActiveCell(1, 0);
                break;
            case Key.Left:
                _session.MoveActiveCell(0, -1);
                break;
            case Key.Right:
                _session.MoveActiveCell(0, 1);
                break;
            case Key.PageUp:
                _session.MoveActiveCell(-pageRows, 0);
                break;
            case Key.PageDown:
                _session.MoveActiveCell(pageRows, 0);
                break;
            case Key.Home:
                _session.MoveActiveCell(0, 1 - checked((int)_session.ActiveCell.Col));
                break;
            case Key.End:
                _session.MoveActiveCell(0, pageCols);
                break;
            default:
                handled = false;
                break;
        }

        if (!handled)
            return;

        ClearSelectedDrawingObject();
        e.Handled = true;
        RefreshShell("Ready");
    }

    private void MainWindow_TextInput(object? sender, TextInputEventArgs e)
    {
        if (_formulaBox.IsFocused || string.IsNullOrEmpty(e.Text))
            return;

        foreach (var character in e.Text)
        {
            if (char.IsControl(character))
                return;
        }

        BeginFormulaEdit(_session.ActiveCell, e.Text);
        e.Handled = true;
    }

    private void SheetScrollViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_formulaBox.IsFocused)
            return;

        var vertical = e.Delta.Y;
        var horizontal = e.Delta.X;
        var rowDelta = 0;
        var colDelta = 0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
            Math.Abs(horizontal) > Math.Abs(vertical))
        {
            var scroll = Math.Abs(horizontal) > 0 ? horizontal : vertical;
            colDelta = scroll < 0 ? 1 : -1;
        }
        else if (Math.Abs(vertical) > 0)
        {
            rowDelta = vertical < 0 ? 1 : -1;
        }

        if (rowDelta == 0 && colDelta == 0)
            return;

        if (_session.PanViewport(rowDelta * 3, colDelta * 3))
            RefreshShell("Ready");

        e.Handled = true;
    }

    private void SheetScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!TryGetSheetViewportSize(out var viewportHeight, out var viewportWidth))
            return;

        if (_session.UpdateViewportSize(viewportHeight, viewportWidth))
            RefreshShell(string.IsNullOrWhiteSpace(_statusText.Text) ? "Ready" : _statusText.Text);
    }

    private void WorksheetScrollBar_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingWorksheetScrollBars)
            return;

        var (topRow, leftCol) = WorkbookViewportScrollPlanner.CalculateViewportOrigin(
            _session.ActiveSheet,
            _verticalWorksheetScrollBar.Value,
            _horizontalWorksheetScrollBar.Value);
        if (_session.SetViewportOrigin(topRow, leftCol))
            RefreshShell("Ready");
    }

    private async Task OpenWorkbookAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (_session.IsDirty)
        {
            ShowOpenIssue("Save changes before opening another workbook.");
            return;
        }

        if (!StorageProvider.CanOpen)
        {
            ShowOpenIssue("Open unavailable on this platform.");
            return;
        }

        var fileTypes = BuildOpenFileTypes();
        if (fileTypes.Count == 0)
        {
            ShowOpenIssue("No open formats are available.");
            return;
        }

        var storageFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Workbook",
            AllowMultiple = false,
            FileTypeFilter = fileTypes,
        });

        var storageFile = storageFiles.FirstOrDefault();
        if (storageFile is null)
            return;

        using (storageFile)
        {
            var path = storageFile.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowOpenIssue("Open requires a local file path.");
                return;
            }

            await OpenWorkbookPathAsync(path);
        }
    }

    private async Task OpenWorkbookPathAsync(string path)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (_session.IsDirty)
        {
            ShowOpenIssue("Save changes before opening another workbook.");
            return;
        }

        if (!_session.TryResolveOpenTarget(path, out var target, out var message))
        {
            ShowOpenIssue(message);
            return;
        }

        await OpenWorkbookFromTargetAsync(target!);
    }

    private bool TrySelectDroppedWorkbookPath(DragEventArgs e, out string? path, out string message)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            path = null;
            message = "Drop a supported local workbook file.";
            return false;
        }

        return TrySelectOpenableLocalWorkbookPath(files, out path, out message);
    }

    private bool TrySelectOpenableLocalWorkbookPath(IEnumerable<IStorageItem> files, out string? path, out string message)
    {
        path = null;
        if (_isOpening || _isSaving)
        {
            message = "Open is busy.";
            return false;
        }

        if (!TryCommitPendingFormulaEdit())
        {
            message = "Finish the current cell edit before opening another workbook.";
            return false;
        }

        if (_session.IsDirty)
        {
            message = "Save changes before opening another workbook.";
            return false;
        }

        var sawLocalPath = false;
        var sawFileCandidate = false;
        var unsupportedMessage = "Drop a supported workbook file.";
        foreach (var file in files)
        {
            var candidate = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            sawLocalPath = true;
            if (Directory.Exists(candidate))
                continue;
            if (!File.Exists(candidate))
                continue;

            sawFileCandidate = true;
            if (_session.TryResolveOpenTarget(candidate, out _, out unsupportedMessage))
            {
                path = candidate;
                message = "";
                return true;
            }
        }

        message = sawFileCandidate
            ? unsupportedMessage
            : sawLocalPath
                ? "Drop a supported workbook file."
                : "Open requires a local file path.";
        return false;
    }

    private async Task OpenWorkbookFromTargetAsync(WorkbookOpenTarget target)
    {
        try
        {
            _isOpening = true;
            UpdateSaveButton();
            _statusText.Text = "Opening...";
            _statusText.Foreground = Brush(67, 113, 83);
            var progress = new Progress<WorkbookOpenProgressUpdate>(
                update =>
                {
                    _statusText.Text = FormatOpenStatus(update);
                    _statusText.Foreground = Brush(67, 113, 83);
                });

            var result = await _openService.LoadAsync(
                target.Path,
                target.Adapter,
                target.Extension,
                target.Format,
                progress);
            var (viewportHeight, viewportWidth) = GetCurrentSheetViewportSize();
            _session = _sessionFactory.CreateOpened(target, result, viewportHeight, viewportWidth, includeObjects: true);
            RefreshViewportSizeForZoom();
            RecordRecentWorkbook(target.Path);
            ClearSelectedDrawingObject();
            RefreshShell(_session.StartupStatus);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or WorkbookTooLargeException)
        {
            ShowOpenIssue($"Open failed: {ex.Message}");
        }
        finally
        {
            _isOpening = false;
            UpdateSaveButton();
        }
    }

    private async Task SaveCurrentWorkbookAsync()
    {
        if (_isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (_session.CanSaveCurrentSource(out var target))
        {
            await SaveWorkbookToTargetAsync(target!);
            return;
        }

        await SaveWorkbookAsAsync();
    }

    private async Task SaveWorkbookAsAsync()
    {
        if (_isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (!StorageProvider.CanSave)
        {
            ShowSaveIssue("Save As unavailable on this platform.");
            return;
        }

        var fileTypes = BuildSaveFileTypes();
        if (fileTypes.Count == 0)
        {
            ShowSaveIssue("No save formats are available.");
            return;
        }

        var storageFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Workbook",
            SuggestedFileName = _session.BuildSuggestedSaveAsFileName(NativeWorkbookExtension),
            DefaultExtension = NativeWorkbookExtension[1..],
            FileTypeChoices = fileTypes,
            SuggestedFileType = fileTypes[0],
            ShowOverwritePrompt = true,
        });

        if (storageFile is null)
            return;

        using (storageFile)
        {
            var path = storageFile.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowSaveIssue("Save As requires a local file path.");
                return;
            }

            path = WorkbookSession.EnsureSaveExtension(path, NativeWorkbookExtension);
            if (!_session.TryResolveSaveTarget(path, out var target, out var message))
            {
                ShowSaveIssue(message);
                return;
            }

            await SaveWorkbookToTargetAsync(target!);
        }
    }

    private async Task SaveWorkbookToTargetAsync(FileSaveTarget target)
    {
        try
        {
            _isSaving = true;
            UpdateSaveButton();
            _statusText.Text = "Saving...";
            _statusText.Foreground = Brush(67, 113, 83);
            var progress = new Progress<WorkbookSaveProgressUpdate>(
                update =>
                {
                    _statusText.Text = FormatSaveStatus(update);
                    _statusText.Foreground = Brush(67, 113, 83);
                });

            await _saveService.SaveAsync(target.Path, target.Adapter, _session.Workbook, progress);
            _session.MarkSaved(target.Path);
            RecordRecentWorkbook(target.Path);
            RefreshShell($"Saved {Path.GetFileName(target.Path)}");
        }
        catch (Exception ex)
        {
            ShowSaveIssue($"Save failed: {ex.Message}");
        }
        finally
        {
            _isSaving = false;
            UpdateSaveButton();
        }
    }

    private IReadOnlyList<FilePickerFileType> BuildSaveFileTypes()
    {
        var formats = _session.SaveFormats.ToList();

        var nativeIndex = formats.FindIndex(format =>
            string.Equals(
                FileFormatResolver.NormalizeExtension(format.Extension),
                NativeWorkbookExtension,
                StringComparison.OrdinalIgnoreCase));
        if (nativeIndex > 0)
        {
            var native = formats[nativeIndex];
            formats.RemoveAt(nativeIndex);
            formats.Insert(0, native);
        }

        return formats
            .Select(format =>
            {
                var extension = FileFormatResolver.NormalizeExtension(format.Extension);
                return new FilePickerFileType(format.FormatName)
                {
                    Patterns = [$"*{extension}"],
                };
            })
            .ToList();
    }

    private IReadOnlyList<FilePickerFileType> BuildOpenFileTypes()
    {
        var formats = _session.OpenFormats.ToList();
        var patterns = formats
            .Select(format => FileFormatResolver.NormalizeExtension(format.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => $"*{extension}")
            .ToList();
        if (patterns.Count == 0)
            return [];

        var fileTypes = new List<FilePickerFileType>
        {
            new("All supported workbooks")
            {
                Patterns = patterns,
            },
        };

        fileTypes.AddRange(formats.Select(format =>
        {
            var extension = FileFormatResolver.NormalizeExtension(format.Extension);
            return new FilePickerFileType(format.FormatName)
            {
                Patterns = [$"*{extension}"],
            };
        }));

        return fileTypes;
    }

    private void ShowSaveIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
    }

    private void ShowOpenIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
    }

    private void ShowEditIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
        UpdateSaveButton();
    }

    private void ShowHelpIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
    }

    private async Task<bool> ConfirmDirtyWorkbookCloseAsync(string title, string discardButtonText)
    {
        if (!_session.IsDirty)
            return true;

        var choice = await ShowDirtyWorkbookCloseDialogAsync(title, discardButtonText);
        if (choice == DirtyWorkbookCloseChoice.Cancel)
            return false;

        if (choice == DirtyWorkbookCloseChoice.Discard)
            return true;

        await SaveCurrentWorkbookAsync();
        return !_session.IsDirty;
    }

    private async Task<DirtyWorkbookCloseChoice> ShowDirtyWorkbookCloseDialogAsync(
        string title,
        string discardButtonText)
    {
        if (_isDirtyCloseDialogOpen)
            return DirtyWorkbookCloseChoice.Cancel;

        _isDirtyCloseDialogOpen = true;
        var choice = DirtyWorkbookCloseChoice.Cancel;
        try
        {
            var dialog = new Window
            {
                Title = title,
                Width = 440,
                Height = 210,
                MinWidth = 400,
                MinHeight = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var titleText = new TextBlock
            {
                Text = $"Save changes to {_session.DisplayName}?",
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            var detailText = new TextBlock
            {
                Text = "Unsaved changes will be lost if you discard them.",
                Foreground = HeaderForeground,
                TextWrapping = TextWrapping.Wrap,
            };

            var saveButton = new Button
            {
                Content = "Save",
                MinWidth = 92,
                Padding = new Thickness(10, 4),
            };
            AutomationProperties.SetAutomationId(saveButton, "DirtyWorkbookSaveButton");
            AutomationProperties.SetName(saveButton, "Save");
            AutomationProperties.SetHelpText(saveButton, "Save the workbook before closing.");

            var discardButton = new Button
            {
                Content = discardButtonText,
                MinWidth = 132,
                Padding = new Thickness(10, 4),
            };
            AutomationProperties.SetAutomationId(discardButton, "DirtyWorkbookDiscardButton");
            AutomationProperties.SetName(discardButton, discardButtonText);
            AutomationProperties.SetHelpText(discardButton, "Close without saving workbook changes.");

            var cancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 92,
                Padding = new Thickness(10, 4),
            };
            AutomationProperties.SetAutomationId(cancelButton, "DirtyWorkbookCancelButton");
            AutomationProperties.SetName(cancelButton, "Cancel");
            AutomationProperties.SetHelpText(cancelButton, "Return to the workbook without closing.");

            void Finish(DirtyWorkbookCloseChoice selectedChoice)
            {
                choice = selectedChoice;
                dialog.Close();
            }

            saveButton.Click += (_, _) => Finish(DirtyWorkbookCloseChoice.Save);
            discardButton.Click += (_, _) => Finish(DirtyWorkbookCloseChoice.Discard);
            cancelButton.Click += (_, _) => Finish(DirtyWorkbookCloseChoice.Cancel);
            dialog.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Finish(DirtyWorkbookCloseChoice.Save);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    Finish(DirtyWorkbookCloseChoice.Cancel);
                    e.Handled = true;
                }
            };

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                Children =
                {
                    cancelButton,
                    discardButton,
                    saveButton,
                },
            };

            dialog.Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 12,
                Children =
                {
                    titleText,
                    detailText,
                    new Border { Height = 10 },
                    buttonRow,
                },
            };

            await dialog.ShowDialog(this);
            return choice;
        }
        finally
        {
            _isDirtyCloseDialogOpen = false;
        }
    }

    private async Task OpenExternalHelpLinkAsync(string url, string title)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowHelpIssue($"{title} link is blocked.");
            return;
        }

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            ShowHelpIssue($"{title} link cannot be opened on this platform.");
            return;
        }

        try
        {
            if (!await launcher.LaunchUriAsync(uri))
                ShowHelpIssue($"{title} link could not be opened.");
        }
        catch (Exception ex)
        {
            ShowHelpIssue($"{title} link could not be opened: {ex.Message}");
        }
    }

    private async Task ShowAboutDialogAsync()
    {
        var versionText = AppHelpInfo.GetVersionText(typeof(MainWindow).Assembly);
        await ShowTextDialogAsync(
            "About FreeX",
            AppHelpInfo.BuildAboutText(versionText, PlatformAboutSummary),
            560,
            460);
    }

    private async Task ShowLegalNoticesDialogAsync()
    {
        var text = string.Join(
            Environment.NewLine + Environment.NewLine,
            LegalNoticeProvider.GetDocuments().Select(document =>
                $"{document.Title}{Environment.NewLine}{new string('=', document.Title.Length)}{Environment.NewLine}{document.Text.Trim()}"));

        await ShowTextDialogAsync("Legal Notices", text, 860, 620);
    }

    private async Task ShowTextDialogAsync(string title, string text, double width, double height)
    {
        var dialog = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 420),
            MinHeight = Math.Min(height, 320),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var closeButton = new Button
        {
            Content = "Close",
            Width = 92,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        closeButton.Click += (_, _) => dialog.Close();

        var textBox = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Padding = new Thickness(8),
            MinHeight = 240,
        };

        var root = new DockPanel
        {
            Margin = new Thickness(16),
        };
        DockPanel.SetDock(closeButton, Dock.Bottom);
        root.Children.Add(closeButton);
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = textBox,
        });

        dialog.Content = root;
        await dialog.ShowDialog(this);
    }

    private (double Height, double Width) GetCurrentSheetViewportSize()
    {
        return TryGetSheetViewportSize(out var viewportHeight, out var viewportWidth)
            ? (viewportHeight, viewportWidth)
            : (_session.ViewportHeight, _session.ViewportWidth);
    }

    private bool TryGetSheetViewportSize(out double viewportHeight, out double viewportWidth)
    {
        if (!TryGetSheetViewportDisplaySize(out var displayHeight, out var displayWidth))
        {
            viewportHeight = 0;
            viewportWidth = 0;
            return false;
        }

        var zoomFactor = GetActiveZoomFactor();
        viewportHeight = displayHeight / zoomFactor;
        viewportWidth = displayWidth / zoomFactor;
        return true;
    }

    private bool TryGetSheetViewportDisplaySize(out double viewportHeight, out double viewportWidth)
    {
        var bounds = _sheetScrollViewer.Bounds;
        var zoomFactor = GetActiveZoomFactor();
        var showHeadings = _session.ActiveSheet.ShowHeadings;
        var headerHeight = showHeadings ? HeaderRowHeight * zoomFactor : 0;
        var headerWidth = showHeadings ? HeaderColumnWidth * zoomFactor : 0;
        if (bounds.Height <= headerHeight || bounds.Width <= headerWidth)
        {
            viewportHeight = 0;
            viewportWidth = 0;
            return false;
        }

        viewportHeight = bounds.Height - headerHeight;
        viewportWidth = bounds.Width - headerWidth;
        return true;
    }

    private void RefreshViewportSizeForZoom()
    {
        if (TryGetSheetViewportSize(out var viewportHeight, out var viewportWidth))
            _session.UpdateViewportSize(viewportHeight, viewportWidth);
    }

    private double GetActiveZoomFactor() =>
        ClampZoomPercent(_session.ZoomPercent) / 100d;

    private static int ClampZoomPercent(int zoomPercent) =>
        Math.Clamp(
            zoomPercent,
            SetWorksheetZoomCommand.MinZoomPercent,
            SetWorksheetZoomCommand.MaxZoomPercent);

    private static string FormatZoomPercent(int zoomPercent) =>
        $"{ClampZoomPercent(zoomPercent)}%";

    private bool ShouldUseWarningStatusColor(string status) =>
        _session.IsFallback ||
        status.Contains("Unsupported XLSX", StringComparison.Ordinal) ||
        status.Contains("load warning", StringComparison.OrdinalIgnoreCase);

    private static string FormatSaveStatus(WorkbookSaveProgressUpdate update) =>
        update.Phase switch
        {
            WorkbookSavePhase.Preparing => "Preparing save...",
            WorkbookSavePhase.Writing => "Writing file...",
            WorkbookSavePhase.Completed => "Saved",
            _ => "Saving..."
        };

    private static string FormatOpenStatus(WorkbookOpenProgressUpdate update) =>
        update.Phase switch
        {
            WorkbookOpenPhase.Reading => "Reading file...",
            WorkbookOpenPhase.Inspecting => "Inspecting workbook...",
            WorkbookOpenPhase.Parsing => "Opening workbook...",
            WorkbookOpenPhase.Calculating => "Calculating workbook...",
            _ => "Opening..."
        };

    private static string FormatCellReference(CellAddress address) =>
        CellAddress.NumberToColumnName(address.Col) + address.Row.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatRangeReference(GridRange range)
    {
        var start = FormatCellReference(range.Start);
        var end = FormatCellReference(range.End);
        return string.Equals(start, end, StringComparison.Ordinal)
            ? start
            : $"{start}:{end}";
    }

    private static string FormatFillCellsAction(FillCellsDirection direction) =>
        direction switch
        {
            FillCellsDirection.Down => "Filled down",
            FillCellsDirection.Right => "Filled right",
            FillCellsDirection.Up => "Filled up",
            FillCellsDirection.Left => "Filled left",
            _ => "Filled"
        };

    private static string FormatHorizontalAlignmentStatus(CellHAlign alignment) =>
        alignment switch
        {
            CellHAlign.Left => "left",
            CellHAlign.Center => "center",
            CellHAlign.Right => "right",
            _ => "general"
        };

    private static string FormatVerticalAlignmentStatus(CellVAlign alignment) =>
        alignment switch
        {
            CellVAlign.Top => "top",
            CellVAlign.Center => "middle",
            CellVAlign.Bottom => "bottom",
            _ => "middle"
        };

    private static string FormatEditText(Cell? cell, CellAddress address)
    {
        if (cell?.HasFormula == true && cell.FormulaText is not null)
            return "=" + cell.FormulaText;

        return FormatScalarValue(cell?.Value);
    }

    private static string FormatScalarValue(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TextValue text => text.Value,
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue dateTime => dateTime.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ErrorValue error => error.Code,
        _ => ""
    };

    private static void AddGridChild(AvaloniaGrid grid, Control control, int row, int column)
    {
        AvaloniaGrid.SetRow(control, row);
        AvaloniaGrid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static IBrush Brush(byte red, byte green, byte blue) =>
        new SolidColorBrush(Color.FromRgb(red, green, blue));

    private static IBrush Brush(byte alpha, byte red, byte green, byte blue) =>
        new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));

    private static IBrush Brush(CellColor color) =>
        Brush(color.R, color.G, color.B);
}

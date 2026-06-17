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
using System.Globalization;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.App.Services.Updates;
using FreeX.Ribbon.Avalonia;
using Free.Shared.Ribbon;
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

public sealed partial class MainWindow : Window
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

    private enum MergeCellsWarningChoice
    {
        Cancel,
        KeepFirstCell,
        ConcatenateAllCells
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

    private enum DataValidationDialogAction
    {
        Apply,
        Clear
    }

    private enum SubtotalDialogAction
    {
        Apply,
        RemoveAll
    }

    private enum GoalSeekStatusDialogChoice
    {
        KeepResult,
        RestoreOriginalValues,
        Dismiss
    }

    private sealed record ScenarioManagerDialogScenarioItem(ScenarioManagerScenarioChoice Choice)
    {
        public override string ToString()
        {
            var cellLabel = Choice.ChangingCellCount == 1 ? "cell" : "cells";
            return $"{Choice.Name} ({Choice.ChangingCellCount} {cellLabel})";
        }
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
    private sealed record SortDialogSmokeProbe(
        Window Dialog,
        CheckBox HeadersCheckBox,
        StackPanel LevelsPanel,
        ComboBox SortOnBox,
        ComboBox ColorBox,
        Button AddLevelButton,
        Button OkButton,
        Button CancelButton);
    private sealed record DataValidationDialogResult(
        DataValidationDialogAction Action,
        DataValidation? Rule);
    private sealed record DataValidationDialogSmokeProbe(
        Window Dialog,
        TextBlock SummaryText,
        ComboBox TypeBox,
        ComboBox OperatorBox,
        TextBox Formula1Box,
        TextBox Formula2Box,
        CheckBox AllowBlankBox,
        CheckBox ShowDropdownBox,
        CheckBox ShowInputMessageBox,
        TextBox PromptTitleBox,
        TextBox PromptMessageBox,
        CheckBox ShowErrorMessageBox,
        ComboBox AlertStyleBox,
        TextBox ErrorTitleBox,
        TextBox ErrorMessageBox,
        Button ApplyButton,
        Button ClearButton,
        Button CancelButton);
    private sealed record SubtotalDialogResult(
        SubtotalDialogAction Action,
        SubtotalInputOptions? Options);
    private sealed record SubtotalColumnChoice(uint Offset, string Header, bool IsSelected)
    {
        public override string ToString() => Header;
    }
    private sealed record SubtotalFunctionChoice(string Label, string FunctionText)
    {
        public override string ToString() => Label;
    }
    private sealed record SortDialogResult(
        IReadOnlyList<SortDialogLevel> Levels,
        bool HasHeaders);
    private sealed record SortDialogComboItem<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }
    private sealed record DataValidationTypeChoice(DvType Type, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record DataValidationOperatorChoice(DvOperator Operator, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record DataValidationAlertStyleChoice(DvAlertStyle AlertStyle, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record FormatCellsDialogResult(
        FormatCellsCompactRequest Request,
        CellBorderPreset? BorderPreset,
        BorderStyle BorderStyle,
        CellColor? BorderColor);
    private sealed record FormatCellsNumberFormatChoice(string Category, string FormatCode, string Preview)
    {
        public override string ToString() => FormatCode;
    }
    private sealed record FormatCellsNullableChoice<T>(string Label, T? Value)
        where T : struct
    {
        public override string ToString() => Label;
    }
    private sealed record FormatCellsColorChoice(string Label, CellColor? Color, bool Clear)
    {
        public override string ToString() => Label;
    }
    private sealed record FormatCellsDialogSmokeProbe(
        Window Dialog,
        TabControl TabStrip,
        TabItem NumberTab,
        TabItem AlignmentTab,
        TabItem FontTab,
        TabItem FillTab,
        TabItem BorderTab,
        TabItem ProtectionTab,
        ListBox NumberCategoryList,
        ComboBox NumberFormatBox,
        TextBlock NumberPreview,
        Button OkButton,
        Button CancelButton);

    private const double CellIndentLevelWidth = 12;
    private const string CommaNumberFormat = "#,##0.00";
    private const string CurrencyNumberFormat = "$#,##0.00";
    private const double DoubleUnderlineSecondStrokeOffset = 2;
    private const string GeneralNumberFormat = "General";
    private const string PercentNumberFormat = "0%";
    private const double HeaderColumnWidth = 58;
    private const double HeaderRowHeight = 28;
    private const double InitialViewportHeight = 880;
    private const double InitialViewportWidth = 1440;
    private const double MinimumDisplayedColumnWidth = 54;
    private const double MinimumDisplayedRowHeight = 22;
    private const uint PortablePdfColumnsPerPage = 8;
    private const uint PortablePdfRowsPerPage = 28;
    private const double ZoomToSelectionDefaultColumnWidth = 80;
    private const double ZoomToSelectionDefaultRowHeight = 20;
    private const int ZoomStepPercent = 10;
    private const string WorkbookShareSheetLabel = "macOS Share Sheet";
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
    private readonly IWorkbookShareSheetService _workbookShareSheetService;
    private readonly IWorkbookFileAccessService _workbookFileAccessService;
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
    private readonly Button _updateReadyIndicator = new();
    private IUpdateService? _updateService;
    private string? _stagedUpdateVersion;
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
    private readonly NativeMenuItem _exportPdfMenuItem = new();
    private readonly NativeMenuItem _shareWorkbookMenuItem = new();
    private readonly NativeMenuItem _workbookStatisticsMenuItem = new();
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
    private readonly NativeMenuItem _openHyperlinkMenuItem = new();
    private readonly NativeMenuItem _insertHyperlinkMenuItem = new();
    private readonly NativeMenuItem _insertColumnChartMenuItem = new();
    private readonly NativeMenuItem _insertBarChartMenuItem = new();
    private readonly NativeMenuItem _insertLineChartMenuItem = new();
    private readonly NativeMenuItem _insertPieChartMenuItem = new();
    private readonly NativeMenuItem _insertAreaChartMenuItem = new();
    private readonly NativeMenuItem _insertScatterChartMenuItem = new();
    private readonly NativeMenuItem _insertTableMenuItem = new();
    private readonly NativeMenuItem _sortAscendingMenuItem = new();
    private readonly NativeMenuItem _sortDescendingMenuItem = new();
    private readonly NativeMenuItem _customSortMenuItem = new();
    private readonly NativeMenuItem _flashFillMenuItem = new();
    private readonly NativeMenuItem _advancedFilterMenuItem = new();
    private readonly NativeMenuItem _removeDuplicatesMenuItem = new();
    private readonly NativeMenuItem _subtotalMenuItem = new();
    private readonly NativeMenuItem _textToColumnsMenuItem = new();
    private readonly NativeMenuItem _consolidateMenuItem = new();
    private readonly NativeMenuItem _dataValidationPreviewMenuItem = new();
    private readonly NativeMenuItem _dataValidationMenuItem = new();
    private readonly NativeMenuItem _whatIfAnalysisMenuItem = new();
    private readonly NativeMenuItem _goalSeekMenuItem = new();
    private readonly NativeMenuItem _scenarioManagerMenuItem = new();
    private readonly NativeMenuItem _dataTableMenuItem = new();
    private readonly NativeMenuItem _forecastSheetMenuItem = new();
    private readonly NativeMenuItem _reviewSummaryMenuItem = new();
    private readonly NativeMenuItem _checkAccessibilityMenuItem = new();
    private readonly NativeMenuItem _protectSheetMenuItem = new();
    private readonly NativeMenuItem _protectWorkbookMenuItem = new();
    private readonly NativeMenuItem _nextNoteMenuItem = new();
    private readonly NativeMenuItem _previousNoteMenuItem = new();
    private readonly NativeMenuItem _nextCommentMenuItem = new();
    private readonly NativeMenuItem _previousCommentMenuItem = new();
    private readonly NativeMenuItem _insertFunctionMenuItem = new();
    private readonly NativeMenuItem _nameManagerMenuItem = new();
    private readonly NativeMenuItem _defineNameMenuItem = new();
    private readonly NativeMenuItem _createNamesFromSelectionMenuItem = new();
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
    private MacOsLaunchSmokeLiveCommandKeySnapshot _launchSmokeLiveCommandKeySnapshot = MacOsLaunchSmokeLiveCommandKeySnapshot.Empty;
    private readonly NativeMenuItem _doubleUnderlineMenuItem = new();
    private readonly NativeMenuItem _strikethroughMenuItem = new();
    private readonly NativeMenuItem _increaseFontSizeMenuItem = new();
    private readonly NativeMenuItem _decreaseFontSizeMenuItem = new();
    private readonly NativeMenuItem _fillColorMenuItem = new();
    private readonly NativeMenuItem _clearFillMenuItem = new();
    private readonly NativeMenuItem _fontColorMenuItem = new();
    private readonly NativeMenuItem _bordersMenuItem = new();
    private readonly NativeMenuItem _cellStylesMenuItem = new();
    private readonly NativeMenuItem _formatCellsMenuItem = new();
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
    private readonly NativeMenuItem _pageSetupMenuItem = new();
    private readonly NativeMenuItem _printPreviewMenuItem = new();
    private readonly NativeMenuItem _pageBreakPreviewMenuItem = new();
    private readonly NativeMenuItem _minimizeWindowMenuItem = new();
    private readonly NativeMenuItem _zoomWindowMenuItem = new();
    private readonly NativeMenuItem _bringAllToFrontMenuItem = new();
    private readonly NativeMenuItem _helpOnlineMenuItem = new();
    private readonly NativeMenuItem _sendFeedbackMenuItem = new();
    private readonly NativeMenuItem _checkForUpdatesMenuItem = new();
    private readonly NativeMenuItem _aboutMenuItem = new();
    private readonly NativeMenuItem _legalNoticesMenuItem = new();
    private readonly NativeMenuItem _quitMenuItem = new();
    private NativeMenu? _nativeMenu;
    private WorkbookSession _session;
    private MacOsLaunchSmokeDialogSnapshot _launchSmokeDialogEvidence = MacOsLaunchSmokeDialogSnapshot.Empty;
    private ComboBox? _activeDataValidationDropdown;
    private IReadOnlyDictionary<(uint Row, uint Col), (IReadOnlyList<double> Values, SparklineKind Kind)> _sparklinesByCell =
        new Dictionary<(uint Row, uint Col), (IReadOnlyList<double>, SparklineKind)>();
    private string? _formulaBoxEditOriginalText;
    private bool _isOpening;
    private bool _isSaving;
    private bool _allowCloseWithoutDirtyPrompt;
    private bool _isDirtyCloseDialogOpen;
    private bool _isUpdatingWorksheetScrollBars;
    private SelectionPaneObjectKind? _selectedDrawingObjectKind;
    private Guid? _selectedDrawingObjectId;

    public MainWindow(IReadOnlyList<string> startupArguments)
        : this(
            startupArguments,
            WorkbookShareSheetServiceFactory.Create(WorkbookShareSheetLabel),
            WorkbookFileAccessServiceFactory.Create(App.Diagnostics))
    {
    }

    internal MainWindow(
        IReadOnlyList<string> startupArguments,
        IWorkbookShareSheetService workbookShareSheetService,
        IWorkbookFileAccessService workbookFileAccessService)
    {
        ArgumentNullException.ThrowIfNull(workbookShareSheetService);
        ArgumentNullException.ThrowIfNull(workbookFileAccessService);

        _workbookShareSheetService = workbookShareSheetService;
        _workbookFileAccessService = workbookFileAccessService;
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

        var ribbon = FreeX.App.Avalonia.Ribbon.AvaloniaRibbonHost.Build(
            () => _session,
            RefreshShell,
            openTextToColumns: TextToColumns,
            openConsolidate: Consolidate);
        DockPanel.SetDock(ribbon, Dock.Top);
        root.Children.Add(ribbon);

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var sheetTabs = BuildSheetTabsChrome();
        DockPanel.SetDock(sheetTabs, Dock.Bottom);
        root.Children.Add(sheetTabs);

        var pivotFieldPane = BuildPivotFieldPaneChrome();
        DockPanel.SetDock(pivotFieldPane, Dock.Right);
        root.Children.Add(pivotFieldPane);

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

        _updateReadyIndicator.Content = new TextBlock
        {
            Text = "↻ Update ready",
            FontSize = 11,
            Opacity = 0.75,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        _updateReadyIndicator.Background = Brushes.Transparent;
        _updateReadyIndicator.BorderThickness = new Thickness(0);
        _updateReadyIndicator.Padding = new Thickness(6, 0);
        _updateReadyIndicator.IsVisible = false;
        _updateReadyIndicator.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _updateReadyIndicator.Click += UpdateReadyIndicator_Click;
        AutomationProperties.SetAutomationId(_updateReadyIndicator, "UpdateReadyIndicator");
        AutomationProperties.SetName(_updateReadyIndicator, "Update ready");
        AutomationProperties.SetHelpText(
            _updateReadyIndicator,
            "A new version of FreeX has been downloaded. Click to restart and update.");

        var chrome = new DockPanel
        {
            LastChildFill = true,
        };
        DockPanel.SetDock(_newSheetButton, Dock.Right);
        chrome.Children.Add(_newSheetButton);
        DockPanel.SetDock(_updateReadyIndicator, Dock.Right);
        chrome.Children.Add(_updateReadyIndicator);
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

    /// <summary>
    /// Wires the update service and starts a background check. When an update has been
    /// downloaded, the discreet status-strip indicator is revealed on the UI thread.
    /// </summary>
    internal void AttachUpdateService(IUpdateService updateService)
    {
        ArgumentNullException.ThrowIfNull(updateService);
        _updateService = updateService;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await updateService.CheckAndDownloadAsync().ConfigureAwait(false);
                if (result.State == UpdateState.ReadyToApply)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => ShowUpdateReady(result.AvailableVersion));
                }
            }
            catch
            {
                // Best-effort: a failed background check must never disrupt the app.
            }
        });
    }

    /// <summary>Reveal the discreet update-ready indicator. Safe to call only on the UI thread.</summary>
    public void ShowUpdateReady(string? version)
    {
        _stagedUpdateVersion = version;
        _updateReadyIndicator.IsVisible = true;
    }

    private void UpdateReadyIndicator_Click(object? sender, RoutedEventArgs e)
    {
        var versionSuffix = string.IsNullOrWhiteSpace(_stagedUpdateVersion)
            ? string.Empty
            : $" {_stagedUpdateVersion}";
        RefreshShell($"Restarting to install FreeX{versionSuffix}...");
        _updateService?.ApplyAndRestart();
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

        _exportPdfMenuItem.Header = "Export to PDF...";
        _exportPdfMenuItem.Click += async (_, _) => await ExportActiveSheetPdfAsync();

        _pageSetupMenuItem.Header = "Page Setup...";
        _pageSetupMenuItem.Click += async (_, _) => await ShowPageSetupDialogAsync();

        _printPreviewMenuItem.Header = "Print Preview...";
        _printPreviewMenuItem.Gesture = new KeyGesture(Key.P, KeyModifiers.Meta);
        _printPreviewMenuItem.Click += async (_, _) => await ShowPrintPreviewDialogAsync();

        _shareWorkbookMenuItem.Header = "Share Workbook...";
        _shareWorkbookMenuItem.Click += async (_, _) => await ShareWorkbookAsync();

        _workbookStatisticsMenuItem.Header = "Workbook Statistics...";
        _workbookStatisticsMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Control | KeyModifiers.Shift);
        _workbookStatisticsMenuItem.Click += async (_, _) => await ShowWorkbookStatisticsDialogAsync();

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

        _openHyperlinkMenuItem.Header = "Open Hyperlink";
        _openHyperlinkMenuItem.Click += async (_, _) => await OpenSelectedHyperlinkAsync();

        _insertHyperlinkMenuItem.Header = "Hyperlink...";
        _insertHyperlinkMenuItem.Click += async (_, _) => await ShowInsertHyperlinkDialogAsync();

        _insertColumnChartMenuItem.Header = "Column Chart";
        _insertColumnChartMenuItem.Click += (_, _) => InsertChartFromSelection(ChartType.Column);
        _insertBarChartMenuItem.Header = "Bar Chart";
        _insertBarChartMenuItem.Click += (_, _) => InsertChartFromSelection(ChartType.Bar);
        _insertLineChartMenuItem.Header = "Line Chart";
        _insertLineChartMenuItem.Click += (_, _) => InsertChartFromSelection(ChartType.Line);
        _insertPieChartMenuItem.Header = "Pie Chart";
        _insertPieChartMenuItem.Click += (_, _) => InsertChartFromSelection(ChartType.Pie);
        _insertAreaChartMenuItem.Header = "Area Chart";
        _insertAreaChartMenuItem.Click += (_, _) => InsertChartFromSelection(ChartType.Area);
        _insertScatterChartMenuItem.Header = "Scatter Chart";
        _insertScatterChartMenuItem.Click += (_, _) => InsertChartFromSelection(ChartType.Scatter);

        _insertTableMenuItem.Header = "Table...";
        _insertTableMenuItem.Click += (_, _) => InsertTableFromSelection();

        _sortAscendingMenuItem.Header = "Sort A to Z";
        _sortAscendingMenuItem.Click += (_, _) => SortSelectedRange(ascending: true);

        _sortDescendingMenuItem.Header = "Sort Z to A";
        _sortDescendingMenuItem.Click += (_, _) => SortSelectedRange(ascending: false);

        _customSortMenuItem.Header = "Sort...";
        _customSortMenuItem.Click += async (_, _) => await ShowSortDialogAsync();

        _flashFillMenuItem.Header = "Flash Fill";
        _flashFillMenuItem.Gesture = new KeyGesture(Key.E, KeyModifiers.Control);
        _flashFillMenuItem.Click += (_, _) => FlashFillSelectedRange();

        _advancedFilterMenuItem.Header = "Advanced Filter...";
        _advancedFilterMenuItem.Click += async (_, _) => await ShowAdvancedFilterDialogAsync();

        _removeDuplicatesMenuItem.Header = "Remove Duplicates...";
        _removeDuplicatesMenuItem.Click += async (_, _) => await ShowRemoveDuplicatesDialogAsync();

        _subtotalMenuItem.Header = "Subtotal...";
        _subtotalMenuItem.Click += async (_, _) => await ShowSubtotalDialogAsync();

        _textToColumnsMenuItem.Header = "Text to Columns...";
        _textToColumnsMenuItem.Click += async (_, _) => await ShowTextToColumnsDialogAsync();

        _consolidateMenuItem.Header = "Consolidate...";
        _consolidateMenuItem.Click += async (_, _) => await ShowConsolidateDialogAsync();

        _dataValidationPreviewMenuItem.Header = "Data Validation Preview...";
        _dataValidationPreviewMenuItem.Click += async (_, _) => await ShowDataValidationPreviewDialogAsync();

        _dataValidationMenuItem.Header = "Data Validation...";
        _dataValidationMenuItem.Click += async (_, _) => await ShowDataValidationDialogAsync();

        _goalSeekMenuItem.Header = "Goal Seek...";
        _goalSeekMenuItem.Click += async (_, _) => await ShowGoalSeekDialogAsync();

        _scenarioManagerMenuItem.Header = "Scenario Manager...";
        _scenarioManagerMenuItem.Click += async (_, _) => await ShowScenarioManagerDialogAsync();

        _dataTableMenuItem.Header = "Data Table...";
        _dataTableMenuItem.Click += async (_, _) => await ShowDataTableDialogAsync();

        _forecastSheetMenuItem.Header = "Forecast Sheet...";
        _forecastSheetMenuItem.Click += async (_, _) => await ShowForecastSheetDialogAsync();

        _whatIfAnalysisMenuItem.Header = "What-If Analysis";
        _whatIfAnalysisMenuItem.Menu = CreateNativeWhatIfAnalysisMenu();

        _reviewSummaryMenuItem.Header = "Review Summary...";
        _reviewSummaryMenuItem.Click += async (_, _) => await ShowReviewSummaryDialogAsync();

        _checkAccessibilityMenuItem.Header = "Check Accessibility...";
        _checkAccessibilityMenuItem.Click += async (_, _) => await ShowReviewSummaryDialogAsync(focusAccessibility: true);

        _protectSheetMenuItem.Header = "Protect Sheet...";
        _protectSheetMenuItem.Click += async (_, _) => await ShowProtectSheetDialogAsync();

        _protectWorkbookMenuItem.Header = "Protect Workbook...";
        _protectWorkbookMenuItem.Click += async (_, _) => await ShowProtectWorkbookDialogAsync();

        _nextNoteMenuItem.Header = "Next Note";
        _nextNoteMenuItem.Click += (_, _) => NavigateReviewNote(previous: false);

        _previousNoteMenuItem.Header = "Previous Note";
        _previousNoteMenuItem.Click += (_, _) => NavigateReviewNote(previous: true);

        _nextCommentMenuItem.Header = "Next Comment";
        _nextCommentMenuItem.Click += (_, _) => NavigateReviewThreadedComment(previous: false);

        _previousCommentMenuItem.Header = "Previous Comment";
        _previousCommentMenuItem.Click += (_, _) => NavigateReviewThreadedComment(previous: true);

        _insertFunctionMenuItem.Header = "Insert Function...";
        _insertFunctionMenuItem.Gesture = new KeyGesture(Key.F3, KeyModifiers.Shift);
        _insertFunctionMenuItem.Click += (_, _) => InsertFunction();

        _nameManagerMenuItem.Header = "Name Manager...";
        _nameManagerMenuItem.Click += (_, _) => NameManager();

        _defineNameMenuItem.Header = "Define Name...";
        _defineNameMenuItem.Click += (_, _) => DefineName();

        _createNamesFromSelectionMenuItem.Header = "Create from Selection...";
        _createNamesFromSelectionMenuItem.Click += (_, _) => CreateNamesFromSelection();

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
        _boldMenuItem.Click += (_, _) => ToggleSelectedRangeBold(trackLaunchSmokeLiveCommandKey: true);

        _italicMenuItem.Header = "Italic";
        _italicMenuItem.Gesture = new KeyGesture(Key.I, KeyModifiers.Meta);
        _italicMenuItem.Click += (_, _) => ToggleSelectedRangeItalic(trackLaunchSmokeLiveCommandKey: true);

        _underlineMenuItem.Header = "Underline";
        _underlineMenuItem.Gesture = new KeyGesture(Key.U, KeyModifiers.Meta);
        _underlineMenuItem.Click += (_, _) => ToggleSelectedRangeUnderline(trackLaunchSmokeLiveCommandKey: true);

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

        _formatCellsMenuItem.Header = "Format Cells...";
        _formatCellsMenuItem.Gesture = new KeyGesture(Key.D1, KeyModifiers.Meta);
        _formatCellsMenuItem.Click += async (_, _) => await ShowFormatCellsDialogAsync();

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
        _mergeAndCenterMenuItem.Click += async (_, _) => await MergeAndCenterSelectedRangeAsync();

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

        _pageBreakPreviewMenuItem.Header = "Page Break Preview";
        _pageBreakPreviewMenuItem.ToggleType = MenuItemToggleType.CheckBox;
        _pageBreakPreviewMenuItem.Click += (_, _) => TogglePageBreakPreview();

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

        _minimizeWindowMenuItem.Header = "Minimize";
        _minimizeWindowMenuItem.Gesture = new KeyGesture(Key.M, KeyModifiers.Meta);
        _minimizeWindowMenuItem.Click += (_, _) => WindowState = WindowState.Minimized;

        _zoomWindowMenuItem.Header = "Zoom";
        _zoomWindowMenuItem.Click += (_, _) =>
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        _bringAllToFrontMenuItem.Header = "Bring All to Front";
        _bringAllToFrontMenuItem.Click += (_, _) =>
        {
            Show();
            Activate();
        };

        var fileMenu = new NativeMenu();
        fileMenu.Items.Add(_newWorkbookMenuItem);
        fileMenu.Items.Add(_openMenuItem);
        fileMenu.Items.Add(_openRecentMenuItem);
        fileMenu.Items.Add(_saveMenuItem);
        fileMenu.Items.Add(_saveAsMenuItem);
        fileMenu.Items.Add(_exportPdfMenuItem);
        fileMenu.Items.Add(_shareWorkbookMenuItem);
        fileMenu.Items.Add(_workbookStatisticsMenuItem);
        fileMenu.Items.Add(new NativeMenuItemSeparator());
        fileMenu.Items.Add(_pageSetupMenuItem);
        fileMenu.Items.Add(_printPreviewMenuItem);
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
        editMenu.Items.Add(_openHyperlinkMenuItem);
        editMenu.Items.Add(_insertHyperlinkMenuItem);
        editMenu.Items.Add(new NativeMenuItemSeparator());
        editMenu.Items.Add(_autoSumMenuItem);
        editMenu.Items.Add(_fillCellsMenuItem);
        editMenu.Items.Add(_clearMenuItem);

        var insertMenu = new NativeMenu();
        insertMenu.Items.Add(_insertColumnChartMenuItem);
        insertMenu.Items.Add(_insertBarChartMenuItem);
        insertMenu.Items.Add(_insertLineChartMenuItem);
        insertMenu.Items.Add(_insertPieChartMenuItem);
        insertMenu.Items.Add(_insertAreaChartMenuItem);
        insertMenu.Items.Add(_insertScatterChartMenuItem);
        insertMenu.Items.Add(new NativeMenuItemSeparator());
        insertMenu.Items.Add(_insertTableMenuItem);

        var dataMenu = new NativeMenu();
        dataMenu.Items.Add(_sortAscendingMenuItem);
        dataMenu.Items.Add(_sortDescendingMenuItem);
        dataMenu.Items.Add(_customSortMenuItem);
        dataMenu.Items.Add(_flashFillMenuItem);
        dataMenu.Items.Add(_advancedFilterMenuItem);
        dataMenu.Items.Add(_removeDuplicatesMenuItem);
        dataMenu.Items.Add(_subtotalMenuItem);
        dataMenu.Items.Add(new NativeMenuItemSeparator());
        dataMenu.Items.Add(_textToColumnsMenuItem);
        dataMenu.Items.Add(_consolidateMenuItem);
        dataMenu.Items.Add(new NativeMenuItemSeparator());
        dataMenu.Items.Add(_dataValidationPreviewMenuItem);
        dataMenu.Items.Add(_dataValidationMenuItem);
        dataMenu.Items.Add(new NativeMenuItemSeparator());
        dataMenu.Items.Add(_whatIfAnalysisMenuItem);
        dataMenu.Items.Add(_forecastSheetMenuItem);

        var formulasMenu = new NativeMenu();
        formulasMenu.Items.Add(_insertFunctionMenuItem);
        formulasMenu.Items.Add(new NativeMenuItemSeparator());
        formulasMenu.Items.Add(_nameManagerMenuItem);
        formulasMenu.Items.Add(_defineNameMenuItem);
        formulasMenu.Items.Add(_createNamesFromSelectionMenuItem);

        var reviewMenu = new NativeMenu();
        reviewMenu.Items.Add(_reviewSummaryMenuItem);
        reviewMenu.Items.Add(_checkAccessibilityMenuItem);
        reviewMenu.Items.Add(new NativeMenuItemSeparator());
        reviewMenu.Items.Add(_protectSheetMenuItem);
        reviewMenu.Items.Add(_protectWorkbookMenuItem);
        reviewMenu.Items.Add(new NativeMenuItemSeparator());
        reviewMenu.Items.Add(_nextNoteMenuItem);
        reviewMenu.Items.Add(_previousNoteMenuItem);
        reviewMenu.Items.Add(new NativeMenuItemSeparator());
        reviewMenu.Items.Add(_nextCommentMenuItem);
        reviewMenu.Items.Add(_previousCommentMenuItem);

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
        formatMenu.Items.Add(_formatCellsMenuItem);
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
        viewMenu.Items.Add(_pageBreakPreviewMenuItem);

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

        var windowMenu = new NativeMenu();
        windowMenu.Items.Add(_minimizeWindowMenuItem);
        windowMenu.Items.Add(_zoomWindowMenuItem);
        windowMenu.Items.Add(new NativeMenuItemSeparator());
        windowMenu.Items.Add(_bringAllToFrontMenuItem);

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
            Header = "Insert",
            Menu = insertMenu,
        });
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "Data",
            Menu = dataMenu,
        });
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "Formulas",
            Menu = formulasMenu,
        });
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "Review",
            Menu = reviewMenu,
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
            Header = "Window",
            Menu = windowMenu,
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
        AutomationProperties.SetAutomationId(_statusText, "StatusText");
        AutomationProperties.SetName(_statusText, "Status");
        AutomationProperties.SetHelpText(_statusText, "Shows the current workbook status.");

        _selectionStatsText.FontSize = 12;
        _selectionStatsText.Foreground = Brush(73, 80, 93);
        _selectionStatsText.MaxWidth = 420;
        _selectionStatsText.TextTrimming = TextTrimming.CharacterEllipsis;
        _selectionStatsText.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        AutomationProperties.SetAutomationId(_selectionStatsText, "SelectionStatsText");
        AutomationProperties.SetName(_selectionStatsText, "Selection statistics");
        AutomationProperties.SetHelpText(_selectionStatsText, "Shows statistics for the current selection.");

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
        AutomationProperties.SetAutomationId(_cellAddressText, "CellAddressText");
        AutomationProperties.SetName(_cellAddressText, "Cell address");
        AutomationProperties.SetHelpText(_cellAddressText, "Shows the active cell address.");

        _formulaBox.MinWidth = 320;
        _formulaBox.FontSize = 12;
        _formulaBox.Padding = new Thickness(8, 4);
        _formulaBox.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _formulaBox.GotFocus += FormulaBox_GotFocus;
        _formulaBox.KeyDown += FormulaBox_KeyDown;
        AutomationProperties.SetAutomationId(_formulaBox, "FormulaBox");
        AutomationProperties.SetName(_formulaBox, "Formula bar");
        AutomationProperties.SetHelpText(_formulaBox, "Edit the active cell value or formula.");

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
        RefreshPivotFieldPane();
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
        _exportPdfMenuItem.IsEnabled = isIdle && StorageProvider.CanSave;
        _shareWorkbookMenuItem.IsEnabled = isIdle;
        _workbookStatisticsMenuItem.IsEnabled = isIdle;
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
        _openHyperlinkMenuItem.IsEnabled = isIdle && _session.CanOpenSelectedHyperlink;
        _insertHyperlinkMenuItem.IsEnabled = isIdle;
        _insertColumnChartMenuItem.IsEnabled = isIdle;
        _insertBarChartMenuItem.IsEnabled = isIdle;
        _insertLineChartMenuItem.IsEnabled = isIdle;
        _insertPieChartMenuItem.IsEnabled = isIdle;
        _insertAreaChartMenuItem.IsEnabled = isIdle;
        _insertScatterChartMenuItem.IsEnabled = isIdle;
        _insertTableMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1;
        _sortAscendingMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;
        _sortDescendingMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;
        _customSortMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;
        _flashFillMenuItem.IsEnabled = isIdle;
        _advancedFilterMenuItem.IsEnabled = isIdle;
        _removeDuplicatesMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1;
        _subtotalMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1 && _session.SelectedRange.ColCount > 1;
        _textToColumnsMenuItem.IsEnabled = isIdle && _session.SelectedRange.ColCount == 1;
        _consolidateMenuItem.IsEnabled = isIdle;
        _dataValidationPreviewMenuItem.IsEnabled = isIdle;
        _dataValidationMenuItem.IsEnabled = isIdle;
        _whatIfAnalysisMenuItem.IsEnabled = isIdle;
        _goalSeekMenuItem.IsEnabled = isIdle;
        _scenarioManagerMenuItem.IsEnabled = isIdle;
        _dataTableMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1 && _session.SelectedRange.ColCount > 1;
        _forecastSheetMenuItem.IsEnabled = isIdle;
        _reviewSummaryMenuItem.IsEnabled = isIdle;
        _checkAccessibilityMenuItem.IsEnabled = isIdle;
        _protectSheetMenuItem.IsEnabled = isIdle;
        _protectWorkbookMenuItem.IsEnabled = isIdle;
        _nextNoteMenuItem.IsEnabled = isIdle;
        _previousNoteMenuItem.IsEnabled = isIdle;
        _nextCommentMenuItem.IsEnabled = isIdle;
        _previousCommentMenuItem.IsEnabled = isIdle;
        _insertFunctionMenuItem.IsEnabled = isIdle;
        _nameManagerMenuItem.IsEnabled = isIdle;
        _defineNameMenuItem.IsEnabled = isIdle;
        _createNamesFromSelectionMenuItem.IsEnabled = isIdle;
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
        _formatCellsMenuItem.IsEnabled = isIdle;
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
        _pageSetupMenuItem.IsEnabled = isIdle;
        _printPreviewMenuItem.IsEnabled = isIdle;
        _pageBreakPreviewMenuItem.IsEnabled = isIdle;
        _pageBreakPreviewMenuItem.IsChecked = _isPageBreakPreviewActive;
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
        _activeDataValidationDropdown = null;
        var viewport = _session.Viewport;
        var showHeadings = _session.ActiveSheet.ShowHeadings;
        var zoomFactor = GetActiveZoomFactor();
        var headerOffset = showHeadings ? 1 : 0;
        var cellsByAddress = viewport.Cells.ToDictionary(cell => (cell.Row, cell.Col));
        _sparklinesByCell = BuildSparklineCellLookup(_session.ActiveSheet);
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
        AddDataValidationDropdownOverlay(overlay, viewport, showHeadings, zoomFactor);

        var pageBreakOverlay = _isPageBreakPreviewActive
            ? BuildPageBreakPreviewOverlay(viewport, showHeadings, zoomFactor)
            : null;

        if (overlay.Children.Count == 0 && pageBreakOverlay is null)
            return grid;

        var composite = new AvaloniaGrid
        {
            ClipToBounds = true,
            Children = { grid },
        };
        if (pageBreakOverlay is not null)
            composite.Children.Add(pageBreakOverlay);
        if (overlay.Children.Count > 0)
            composite.Children.Add(overlay);

        return composite;
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

        // Charts live on the sheet (not projected into viewport.DrawingObjects), so paint them first —
        // before the drawing-object early-out — so a chart renders even when no other objects exist.
        AddChartOverlays(overlay, viewport);

        // Slicers and timelines are positioned drawing objects connected at the workbook level
        // (not projected into viewport.DrawingObjects), so paint them here — before the
        // drawing-object early-out — so they render even when no other objects exist.
        AddSlicerTimelineOverlays(overlay, viewport);

        if (viewport.DrawingObjects is not { Count: > 0 })
            return overlay;

        foreach (var renderPlan in DrawingObjectRenderPlanner.Plan(viewport))
        {
            var drawingObject = renderPlan.Bounds;
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

            var visual = CreateSelectableDrawingObjectVisual(renderPlan, width, height);
            Canvas.SetLeft(visual, left);
            Canvas.SetTop(visual, top);
            overlay.Children.Add(visual);
        }

        return overlay;
    }

    private void AddDataValidationDropdownOverlay(
        Canvas overlay,
        ViewportModel viewport,
        bool showHeadings,
        double zoomFactor)
    {
        if (_session.FormulaEditAddress is not null)
            return;

        if (!TryGetDisplayedCellBounds(
                viewport,
                _session.ActiveCell,
                showHeadings,
                zoomFactor,
                out var left,
                out var top,
                out var width,
                out var height))
        {
            return;
        }

        if (!DataValidationDropdownPlanner.TryPlan(
                _session.Workbook,
                _session.ActiveSheet,
                _session.ActiveCell,
                new DataValidationDropdownCellBounds(left, top, width, height),
                out var plan))
        {
            return;
        }

        var dropdown = CreateDataValidationDropdown(plan);
        Canvas.SetLeft(dropdown, plan.Bounds.Left);
        Canvas.SetTop(dropdown, plan.Bounds.Top);
        overlay.Children.Add(dropdown);
        _activeDataValidationDropdown = dropdown;
    }

    private ComboBox CreateDataValidationDropdown(DataValidationDropdownPlan plan)
    {
        var dropdown = new ComboBox
        {
            ItemsSource = plan.Items,
            SelectedItem = plan.SelectedItem,
            Width = plan.Bounds.Width,
            Height = plan.Bounds.Height,
            MinWidth = DataValidationDropdownPlanner.MinimumWidth,
            MinHeight = DataValidationDropdownPlanner.MinimumHeight,
            MaxDropDownHeight = 220,
            Padding = new Thickness(0),
            FontSize = 12,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        ToolTip.SetTip(dropdown, "Pick from list");
        AutomationProperties.SetAutomationId(dropdown, "WorksheetDataValidationDropdown");
        AutomationProperties.SetName(dropdown, "Data validation list");
        AutomationProperties.SetHelpText(dropdown, "Pick a permitted value for the active cell.");
        dropdown.SelectionChanged += DataValidationDropdown_SelectionChanged;
        return dropdown;
    }

    private Control CreateSelectableDrawingObjectVisual(
        DrawingObjectRenderPlan renderPlan,
        double width,
        double height)
    {
        var drawingObject = renderPlan.Bounds;
        var visual = CreateDrawingObjectVisual(renderPlan, width, height);
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
        DrawingObjectRenderPlan renderPlan,
        double width,
        double height)
    {
        var drawingObject = renderPlan.Bounds;
        var visual = renderPlan.PrimitiveKind switch
        {
            DrawingObjectRenderPrimitiveKind.Shape => CreateDrawingShapeVisual(drawingObject, width, height),
            DrawingObjectRenderPrimitiveKind.Image or DrawingObjectRenderPrimitiveKind.CroppedImage =>
                CreateDrawingImageVisual(renderPlan, width, height),
            DrawingObjectRenderPrimitiveKind.CellRangeSnapshot => CreateDrawingCellRangeSnapshotVisual(renderPlan, width, height),
            DrawingObjectRenderPrimitiveKind.TextBox => CreateDrawingTextBoxVisual(drawingObject, width, height),
            _ => CreateDrawingObjectBoundsMarker(drawingObject, width, height)
        };
        ApplyDrawingObjectTransform(
            visual,
            drawingObject.RotationDegrees,
            drawingObject.FlipHorizontal,
            drawingObject.FlipVertical);
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

    private static Control CreateDrawingImageVisual(
        DrawingObjectRenderPlan renderPlan,
        double width,
        double height)
    {
        var drawingObject = renderPlan.Bounds;
        if (drawingObject.ImageBytes is { Length: > 0 } imageBytes &&
            TryCreateDrawingBitmap(imageBytes, out var bitmap))
        {
            var frame = new Border
            {
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                BorderBrush = DrawingObjectBoundsBorder,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                IsHitTestVisible = false,
            };

            if (renderPlan.Crop is { } crop)
            {
                frame.Background = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.Fill,
                    SourceRect = CreateDrawingImageSourceRect(crop),
                    DestinationRect = RelativeRect.Fill,
                    TileMode = TileMode.None,
                };
                return frame;
            }

            frame.Child = new Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
            };
            return frame;
        }

        return CreateDrawingObjectBoundsMarker(drawingObject, width, height);
    }

    private static RelativeRect CreateDrawingImageSourceRect(DrawingPictureCrop crop)
    {
        var left = ClampDrawingCrop(crop.Left);
        var top = ClampDrawingCrop(crop.Top);
        var right = ClampDrawingCrop(crop.Right);
        var bottom = ClampDrawingCrop(crop.Bottom);
        return new RelativeRect(
            left,
            top,
            Math.Max(0.01, 1 - left - right),
            Math.Max(0.01, 1 - top - bottom),
            RelativeUnit.Relative);
    }

    private static double ClampDrawingCrop(double crop) => Math.Clamp(crop, 0, 0.99);

    private static Control CreateDrawingCellRangeSnapshotVisual(
        DrawingObjectRenderPlan renderPlan,
        double width,
        double height)
    {
        var drawingObject = renderPlan.Bounds;
        if (renderPlan.PictureGrid is not { } pictureGrid)
            return CreateDrawingObjectBoundsMarker(drawingObject, width, height);

        var frameWidth = Math.Max(1, width);
        var frameHeight = Math.Max(1, height);
        var canvas = new Canvas
        {
            Width = frameWidth,
            Height = frameHeight,
            ClipToBounds = true,
            IsHitTestVisible = false,
        };

        var rowCount = Math.Max(1u, pictureGrid.RowCount);
        var columnCount = Math.Max(1u, pictureGrid.ColumnCount);
        var cellWidth = frameWidth / columnCount;
        var cellHeight = frameHeight / rowCount;

        for (uint row = 1; row < rowCount; row++)
        {
            var line = new AvaloniaRectangle
            {
                Width = frameWidth,
                Height = 1,
                Fill = Brush(0xD7, 0xDE, 0xE6),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(line, 0);
            Canvas.SetTop(line, Math.Max(0, row * cellHeight));
            canvas.Children.Add(line);
        }

        for (uint column = 1; column < columnCount; column++)
        {
            var line = new AvaloniaRectangle
            {
                Width = 1,
                Height = frameHeight,
                Fill = Brush(0xD7, 0xDE, 0xE6),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(line, Math.Max(0, column * cellWidth));
            Canvas.SetTop(line, 0);
            canvas.Children.Add(line);
        }

        foreach (var cell in pictureGrid.Cells)
        {
            if (cell.RowOffset >= rowCount ||
                cell.ColumnOffset >= columnCount ||
                string.IsNullOrEmpty(cell.Text))
            {
                continue;
            }

            var text = new Border
            {
                Width = Math.Max(1, cellWidth - 6),
                Height = Math.Max(1, cellHeight - 2),
                ClipToBounds = true,
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = cell.Text,
                    FontSize = 11,
                    Foreground = Brushes.Black,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                },
            };
            Canvas.SetLeft(text, cell.ColumnOffset * cellWidth + 3);
            Canvas.SetTop(text, cell.RowOffset * cellHeight + 1);
            canvas.Children.Add(text);
        }

        return new Border
        {
            Width = frameWidth,
            Height = frameHeight,
            Background = Brushes.White,
            BorderBrush = DrawingObjectBoundsBorder,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            IsHitTestVisible = false,
            Child = canvas,
        };
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

    private static void ApplyDrawingObjectTransform(
        Control visual,
        double rotationDegrees,
        bool flipHorizontal,
        bool flipVertical)
    {
        var hasRotation = Math.Abs(rotationDegrees % 360) > 0.0001;
        if (!hasRotation && !flipHorizontal && !flipVertical)
            return;

        visual.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        if (!flipHorizontal && !flipVertical)
        {
            visual.RenderTransform = new RotateTransform(rotationDegrees);
            return;
        }

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(flipHorizontal ? -1 : 1, flipVertical ? -1 : 1));
        if (hasRotation)
            transform.Children.Add(new RotateTransform(rotationDegrees));
        visual.RenderTransform = transform;
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

    private static bool TryGetDisplayedCellBounds(
        ViewportModel viewport,
        CellAddress address,
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

        if (!TryGetDisplayedColumnLeft(viewport.ColMetrics, address.Col, zoomFactor, out var columnLeft) ||
            !TryGetDisplayedRowTop(viewport.RowMetrics, address.Row, zoomFactor, out var rowTop))
        {
            return false;
        }

        var columnMetric = viewport.ColMetrics.First(metric => metric.Col == address.Col);
        var rowMetric = viewport.RowMetrics.First(metric => metric.Row == address.Row);
        left = (showHeadings ? HeaderColumnWidth * zoomFactor : 0) + columnLeft;
        top = (showHeadings ? HeaderRowHeight * zoomFactor : 0) + rowTop;
        width = GetDisplayedColumnWidth(columnMetric, zoomFactor);
        height = GetDisplayedRowHeight(rowMetric, zoomFactor);
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

    /// <summary>
    /// Reads every sparkline on <paramref name="sheet"/> into a per-cell lookup keyed by its anchor
    /// <see cref="SparklineModel.Location"/>, using the same numeric series read as the Windows host
    /// (<see cref="SparklineRenderPlanner.BuildValues"/>). Empty series are dropped so cells without
    /// drawable data don't get an empty panel.
    /// </summary>
    private static IReadOnlyDictionary<(uint Row, uint Col), (IReadOnlyList<double> Values, SparklineKind Kind)> BuildSparklineCellLookup(Sheet sheet)
    {
        var lookup = new Dictionary<(uint Row, uint Col), (IReadOnlyList<double>, SparklineKind)>();
        if (sheet.Sparklines.Count == 0)
            return lookup;

        var values = SparklineRenderPlanner.BuildValues(sheet);
        foreach (var sparkline in sheet.Sparklines)
        {
            if (!values.TryGetValue(sparkline.Id, out var series) || series.Count == 0)
                continue;

            lookup[(sparkline.Location.Row, sparkline.Location.Col)] = (series, sparkline.Kind);
        }

        return lookup;
    }

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

        // Highlight and color-scale rules are already baked into cell.Style (fill/font) by the
        // engine, so they ride along with the background/foreground above. Data bars and icon sets
        // arrive as separate engine results on the DisplayCell; the portable planner turns each into
        // a framework-neutral render instruction that the cell content layer draws.
        var dataBar = ConditionalFormatCellRenderPlanner.PlanDataBar(cell.ConditionalDataBar);
        var icon = ConditionalFormatCellRenderPlanner.PlanIcon(cell.ConditionalIcon);

        // Sparklines live per-cell on the sheet (keyed by Location). When one anchors here, build a
        // binding-free panel that paints the series geometry behind the cell text.
        var sparklineLayer = _sparklinesByCell.TryGetValue((row, col), out var sparkline)
            ? new SparklineCellPanel(sparkline.Values, sparkline.Kind)
            : null;

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
            isNumeric,
            dataBar,
            icon,
            sparklineLayer);
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
        bool isNumeric = false,
        CfDataBarRenderInstruction? conditionalDataBar = null,
        CfIconRenderInstruction? conditionalIcon = null,
        Control? sparklineLayer = null)
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
            isNumeric,
            conditionalDataBar,
            conditionalIcon,
            sparklineLayer);
        border.Cursor = new Cursor(StandardCursorType.Hand);
        border.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(border);
            if (point.Properties.IsRightButtonPressed)
            {
                // Right-click selects the clicked cell (so the menu commands target it) and then
                // opens the worksheet cell context menu, built from the shared neutral plan.
                SelectCell(address);
                OpenWorksheetCellContextMenu(border);
                args.Handled = true;
                return;
            }

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

    /// <summary>
    /// Builds and opens the worksheet cell context menu for <paramref name="anchor"/>. The menu is
    /// produced from the platform-neutral <see cref="WorksheetContextMenuPlanner"/> (same plan WPF
    /// uses), bridged to the shared <see cref="RibbonMenu"/> model, then rendered into an Avalonia
    /// <see cref="ContextMenu"/> by <see cref="AvaloniaContextMenuRenderer"/>.
    /// </summary>
    private void OpenWorksheetCellContextMenu(Control anchor)
    {
        var menu = BuildWorksheetCellContextMenu();
        menu.Open(anchor);
    }

    /// <summary>
    /// Creates the worksheet cell <see cref="ContextMenu"/> from the shared neutral plan. The
    /// <see cref="WorksheetContextMenuState"/> is left at its default for this slice — structure is
    /// fully populated; state-driven enablement (comments/notes/hyperlinks/filter) is derivable
    /// later once the Avalonia session exposes those flags.
    /// </summary>
    private ContextMenu BuildWorksheetCellContextMenu()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(
            WorksheetContextMenuTargetKind.Worksheet,
            WorksheetContextMenuState.Default);
        var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);
        return AvaloniaContextMenuRenderer.BuildContextMenu(ribbonMenu, DispatchWorksheetContextMenuCommand);
    }

    /// <summary>
    /// Routes a worksheet context-menu command id back to the matching Avalonia document command.
    /// Actions with an existing Avalonia handler (clipboard + clear) are wired; the rest are no-ops
    /// with a TODO until their Avalonia equivalents exist.
    /// </summary>
    private void DispatchWorksheetContextMenuCommand(RibbonCommandId commandId)
    {
        if (!Enum.TryParse<WorksheetContextMenuAction>(commandId.Value, out var action))
            return;

        switch (action)
        {
            case WorksheetContextMenuAction.Cut:
                _ = CutSelectedRangeToClipboardAsync();
                break;
            case WorksheetContextMenuAction.Copy:
                _ = CopySelectedRangeToClipboardAsync();
                break;
            case WorksheetContextMenuAction.Paste:
                _ = PasteClipboardTextAsync();
                break;
            case WorksheetContextMenuAction.ClearContents:
                ClearSelectedRangeContents();
                break;
            case WorksheetContextMenuAction.ClearAll:
                ClearSelectedRangeAll();
                break;
            case WorksheetContextMenuAction.ClearFormats:
                ClearSelectedRangeFormats();
                break;
            case WorksheetContextMenuAction.ClearComments:
                ClearSelectedRangeComments();
                break;
            case WorksheetContextMenuAction.ClearHyperlinks:
                ClearSelectedRangeHyperlinks();
                break;
            default:
                // TODO: no Avalonia equivalent yet (insert/delete cells, sort/filter, comments,
                // notes, data tools, format cells, etc.). Menu structure is present; wire as the
                // corresponding Avalonia document commands land.
                break;
        }
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
        bool isNumeric = false,
        CfDataBarRenderInstruction? conditionalDataBar = null,
        CfIconRenderInstruction? conditionalIcon = null,
        Control? sparklineLayer = null)
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
            : CreateDefaultCellContent(textBlock, style, conditionalDataBar, conditionalIcon, zoomFactor, scaledIndentPadding, sparklineLayer);

        return new Border
        {
            Background = background,
            BorderBrush = selected ? SelectionBorder : showGridlines ? GridLine : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = content,
        };
    }

    private static AvaloniaGrid CreateDefaultCellContent(
        TextBlock textBlock,
        CellStyle? style,
        CfDataBarRenderInstruction? conditionalDataBar = null,
        CfIconRenderInstruction? conditionalIcon = null,
        double zoomFactor = 1,
        double scaledIndentPadding = 0,
        Control? sparklineLayer = null)
    {
        var content = new AvaloniaGrid { ClipToBounds = true };

        // Sparklines and data bars render behind the text; add them first so they sit at the bottom
        // of the z-order.
        if (sparklineLayer is not null)
            content.Children.Add(sparklineLayer);

        // Data bars render behind the text; add them first so they sit at the bottom of the z-order.
        if (conditionalDataBar is { } bar)
            content.Children.Add(CreateConditionalDataBarLayer(bar, zoomFactor));

        // Icon-set glyphs occupy a left gutter and push the cell text right by the gutter width.
        if (conditionalIcon is { } icon)
        {
            var gutter = icon.TextGutter * zoomFactor;
            if (gutter > 0)
            {
                var existing = textBlock.Margin;
                textBlock.Margin = new Thickness(
                    Math.Max(existing.Left, gutter + scaledIndentPadding),
                    existing.Top,
                    existing.Right,
                    existing.Bottom);
            }
        }

        content.Children.Add(textBlock);

        if (conditionalIcon is { } iconGlyph)
            content.Children.Add(CreateConditionalIconLayer(iconGlyph, zoomFactor));

        AddStyledCellBorderOverlay(content, style);
        return content;
    }

    private static Control CreateConditionalDataBarLayer(CfDataBarRenderInstruction bar, double zoomFactor)
    {
        var horizontalInset = bar.HorizontalInset * zoomFactor;
        var verticalInset = bar.VerticalInset * zoomFactor;
        var color = Color.FromRgb(bar.FillColor.R, bar.FillColor.G, bar.FillColor.B);

        IBrush fill = bar.Gradient
            ? new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(90, color.R, color.G, color.B), 0),
                    new GradientStop(color, 1),
                },
            }
            : new SolidColorBrush(color);

        var rectangle = new AvaloniaRectangle
        {
            Fill = fill,
            // Horizontal extent is set by the hosting panel at arrange time; the rectangle only
            // applies the vertical inset so the bar is shorter than the cell.
            Margin = new Thickness(0, verticalInset, 0, verticalInset),
            IsHitTestVisible = false,
        };
        if (bar.Border)
        {
            rectangle.Stroke = new SolidColorBrush(color);
            rectangle.StrokeThickness = 0.75 * zoomFactor;
        }

        // Width is resolved relative to the cell's drawable content area via a binding-free
        // panel that places the fraction-sized rectangle at arrange time.
        return new ConditionalDataBarPanel(rectangle, bar.StartFraction, bar.FractionWidth, horizontalInset);
    }

    private static Control CreateConditionalIconLayer(CfIconRenderInstruction icon, double zoomFactor)
    {
        const double iconSize = 10d;
        const double iconLeftInset = 4d;
        var size = iconSize * zoomFactor;
        var glyph = ConditionalFormatIconGlyphFactory.Create(icon, size);
        return new Border
        {
            Width = (iconLeftInset + iconSize) * zoomFactor,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            VerticalAlignment = AvaloniaVerticalAlignment.Stretch,
            Padding = new Thickness(iconLeftInset * zoomFactor, 0, 0, 0),
            IsHitTestVisible = false,
            Child = new Border
            {
                Width = size,
                Height = size,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                Child = glyph,
            },
        };
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

    private NativeMenu CreateNativeWhatIfAnalysisMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(_goalSeekMenuItem);
        menu.Items.Add(_scenarioManagerMenuItem);
        menu.Items.Add(_dataTableMenuItem);
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

        FocusFirstEnabledSheetTabMenuItem(items);
    }

    private static void FocusFirstEnabledSheetTabMenuItem(IEnumerable<Control> items)
    {
        foreach (var item in items)
        {
            if (item is MenuItem { IsEnabled: true } menuItem)
            {
                menuItem.Focus();
                return;
            }
        }
    }

    private Button? FindSheetTabButton(SheetId sheetId)
    {
        if (_sheetTabsHost.Content is not StackPanel panel)
            return null;

        foreach (var child in panel.Children)
        {
            if (child is Button button &&
                button.Tag is SheetId tag &&
                tag == sheetId)
            {
                return button;
            }
        }

        return null;
    }

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

    private async Task OpenSelectedHyperlinkAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        if (!_session.TryGetSelectedHyperlinkPlan(out var plan) || plan is null)
        {
            ShowEditIssue("Hyperlink target was not found.");
            return;
        }

        if (plan.Kind == HyperlinkNavigationKind.External)
        {
            await OpenExternalHyperlinkAsync(plan.Target);
            return;
        }

        if (plan.Kind == HyperlinkNavigationKind.LocalFile)
        {
            await OpenLocalFileHyperlinkAsync(plan);
            return;
        }

        var result = _session.OpenSelectedHyperlink();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Open Hyperlink failed.");
            return;
        }

        RefreshShell($"Selected {FormatRangeReference(result.SelectedRange!.Value)}");
    }

    private async Task OpenLocalFileHyperlinkAsync(HyperlinkNavigationPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.LocalPath))
        {
            ShowEditIssue("Open Hyperlink requires a local file path.");
            return;
        }

        if (!_session.TryResolveOpenTarget(plan.LocalPath, out var target, out var message) ||
            target is null)
        {
            ShowEditIssue(string.IsNullOrWhiteSpace(message)
                ? "Open Hyperlink requires a supported workbook file."
                : message);
            return;
        }

        await OpenWorkbookPathAsync(target.Path);
    }

    private async Task OpenExternalHyperlinkAsync(string target)
    {
        var result = await OpenExternalUriAsync(target);
        switch (result)
        {
            case ExternalUriLaunchResult.Launched:
                return;
            case ExternalUriLaunchResult.BlockedScheme:
                ShowEditIssue("Open Hyperlink blocked this link because its scheme is not supported.");
                return;
            case ExternalUriLaunchResult.LauncherUnavailable:
                ShowEditIssue("Open Hyperlink cannot open external links on this platform.");
                return;
            case ExternalUriLaunchResult.LaunchFailed:
            default:
                ShowEditIssue("Open Hyperlink could not open the link.");
                return;
        }
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

    private async Task ShowInsertHyperlinkDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var plan = await ShowInsertHyperlinkInputDialogAsync();
        if (plan is null)
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeHyperlink(plan);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Insert Hyperlink failed.");
            return;
        }

        RefreshShell($"Inserted hyperlink for {rangeReference}");
    }

    private async Task<HyperlinkDialogPlan?> ShowInsertHyperlinkInputDialogAsync()
    {
        HyperlinkDialogPlan? result = null;
        var prefill = _session.GetSelectedRangeHyperlinkDialogPrefill();
        var linkTypeChoices = CreateHyperlinkTypeChoices();
        var selectedLinkType = linkTypeChoices[0];
        foreach (var choice in linkTypeChoices)
        {
            if (choice.Value != prefill.LinkType)
                continue;

            selectedLinkType = choice;
            break;
        }

        var dialog = new Window
        {
            Title = "Insert Hyperlink",
            Width = 460,
            Height = 420,
            MinWidth = 400,
            MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "HyperlinkCompactDialog");

        var linkTypeBox = new ComboBox
        {
            ItemsSource = linkTypeChoices,
            SelectedItem = selectedLinkType,
            MinWidth = 300,
        };
        AutomationProperties.SetName(linkTypeBox, "Link type");
        AutomationProperties.SetAutomationId(linkTypeBox, "HyperlinkLinkTypeBox");

        var displayBox = new TextBox
        {
            Text = prefill.DisplayText,
            MinWidth = 300,
        };
        AutomationProperties.SetName(displayBox, "Text to display");
        AutomationProperties.SetAutomationId(displayBox, "HyperlinkDisplayTextBox");

        var targetLabel = new TextBlock();
        var targetBox = new TextBox
        {
            Text = prefill.Target,
            MinWidth = 300,
        };
        AutomationProperties.SetAutomationId(targetBox, "HyperlinkTargetTextBox");

        var screenTipBox = new TextBox
        {
            Text = prefill.ScreenTip,
            MinWidth = 300,
        };
        AutomationProperties.SetName(screenTipBox, "Screen tip");
        AutomationProperties.SetAutomationId(screenTipBox, "HyperlinkScreenTipTextBox");

        var bookmarkBox = new TextBox
        {
            Text = prefill.Bookmark,
            MinWidth = 300,
        };
        AutomationProperties.SetName(bookmarkBox, "Bookmark");
        AutomationProperties.SetAutomationId(bookmarkBox, "HyperlinkBookmarkTextBox");

        var validationText = new TextBlock
        {
            MinHeight = 20,
            Foreground = Brush(143, 74, 18),
        };
        AutomationProperties.SetAutomationId(validationText, "HyperlinkValidationText");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(okButton, "HyperlinkOkButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(cancelButton, "HyperlinkCancelButton");

        HyperlinkTargetKind CurrentLinkType() =>
            linkTypeBox.SelectedItem is SortDialogComboItem<HyperlinkTargetKind> choice
                ? choice.Value
                : HyperlinkTargetKind.ExistingFileOrWebPage;

        void RefreshTargetField()
        {
            var linkType = CurrentLinkType();
            targetLabel.Text = GetHyperlinkTargetLabel(linkType);
            AutomationProperties.SetName(targetBox, targetLabel.Text);
            AutomationProperties.SetHelpText(targetBox, GetHyperlinkTargetHelpText(linkType));
        }

        void Accept()
        {
            if (!HyperlinkDialogPlanner.TryPlan(
                    targetBox.Text,
                    displayBox.Text,
                    CurrentLinkType(),
                    screenTipBox.Text,
                    bookmarkBox.Text,
                    out var plan,
                    out var validationError))
            {
                validationText.Text = GetHyperlinkValidationErrorText(validationError);
                targetBox.Focus();
                targetBox.SelectAll();
                return;
            }

            result = plan;
            dialog.Close();
        }

        linkTypeBox.SelectionChanged += (_, _) => RefreshTargetField();
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
                new TextBlock { Text = "Link type" },
                linkTypeBox,
                new TextBlock { Text = "Text to display" },
                displayBox,
                targetLabel,
                targetBox,
                new TextBlock { Text = "Screen tip" },
                screenTipBox,
                new TextBlock { Text = "Bookmark" },
                bookmarkBox,
                validationText,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) =>
        {
            RefreshTargetField();
            targetBox.Focus();
            targetBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private static SortDialogComboItem<HyperlinkTargetKind>[] CreateHyperlinkTypeChoices() =>
    [
        new("Existing File or Web Page", HyperlinkTargetKind.ExistingFileOrWebPage),
        new("Place in This Document", HyperlinkTargetKind.PlaceInThisDocument),
        new("Create New Document", HyperlinkTargetKind.CreateNewDocument),
        new("Email Address", HyperlinkTargetKind.EmailAddress),
    ];

    private static string GetHyperlinkTargetLabel(HyperlinkTargetKind linkType) =>
        linkType switch
        {
            HyperlinkTargetKind.PlaceInThisDocument => "Cell reference or defined name",
            HyperlinkTargetKind.CreateNewDocument => "New document name",
            HyperlinkTargetKind.EmailAddress => "Email address",
            _ => "Address"
        };

    private static string GetHyperlinkTargetHelpText(HyperlinkTargetKind linkType) =>
        linkType switch
        {
            HyperlinkTargetKind.PlaceInThisDocument => "Enter a workbook location such as Sheet1!A1.",
            HyperlinkTargetKind.CreateNewDocument => "Enter the document name to store with this hyperlink.",
            HyperlinkTargetKind.EmailAddress => "Enter an email address or mailto link.",
            _ => "Enter a web page or file address."
        };

    private static string GetHyperlinkValidationErrorText(HyperlinkDialogValidationError error) =>
        error switch
        {
            HyperlinkDialogValidationError.MissingDocumentLocation => "Enter a cell reference or defined name.",
            HyperlinkDialogValidationError.MissingEmailAddress => "Enter an email address.",
            HyperlinkDialogValidationError.MissingNewDocumentName => "Enter a new document name.",
            HyperlinkDialogValidationError.InvalidEmailAddress => "Enter a valid email address.",
            _ => "Enter an address."
        };

    private async Task ShowWorkbookStatisticsDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var statistics = WorkbookStatisticsService.GetStatistics(_session.Workbook);
        var dialog = new Window
        {
            Title = "Workbook Statistics",
            Width = 380,
            Height = 320,
            MinWidth = 340,
            MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "WorkbookStatisticsDialog");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        AutomationProperties.SetName(okButton, "OK");
        AutomationProperties.SetAutomationId(okButton, "WorkbookStatisticsOkButton");
        AutomationProperties.SetHelpText(okButton, "Close workbook statistics.");

        okButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        dialog.Content = CreateWorkbookStatisticsDialogContent(statistics, okButton);
        dialog.Opened += (_, _) => okButton.Focus();
        await dialog.ShowDialog(this);
    }

    private static Control CreateWorkbookStatisticsDialogContent(WorkbookStatistics statistics, Button okButton)
    {
        var statisticsBlock = new TextBlock
        {
            Text = FormatWorkbookStatistics(statistics),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
        };
        AutomationProperties.SetName(statisticsBlock, "Workbook Statistics");
        AutomationProperties.SetAutomationId(statisticsBlock, "WorkbookStatisticsSummary");
        AutomationProperties.SetHelpText(statisticsBlock, "Summarizes sheet, cell, formula, comment, and object counts for the workbook.");

        var root = new DockPanel
        {
            Margin = new Thickness(16),
        };
        DockPanel.SetDock(okButton, Dock.Bottom);
        root.Children.Add(okButton);
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = statisticsBlock,
        });

        return root;
    }

    private static string FormatWorkbookStatistics(WorkbookStatistics statistics) =>
        string.Join(Environment.NewLine,
            $"Sheets: {statistics.WorksheetCount}",
            $"Cells with data: {statistics.CellCount}",
            $"Formulas: {statistics.FormulaCount}",
            $"Comments: {statistics.CommentCount}",
            $"Charts: {statistics.ChartCount}",
            $"Pictures: {statistics.PictureCount}",
            $"Shapes and text boxes: {statistics.ShapeCount}",
            $"Named ranges: {statistics.NamedRangeCount}");

    private async Task ShowReviewSummaryDialogAsync(bool focusAccessibility = false)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var plan = _session.GetReviewWorkflowPlan();
        var dialog = new Window
        {
            Title = focusAccessibility ? "Accessibility Check" : "Review Summary",
            Width = 640,
            Height = 520,
            MinWidth = 520,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ReviewSummaryDialog");

        var summaryBlock = new TextBlock
        {
            Text = FormatReviewWorkflowSummary(plan),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
        };
        AutomationProperties.SetName(summaryBlock, "Review Summary");
        AutomationProperties.SetAutomationId(summaryBlock, "ReviewSummaryText");
        AutomationProperties.SetHelpText(summaryBlock, "Summarizes workbook statistics and review item counts.");

        var spellingList = CreateReviewPreviewList(
            "Spelling issues",
            "ReviewSpellingIssuesList",
            FormatReviewSpellingIssues(plan.SpellingIssues));
        var accessibilityList = CreateReviewPreviewList(
            "Accessibility issues",
            "ReviewAccessibilityIssuesList",
            FormatReviewAccessibilityIssues(plan.AccessibilityIssues));
        var notesList = CreateReviewPreviewList(
            "Notes",
            "ReviewNotesList",
            FormatReviewCommentItems(plan.Notes, "No notes on the active sheet."));
        var commentsList = CreateReviewPreviewList(
            "Threaded comments",
            "ReviewCommentsList",
            FormatReviewCommentItems(plan.ThreadedComments, "No threaded comments on the active sheet."));

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        AutomationProperties.SetName(closeButton, "Close");
        AutomationProperties.SetAutomationId(closeButton, "ReviewCloseButton");
        AutomationProperties.SetHelpText(closeButton, "Close review summary.");
        closeButton.Click += (_, _) => dialog.Close();

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
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
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                closeButton,
            },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            summaryBlock,
                            CreateReviewPreviewSection("Spelling", spellingList),
                            CreateReviewPreviewSection("Accessibility", accessibilityList),
                            CreateReviewPreviewSection("Notes", notesList),
                            CreateReviewPreviewSection("Comments", commentsList),
                        },
                    },
                },
            },
        };
        dialog.Opened += (_, _) =>
        {
            if (focusAccessibility)
                accessibilityList.Focus();
            else
                closeButton.Focus();
        };

        await dialog.ShowDialog(this);
    }

    private void NavigateReviewNote(bool previous) =>
        NavigateReviewTarget(
            () => _session.GoToNextNote(previous: previous),
            previous ? "previous note" : "next note",
            previous ? "Previous note was not found." : "Next note was not found.");

    private void NavigateReviewThreadedComment(bool previous) =>
        NavigateReviewTarget(
            () => _session.GoToNextThreadedComment(previous: previous),
            previous ? "previous comment" : "next comment",
            previous ? "Previous threaded comment was not found." : "Next threaded comment was not found.");

    private void NavigateReviewTarget(
        Func<WorkbookNavigationResult> navigate,
        string statusLabel,
        string fallbackMessage)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var result = navigate();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? fallbackMessage);
            return;
        }

        if (result.SelectedRange is not { } selectedRange)
        {
            ShowEditIssue("Review target was not selected.");
            return;
        }

        RefreshShell($"Selected {FormatRangeReference(selectedRange)} ({statusLabel})");
    }

    private static string FormatReviewWorkflowSummary(ReviewWorkflowPlan plan)
    {
        var statistics = plan.Statistics;
        return string.Join(Environment.NewLine,
            $"Sheets: {statistics.WorksheetCount}",
            $"Cells with data: {statistics.CellCount}",
            $"Formulas: {statistics.FormulaCount}",
            $"Workbook comments: {statistics.CommentCount}",
            $"Charts: {statistics.ChartCount}",
            $"Pictures: {statistics.PictureCount}",
            $"Shapes and text boxes: {statistics.ShapeCount}",
            $"Named ranges: {statistics.NamedRangeCount}",
            "",
            $"Spelling issues: {plan.SpellingIssues.Count}",
            $"Accessibility issues: {plan.AccessibilityIssues.Count}",
            $"Notes on active sheet: {plan.Notes.Count}",
            $"Threaded comments on active sheet: {plan.ThreadedComments.Count}");
    }

    private static StackPanel CreateReviewPreviewSection(string header, ListBox list) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = header,
                    FontWeight = FontWeight.SemiBold,
                },
                list,
            },
        };

    private static ListBox CreateReviewPreviewList(
        string name,
        string automationId,
        IReadOnlyList<string> items)
    {
        var list = new ListBox
        {
            ItemsSource = items,
            MinHeight = 56,
            MaxHeight = 96,
        };
        AutomationProperties.SetName(list, name);
        AutomationProperties.SetAutomationId(list, automationId);
        return list;
    }

    private static IReadOnlyList<string> FormatReviewSpellingIssues(IReadOnlyList<SpellingIssue> issues) =>
        CreateReviewPreviewItems(
            issues,
            issue =>
            {
                var suggestion = string.IsNullOrWhiteSpace(issue.Suggestion) ? "no suggestion" : issue.Suggestion;
                return $"{FormatCellReference(issue.Address)}: {issue.Word} -> {suggestion} ({FormatSpellingIssueSource(issue.Source)})";
            },
            "No spelling issues.");

    private static IReadOnlyList<string> FormatReviewAccessibilityIssues(IReadOnlyList<AccessibilityIssue> issues) =>
        CreateReviewPreviewItems(
            issues,
            issue => $"{TrimReviewPreview(issue.SheetName)}!{TrimReviewPreview(issue.Location)}: {TrimReviewPreview(issue.Message)}",
            "No accessibility issues.");

    private static IReadOnlyList<string> FormatReviewCommentItems(
        IReadOnlyList<ReviewCommentListItem> items,
        string emptyMessage) =>
        CreateReviewPreviewItems(
            items,
            item => $"{FormatCellReference(item.Address)}: {TrimReviewPreview(item.PreviewText)}",
            emptyMessage);

    private static IReadOnlyList<string> CreateReviewPreviewItems<T>(
        IReadOnlyList<T> items,
        Func<T, string> format,
        string emptyMessage)
    {
        if (items.Count == 0)
            return [emptyMessage];

        const int previewLimit = 6;
        var preview = items
            .Take(previewLimit)
            .Select(format)
            .ToList();
        if (items.Count > preview.Count)
            preview.Add($"... and {items.Count - preview.Count} more");

        return preview;
    }

    private static string FormatSpellingIssueSource(SpellingIssueSource source) =>
        source switch
        {
            SpellingIssueSource.CellText => "cell text",
            SpellingIssueSource.Note => "note",
            SpellingIssueSource.ThreadedComment => "threaded comment",
            SpellingIssueSource.ThreadedCommentReply => "threaded reply",
            _ => "spelling"
        };

    private static string TrimReviewPreview(string text)
    {
        var normalized = string.Join(
            " ",
            text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "(blank)";

        const int maxLength = 96;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..(maxLength - 3)] + "...";
    }

    private async Task ShowFormatCellsDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var selection = await ShowFormatCellsInputDialogAsync();
        if (selection is null)
            return;

        if (!FormatCellsCompactPlanner.TryPlan(selection.Request, out var diff, out var errorMessage))
        {
            ShowEditIssue(errorMessage);
            return;
        }

        ClearSelectedDrawingObject();
        var range = _session.SelectedRange;
        var mergeContentResolution = MergeCellContentResolution.KeepFirstCell;
        if (selection.Request.MergeCells == true)
        {
            var contentPlan = CellMergePlanner.AnalyzeContent(_session.ActiveSheet, range);
            if (contentPlan.WouldLoseContent)
            {
                var choice = await ShowMergeCellsContentWarningDialogAsync(contentPlan);
                if (choice == MergeCellsWarningChoice.Cancel)
                {
                    RefreshShell(_statusText.Text ?? "Ready");
                    return;
                }

                mergeContentResolution = choice == MergeCellsWarningChoice.ConcatenateAllCells
                    ? MergeCellContentResolution.ConcatenateAllCells
                    : MergeCellContentResolution.KeepFirstCell;
            }
        }

        var rangeReference = FormatRangeReference(range);
        var result = _session.ApplySelectedRangeCompactFormat(
            diff,
            selection.BorderPreset,
            selection.BorderStyle,
            selection.BorderColor,
            selection.Request.MergeCells,
            mergeContentResolution);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Format Cells failed.");
            return;
        }

        RefreshShell($"Formatted {rangeReference}");
    }

    private async Task<FormatCellsDialogResult?> ShowFormatCellsInputDialogAsync(
        Action<FormatCellsDialogSmokeProbe>? launchSmokeProbe = null)
    {
        FormatCellsDialogResult? result = null;
        var currentNumberFormat = _session.SelectedRangeStartNumberFormat;
        var currentHorizontalAlignment = _session.SelectedRangeStartHorizontalAlignment;
        var currentVerticalAlignment = _session.SelectedRangeStartVerticalAlignment;
        var currentMergeCells = _session.IsSelectedRangeMerged;
        var currentFontSize = _session.SelectedRangeStartFontSize;
        var currentStyle = _session.CreateFormatDiffFromActiveCell() ?? StyleDiff.FromStyle(CellStyle.Default);
        var currentUnderline = currentStyle.Underline ?? CellStyle.Default.Underline;
        var currentDoubleUnderline = currentStyle.DoubleUnderline ?? _session.IsSelectedRangeStartDoubleUnderline;
        var currentShrinkToFit = currentStyle.ShrinkToFit ?? CellStyle.Default.ShrinkToFit;
        var currentIndentLevel = currentStyle.IndentLevel ?? _session.SelectedRangeStartIndentLevel;
        var currentTextRotation = currentStyle.TextRotation ?? _session.SelectedRangeStartTextRotation;
        var currentFontName = currentStyle.FontName ?? CellStyle.Default.FontName;
        var currentLocked = currentStyle.Locked ?? CellStyle.Default.Locked;
        var currentHidden = currentStyle.Hidden ?? CellStyle.Default.Hidden;
        var currentSuperscript = currentStyle.Superscript ?? CellStyle.Default.Superscript;
        var currentSubscript = currentStyle.Subscript ?? CellStyle.Default.Subscript;
        var currentFillPatternStyle = currentStyle.FillPatternStyle ?? CellStyle.Default.FillPatternStyle;

        var dialog = new Window
        {
            Title = "Format Cells",
            Width = 560,
            Height = 560,
            MinWidth = 480,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "FormatCellsCompactDialog");

        var numberChoices = CreateFormatCellsNumberFormatChoices(currentNumberFormat);
        var currentNumberChoice =
            FindFormatCellsNumberFormatChoice(numberChoices, currentNumberFormat) ?? numberChoices[0];
        var numberCategoryList = new ListBox
        {
            ItemsSource = numberChoices.Select(choice => choice.Category).Distinct().ToArray(),
            SelectedItem = currentNumberChoice.Category,
            MinWidth = 150,
            MaxHeight = 180,
        };
        AutomationProperties.SetName(numberCategoryList, "Category");
        AutomationProperties.SetAutomationId(numberCategoryList, "FormatCellsNumberCategoryList");

        var numberFormatBox = new ComboBox
        {
            MinWidth = 260,
        };
        AutomationProperties.SetName(numberFormatBox, "Type");
        AutomationProperties.SetAutomationId(numberFormatBox, "FormatCellsNumberFormatCombo");
        AutomationProperties.SetAutomationId(numberFormatBox, "FormatCellsNumberFormatBox");

        var numberPreview = new TextBlock
        {
            Text = currentNumberChoice.Preview,
            MinHeight = 28,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(numberPreview, "FormatCellsNumberPreview");

        void RefreshNumberChoices()
        {
            var category = numberCategoryList.SelectedItem?.ToString() ?? currentNumberChoice.Category;
            var filteredChoices = FilterFormatCellsNumberFormatChoices(numberChoices, category);
            numberFormatBox.ItemsSource = filteredChoices;
            if (numberFormatBox.SelectedItem is not FormatCellsNumberFormatChoice selected ||
                !ContainsFormatCellsNumberFormatChoice(filteredChoices, selected))
            {
                numberFormatBox.SelectedItem = filteredChoices.Count > 0 ? filteredChoices[0] : currentNumberChoice;
            }
        }

        numberCategoryList.SelectionChanged += (_, _) => RefreshNumberChoices();
        numberFormatBox.SelectionChanged += (_, _) =>
        {
            if (numberFormatBox.SelectedItem is FormatCellsNumberFormatChoice choice)
                numberPreview.Text = choice.Preview;
        };
        RefreshNumberChoices();
        numberFormatBox.SelectedItem = currentNumberChoice;

        var horizontalAlignmentBox = CreateFormatCellsComboBox(
            "FormatCellsHorizontalAlignmentBox",
            CreateFormatCellsHorizontalAlignmentChoices(),
            currentHorizontalAlignment);
        var verticalAlignmentBox = CreateFormatCellsComboBox(
            "FormatCellsVerticalAlignmentBox",
            CreateFormatCellsVerticalAlignmentChoices(),
            currentVerticalAlignment);
        var wrapTextBox = CreateFormatCellsCheckBox("Wrap text", "FormatCellsWrapTextBox", _session.IsSelectedRangeStartWrapText);
        var shrinkToFitBox = CreateFormatCellsCheckBox("Shrink to fit", "FormatCellsShrinkToFitBox", currentShrinkToFit);
        var mergeCellsBox = CreateFormatCellsCheckBox("Merge cells", "FormatCellsMergeCellsBox", currentMergeCells);
        var indentLevelBox = new TextBox
        {
            Text = currentIndentLevel.ToString(CultureInfo.InvariantCulture),
            MinWidth = 100,
        };
        AutomationProperties.SetName(indentLevelBox, "Indent level");
        AutomationProperties.SetAutomationId(indentLevelBox, "FormatCellsIndentLevelBox");

        var textRotationBox = new TextBox
        {
            Text = currentTextRotation.ToString(CultureInfo.InvariantCulture),
            MinWidth = 100,
        };
        AutomationProperties.SetName(textRotationBox, "Text rotation");
        AutomationProperties.SetAutomationId(textRotationBox, "FormatCellsTextRotationBox");

        var boldBox = CreateFormatCellsCheckBox("Bold", "FormatCellsBoldBox", _session.IsSelectedRangeStartBold);
        var italicBox = CreateFormatCellsCheckBox("Italic", "FormatCellsItalicBox", _session.IsSelectedRangeStartItalic);
        var underlineBox = CreateFormatCellsCheckBox("Underline", "FormatCellsUnderlineBox", currentUnderline);
        var doubleUnderlineBox = CreateFormatCellsCheckBox("Double underline", "FormatCellsDoubleUnderlineBox", currentDoubleUnderline);
        var strikethroughBox = CreateFormatCellsCheckBox("Strikethrough", "FormatCellsStrikethroughBox", _session.IsSelectedRangeStartStrikethrough);
        var superscriptBox = CreateFormatCellsCheckBox("Superscript", "FormatCellsSuperscriptBox", currentSuperscript);
        var subscriptBox = CreateFormatCellsCheckBox("Subscript", "FormatCellsSubscriptBox", currentSubscript);
        superscriptBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty && superscriptBox.IsChecked == true)
                subscriptBox.IsChecked = false;
        };
        subscriptBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty && subscriptBox.IsChecked == true)
                superscriptBox.IsChecked = false;
        };

        var fontNameBox = new TextBox
        {
            Text = currentFontName,
            MinWidth = 180,
        };
        AutomationProperties.SetName(fontNameBox, "Font");
        AutomationProperties.SetAutomationId(fontNameBox, "FormatCellsFontNameBox");

        var fontSizeBox = new TextBox
        {
            Text = currentFontSize.ToString("0.##", CultureInfo.InvariantCulture),
            MinWidth = 100,
        };
        AutomationProperties.SetName(fontSizeBox, "Size");
        AutomationProperties.SetAutomationId(fontSizeBox, "FormatCellsFontSizeBox");

        var fontColorBox = new ComboBox
        {
            ItemsSource = CreateFormatCellsColorChoices(includeClear: false),
            SelectedIndex = 0,
            MinWidth = 180,
        };
        AutomationProperties.SetName(fontColorBox, "Font color");
        AutomationProperties.SetAutomationId(fontColorBox, "FormatCellsFontColorBox");
        var normalFontBox = CreateFormatCellsCheckBox("Normal font", "FormatCellsNormalFontBox", false);
        normalFontBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != ToggleButton.IsCheckedProperty || normalFontBox.IsChecked != true)
                return;

            var normal = CellStyle.Default;
            fontNameBox.Text = normal.FontName;
            fontSizeBox.Text = normal.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
            boldBox.IsChecked = normal.Bold;
            italicBox.IsChecked = normal.Italic;
            underlineBox.IsChecked = normal.Underline;
            doubleUnderlineBox.IsChecked = normal.DoubleUnderline;
            strikethroughBox.IsChecked = normal.Strikethrough;
            superscriptBox.IsChecked = normal.Superscript;
            subscriptBox.IsChecked = normal.Subscript;
            SelectFormatCellsColor(fontColorBox, normal.FontColor);
        };

        var fillColorBox = new ComboBox
        {
            ItemsSource = CreateFormatCellsColorChoices(includeClear: true),
            SelectedIndex = 0,
            MinWidth = 180,
        };
        AutomationProperties.SetName(fillColorBox, "Fill color");
        AutomationProperties.SetAutomationId(fillColorBox, "FormatCellsFillColorBox");
        var fillPatternStyleBox = CreateFormatCellsComboBox(
            "FormatCellsFillPatternStyleBox",
            CreateFormatCellsFillPatternStyleChoices(),
            currentFillPatternStyle);
        var fillPatternColorBox = new ComboBox
        {
            ItemsSource = CreateFormatCellsColorChoices(includeClear: false),
            SelectedIndex = 0,
            MinWidth = 180,
        };
        AutomationProperties.SetName(fillPatternColorBox, "Pattern color");
        AutomationProperties.SetAutomationId(fillPatternColorBox, "FormatCellsFillPatternColorBox");

        var borderPresetBox = new ComboBox
        {
            ItemsSource = CreateFormatCellsBorderPresetChoices(),
            SelectedIndex = 0,
            MinWidth = 220,
        };
        AutomationProperties.SetName(borderPresetBox, "Border preset");
        AutomationProperties.SetAutomationId(borderPresetBox, "FormatCellsBorderPresetBox");
        var borderStyleBox = CreateFormatCellsComboBox(
            "FormatCellsBorderStyleBox",
            CreateFormatCellsBorderStyleChoices(),
            BorderStyle.Thin);
        var borderColorBox = new ComboBox
        {
            ItemsSource = CreateFormatCellsColorChoices(includeClear: false),
            SelectedIndex = 0,
            MinWidth = 180,
        };
        AutomationProperties.SetName(borderColorBox, "Border color");
        AutomationProperties.SetAutomationId(borderColorBox, "FormatCellsBorderColorBox");

        var lockedBox = CreateFormatCellsCheckBox("Locked", "FormatCellsLockedBox", currentLocked);
        var hiddenBox = CreateFormatCellsCheckBox("Hidden", "FormatCellsHiddenBox", currentHidden);
        var protectionExplanationText = new TextBlock
        {
            Text = "Locking cells or hiding formulas has no effect until you protect the worksheet.",
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(protectionExplanationText, "FormatCellsProtectionExplanationText");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(okButton, "FormatCellsOkButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(cancelButton, "FormatCellsCancelButton");

        var errorText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
        };

        void Accept()
        {
            var normalFont = normalFontBox.IsChecked == true;
            var normalStyle = CellStyle.Default;
            string message;
            double? fontSize;
            if (normalFont)
            {
                fontSize = normalStyle.FontSize;
            }
            else if (!TryReadFormatCellsFontSize(fontSizeBox.Text, currentFontSize, out fontSize, out message))
            {
                errorText.Text = message;
                return;
            }
            if (!TryReadFormatCellsIndentLevel(indentLevelBox.Text, currentIndentLevel, out var indentLevel, out message))
            {
                errorText.Text = message;
                return;
            }
            if (!TryReadFormatCellsTextRotation(textRotationBox.Text, currentTextRotation, out var textRotation, out message))
            {
                errorText.Text = message;
                return;
            }

            var numberFormat = numberFormatBox.SelectedItem is FormatCellsNumberFormatChoice numberChoice &&
                !string.Equals(numberChoice.FormatCode, currentNumberFormat, StringComparison.Ordinal)
                    ? numberChoice.FormatCode
                    : null;
            var fillChoice = fillColorBox.SelectedItem as FormatCellsColorChoice;
            var clearFill = fillChoice?.Clear == true;
            var borderChoice = borderPresetBox.SelectedItem as FormatCellsNullableChoice<CellBorderPreset>;
            var borderStyle = borderStyleBox.SelectedItem is FormatCellsNullableChoice<BorderStyle> { Value: { } selectedBorderStyle }
                ? selectedBorderStyle
                : BorderStyle.Thin;
            var borderColor = (borderColorBox.SelectedItem as FormatCellsColorChoice)?.Color;
            var request = new FormatCellsCompactRequest(
                NumberFormat: numberFormat,
                HorizontalAlignment: ReadChangedFormatCellsValue(currentHorizontalAlignment, horizontalAlignmentBox),
                VerticalAlignment: ReadChangedFormatCellsValue(currentVerticalAlignment, verticalAlignmentBox),
                WrapText: ReadChangedFormatCellsBool(_session.IsSelectedRangeStartWrapText, wrapTextBox),
                Bold: normalFont ? normalStyle.Bold : ReadChangedFormatCellsBool(_session.IsSelectedRangeStartBold, boldBox),
                Italic: normalFont ? normalStyle.Italic : ReadChangedFormatCellsBool(_session.IsSelectedRangeStartItalic, italicBox),
                Underline: normalFont ? normalStyle.Underline : ReadChangedFormatCellsBool(currentUnderline, underlineBox),
                Strikethrough: normalFont ? normalStyle.Strikethrough : ReadChangedFormatCellsBool(_session.IsSelectedRangeStartStrikethrough, strikethroughBox),
                DoubleUnderline: normalFont ? normalStyle.DoubleUnderline : ReadChangedFormatCellsBool(currentDoubleUnderline, doubleUnderlineBox),
                Superscript: normalFont ? normalStyle.Superscript : ReadChangedFormatCellsBool(currentSuperscript, superscriptBox),
                Subscript: normalFont ? normalStyle.Subscript : ReadChangedFormatCellsBool(currentSubscript, subscriptBox),
                FontName: normalFont ? normalStyle.FontName : ReadChangedFormatCellsText(currentFontName, fontNameBox),
                FontSize: fontSize,
                FillColor: fillChoice?.Color,
                ClearFill: clearFill,
                FillPatternStyle: clearFill ? null : ReadChangedFormatCellsValue(currentFillPatternStyle, fillPatternStyleBox),
                FillPatternColor: clearFill ? null : (fillPatternColorBox.SelectedItem as FormatCellsColorChoice)?.Color,
                FontColor: normalFont ? normalStyle.FontColor : (fontColorBox.SelectedItem as FormatCellsColorChoice)?.Color,
                ShrinkToFit: ReadChangedFormatCellsBool(currentShrinkToFit, shrinkToFitBox),
                MergeCells: ReadChangedFormatCellsBool(currentMergeCells, mergeCellsBox),
                IndentLevel: indentLevel,
                TextRotation: textRotation,
                Locked: ReadChangedFormatCellsBool(currentLocked, lockedBox),
                Hidden: ReadChangedFormatCellsBool(currentHidden, hiddenBox));
            result = new FormatCellsDialogResult(request, borderChoice?.Value, borderStyle, borderColor);
            dialog.Close();
        }

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

        var numberTab = CreateFormatCellsTab(
            "Number",
            "FormatCellsNumberTab",
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            CreateFormatCellsField("Category", numberCategoryList),
                            CreateFormatCellsField("Type", numberFormatBox),
                        },
                    },
                    CreateFormatCellsField("Sample", numberPreview),
                },
            });
        var alignmentTab = CreateFormatCellsTab(
            "Alignment",
            "FormatCellsAlignmentTab",
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateFormatCellsField("Horizontal", horizontalAlignmentBox),
                    CreateFormatCellsField("Vertical", verticalAlignmentBox),
                    wrapTextBox,
                    shrinkToFitBox,
                    mergeCellsBox,
                    CreateFormatCellsField("Indent", indentLevelBox),
                    CreateFormatCellsField("Text rotation", textRotationBox),
                },
            });
        var fontTab = CreateFormatCellsTab(
            "Font",
            "FormatCellsFontTab",
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            boldBox,
                            italicBox,
                            underlineBox,
                            doubleUnderlineBox,
                            strikethroughBox,
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            superscriptBox,
                            subscriptBox,
                        },
                    },
                    CreateFormatCellsField("Font", fontNameBox),
                    CreateFormatCellsField("Size", fontSizeBox),
                    CreateFormatCellsField("Color", fontColorBox),
                    normalFontBox,
                },
            });
        var fillTab = CreateFormatCellsTab(
            "Fill",
            "FormatCellsFillTab",
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateFormatCellsField("Fill color", fillColorBox),
                    CreateFormatCellsField("Pattern style", fillPatternStyleBox),
                    CreateFormatCellsField("Pattern color", fillPatternColorBox),
                },
            });
        var borderTab = CreateFormatCellsTab(
            "Border",
            "FormatCellsBorderTab",
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateFormatCellsField("Preset", borderPresetBox),
                    CreateFormatCellsField("Line style", borderStyleBox),
                    CreateFormatCellsField("Line color", borderColorBox),
                },
            });
        var protectionTab = CreateFormatCellsTab(
            "Protection",
            "FormatCellsProtectionTab",
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    lockedBox,
                    hiddenBox,
                    protectionExplanationText,
                },
            });
        var tabStrip = new TabControl
        {
            SelectedIndex = 0,
            ItemsSource = new[]
            {
                numberTab,
                alignmentTab,
                fontTab,
                fillTab,
                borderTab,
                protectionTab,
            },
        };
        AutomationProperties.SetAutomationId(tabStrip, "FormatCellsTabStrip");

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
            Spacing = 10,
            Children =
            {
                tabStrip,
                errorText,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) => numberFormatBox.Focus();
        if (launchSmokeProbe is not null)
        {
            dialog.Opened += (_, _) =>
            {
                RunLaunchSmokeDialogProbe(
                    dialog,
                    () => launchSmokeProbe(new FormatCellsDialogSmokeProbe(
                        dialog,
                        tabStrip,
                        numberTab,
                        alignmentTab,
                        fontTab,
                        fillTab,
                        borderTab,
                        protectionTab,
                        numberCategoryList,
                        numberFormatBox,
                        numberPreview,
                        okButton,
                        cancelButton)));
            };
        }

        await dialog.ShowDialog(this);
        return result;
    }

    private static TabItem CreateFormatCellsTab(string header, string automationId, Control content)
    {
        var tab = new TabItem
        {
            Header = header,
            Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };
        AutomationProperties.SetAutomationId(tab, automationId);
        return tab;
    }

    private static StackPanel CreateFormatCellsField(string label, Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label },
                control,
            },
        };

    private static ComboBox CreateFormatCellsComboBox<T>(
        string automationId,
        IReadOnlyList<FormatCellsNullableChoice<T>> choices,
        T currentValue)
        where T : struct
    {
        var selected = choices[0];
        foreach (var choice in choices)
        {
            if (choice.Value.HasValue &&
                EqualityComparer<T>.Default.Equals(choice.Value.Value, currentValue))
            {
                selected = choice;
                break;
            }
        }

        var comboBox = new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = selected,
            MinWidth = 180,
        };
        AutomationProperties.SetAutomationId(comboBox, automationId);
        return comboBox;
    }

    private static CheckBox CreateFormatCellsCheckBox(string label, string automationId, bool isChecked)
    {
        var checkBox = new CheckBox
        {
            Content = label,
            IsChecked = isChecked,
        };
        AutomationProperties.SetAutomationId(checkBox, automationId);
        return checkBox;
    }

    private static IReadOnlyList<FormatCellsNumberFormatChoice> CreateFormatCellsNumberFormatChoices(string currentNumberFormat)
    {
        var choices = new List<FormatCellsNumberFormatChoice>
        {
            new("General", GeneralNumberFormat, "Sample"),
            new("Number", "0", "1234"),
            new("Number", "0.00", "1234.00"),
            new("Number", "#,##0", "1,234"),
            new("Number", CommaNumberFormat, "1,234.00"),
            new("Currency", CurrencyNumberFormat, "$1,234.00"),
            new("Percentage", PercentNumberFormat, "12%"),
            new("Percentage", "0.00%", "12.34%"),
            new("Date", "m/d/yyyy", "1/1/2026"),
            new("Time", "h:mm AM/PM", "9:30 AM"),
        };
        if (FindFormatCellsNumberFormatChoice(choices, currentNumberFormat) is null)
            choices.Add(new FormatCellsNumberFormatChoice("Custom", currentNumberFormat, "Custom"));

        return choices;
    }

    private static FormatCellsNumberFormatChoice? FindFormatCellsNumberFormatChoice(
        IEnumerable<FormatCellsNumberFormatChoice> choices,
        string formatCode)
    {
        foreach (var choice in choices)
        {
            if (string.Equals(choice.FormatCode, formatCode, StringComparison.Ordinal))
                return choice;
        }

        return null;
    }

    private static IReadOnlyList<FormatCellsNumberFormatChoice> FilterFormatCellsNumberFormatChoices(
        IEnumerable<FormatCellsNumberFormatChoice> choices,
        string category)
    {
        var matchingChoices = new List<FormatCellsNumberFormatChoice>();
        foreach (var choice in choices)
        {
            if (string.Equals(choice.Category, category, StringComparison.Ordinal))
                matchingChoices.Add(choice);
        }

        return matchingChoices;
    }

    private static bool ContainsFormatCellsNumberFormatChoice(
        IEnumerable<FormatCellsNumberFormatChoice> choices,
        FormatCellsNumberFormatChoice selected)
    {
        foreach (var choice in choices)
        {
            if (EqualityComparer<FormatCellsNumberFormatChoice>.Default.Equals(choice, selected))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<FormatCellsNullableChoice<CellHAlign>> CreateFormatCellsHorizontalAlignmentChoices() =>
    [
        new("General", CellHAlign.General),
        new("Left", CellHAlign.Left),
        new("Center", CellHAlign.Center),
        new("Right", CellHAlign.Right),
        new("Justify", CellHAlign.Justify),
        new("Distributed", CellHAlign.Distributed),
    ];

    private static IReadOnlyList<FormatCellsNullableChoice<CellVAlign>> CreateFormatCellsVerticalAlignmentChoices() =>
    [
        new("Top", CellVAlign.Top),
        new("Middle", CellVAlign.Center),
        new("Bottom", CellVAlign.Bottom),
        new("Justify", CellVAlign.Justify),
        new("Distributed", CellVAlign.Distributed),
    ];

    private static IReadOnlyList<FormatCellsColorChoice> CreateFormatCellsColorChoices(bool includeClear)
    {
        var choices = new List<FormatCellsColorChoice>
        {
            new("No change", null, Clear: false),
        };
        if (includeClear)
            choices.Add(new FormatCellsColorChoice("No fill", null, Clear: true));

        choices.AddRange(CellColorPalettePlanner.BuildDefaultSwatches()
            .Take(16)
            .Select(swatch => new FormatCellsColorChoice(swatch.Hex, swatch.Color, Clear: false)));
        return choices;
    }

    private static IReadOnlyList<FormatCellsNullableChoice<CellFillPatternStyle>> CreateFormatCellsFillPatternStyleChoices() =>
    [
        new("None", CellFillPatternStyle.None),
        new("Solid", CellFillPatternStyle.Solid),
        new("6.25% gray", CellFillPatternStyle.Gray0625),
        new("12.5% gray", CellFillPatternStyle.Gray125),
        new("Light gray", CellFillPatternStyle.LightGray),
        new("Medium gray", CellFillPatternStyle.MediumGray),
        new("Dark gray", CellFillPatternStyle.DarkGray),
        new("Light horizontal", CellFillPatternStyle.LightHorizontal),
        new("Light vertical", CellFillPatternStyle.LightVertical),
        new("Light down", CellFillPatternStyle.LightDown),
        new("Light up", CellFillPatternStyle.LightUp),
        new("Light grid", CellFillPatternStyle.LightGrid),
        new("Light trellis", CellFillPatternStyle.LightTrellis),
        new("Dark horizontal", CellFillPatternStyle.DarkHorizontal),
        new("Dark vertical", CellFillPatternStyle.DarkVertical),
        new("Dark down", CellFillPatternStyle.DarkDown),
        new("Dark up", CellFillPatternStyle.DarkUp),
        new("Dark grid", CellFillPatternStyle.DarkGrid),
        new("Dark trellis", CellFillPatternStyle.DarkTrellis),
    ];

    private static IReadOnlyList<FormatCellsNullableChoice<CellBorderPreset>> CreateFormatCellsBorderPresetChoices() =>
    [
        new("No border change", null),
        .. FormatCellsCompactPlanner.GetBorderPresetMetadata()
            .Select(metadata => new FormatCellsNullableChoice<CellBorderPreset>(metadata.DisplayName, metadata.Preset)),
    ];

    private static IReadOnlyList<FormatCellsNullableChoice<BorderStyle>> CreateFormatCellsBorderStyleChoices() =>
    [
        new("Thin", BorderStyle.Thin),
        new("Medium", BorderStyle.Medium),
        new("Thick", BorderStyle.Thick),
        new("Dashed", BorderStyle.Dashed),
        new("Dotted", BorderStyle.Dotted),
        new("Double", BorderStyle.Double),
    ];

    private static bool? ReadChangedFormatCellsBool(bool currentValue, CheckBox checkBox)
    {
        var value = checkBox.IsChecked == true;
        return value == currentValue ? null : value;
    }

    private static void SelectFormatCellsColor(ComboBox comboBox, CellColor color)
    {
        if (comboBox.ItemsSource is not IEnumerable<FormatCellsColorChoice> choices)
            return;

        foreach (var choice in choices)
        {
            if (choice.Color == color)
            {
                comboBox.SelectedItem = choice;
                return;
            }
        }
    }

    private static T? ReadChangedFormatCellsValue<T>(T currentValue, ComboBox comboBox)
        where T : struct
    {
        if (comboBox.SelectedItem is FormatCellsNullableChoice<T> { Value: { } value } &&
            !EqualityComparer<T>.Default.Equals(value, currentValue))
        {
            return value;
        }

        return null;
    }

    private static string? ReadChangedFormatCellsText(string currentValue, TextBox textBox)
    {
        var value = textBox.Text?.Trim();
        return !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, currentValue, StringComparison.Ordinal)
                ? value
                : null;
    }

    private static bool TryReadFormatCellsFontSize(
        string? text,
        double currentFontSize,
        out double? fontSize,
        out string errorMessage)
    {
        fontSize = null;
        errorMessage = "";
        var trimmed = text?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return true;

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) &&
            !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            errorMessage = "Font size must be a number.";
            return false;
        }

        if (!double.IsFinite(parsed) || parsed <= 0)
        {
            errorMessage = "Font size must be a positive number.";
            return false;
        }

        if (Math.Abs(parsed - currentFontSize) > 0.001)
            fontSize = parsed;

        return true;
    }

    private static bool TryReadFormatCellsIndentLevel(
        string? text,
        int currentIndentLevel,
        out int? indentLevel,
        out string errorMessage)
    {
        indentLevel = null;
        errorMessage = "";
        var trimmed = text?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return true;

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            errorMessage = "Indent level must be a whole number from 0 through 15.";
            return false;
        }

        var normalized = Math.Clamp(parsed, 0, 15);
        if (normalized != currentIndentLevel)
            indentLevel = normalized;

        return true;
    }

    private static bool TryReadFormatCellsTextRotation(
        string? text,
        int currentTextRotation,
        out int? textRotation,
        out string errorMessage)
    {
        textRotation = null;
        errorMessage = "";
        var trimmed = text?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return true;

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed != 255 && parsed is < -90 or > 90)
        {
            errorMessage = "Text rotation must be 255 or a whole number from -90 through 90.";
            return false;
        }

        if (parsed != currentTextRotation)
            textRotation = parsed;

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

    private void FlashFillSelectedRange()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.FlashFillSelectedRange();
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Flash Fill failed.");
            return;
        }

        RefreshShell($"Flash filled {rangeReference}");
    }

    private void SortSelectedRange(bool ascending)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SortSelectedRange(ascending);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Sort failed.");
            return;
        }

        RefreshShell($"Sorted {rangeReference} {(ascending ? "A to Z" : "Z to A")}");
    }

    private async Task ShowSortDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var selection = await ShowSortInputDialogAsync();
        if (selection is null)
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var keys = SortDialogPlanner.BuildSortKeys(selection.Levels);
        var options = new SortOptions(CaseSensitive: false, LeftToRight: false);
        var result = _session.SortSelectedRange(keys, options, selection.HasHeaders);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Sort failed.");
            return;
        }

        RefreshShell($"Sorted {rangeReference}");
    }

    private async Task<SortDialogResult?> ShowSortInputDialogAsync()
    {
        return await ShowSortInputDialogAsync(null);
    }

    private async Task<SortDialogResult?> ShowSortInputDialogAsync(Action<SortDialogSmokeProbe>? launchSmokeProbe)
    {
        SortDialogResult? result = null;
        var range = _session.SelectedRange;
        var levels = SortDialogPlanner.NormalizeLevels(null).ToList();
        var sortOnChoices = new[]
        {
            new SortOnChoice(SortDialogPlannerText.Default.SortOnCellValues),
            new SortOnChoice(SortDialogPlannerText.Default.SortOnCellColor),
            new SortOnChoice(SortDialogPlannerText.Default.SortOnFontColor),
        };
        var cellColorChoices = SortDialogPlanner.BuildColorChoices(_session.Workbook, _session.ActiveSheet, range, SortOn.CellColor);
        var fontColorChoices = SortDialogPlanner.BuildColorChoices(_session.Workbook, _session.ActiveSheet, range, SortOn.FontColor);
        var dialog = new Window
        {
            Title = "Sort",
            Width = 760,
            Height = 430,
            MinWidth = 700,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "SortCompactDialog");
        AutomationProperties.SetHelpText(
            dialog,
            "macOS preview custom sort supports cell values, cell color, and font color by column; custom-list, case-sensitive, and left-to-right options remain WPF-only.");

        var headersCheck = new CheckBox
        {
            Content = "My data has headers",
            IsChecked = true,
        };
        AutomationProperties.SetAutomationId(headersCheck, "SortHeadersCheckBox");

        var levelsPanel = new StackPanel
        {
            Spacing = 8,
        };
        AutomationProperties.SetAutomationId(levelsPanel, "SortLevelsPanel");

        var addLevelButton = new Button
        {
            Content = "Add Level",
            MinWidth = 94,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(addLevelButton, "SortAddLevelButton");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(okButton, "SortOkButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(cancelButton, "SortCancelButton");

        ComboBox? firstSortOnBox = null;
        ComboBox? firstColorBox = null;

        IReadOnlyList<SortColumnChoice> CurrentColumnChoices()
        {
            var hasHeaders = headersCheck.IsChecked == true;
            var headerChoices = SortDialogPlanner.BuildColumnChoices(_session.ActiveSheet, range, hasHeaders: true);
            var genericChoices = SortDialogPlanner.BuildColumnChoices(_session.ActiveSheet, range, hasHeaders: false);
            return hasHeaders ? headerChoices : genericChoices;
        }

        IReadOnlyList<SortDialogComboItem<SortColumnChoice>> CreateColumnItems() =>
            CurrentColumnChoices()
                .Select(choice => new SortDialogComboItem<SortColumnChoice>(choice.Label, choice))
                .ToList();

        IReadOnlyList<SortDialogComboItem<SortOnChoice>> CreateSortOnItems() =>
            sortOnChoices
                .Select(choice => new SortDialogComboItem<SortOnChoice>(choice.Label, choice))
                .ToList();

        IReadOnlyList<SortDialogComboItem<SortDirectionChoice>> CreateOrderItems(SortDialogLevel level) =>
            level.OrderChoices
                .Select(choice => new SortDialogComboItem<SortDirectionChoice>(choice.Label, choice))
                .ToList();

        IReadOnlyList<SortDialogComboItem<SortColorChoice>> CreateColorItems(SortDialogLevel level) =>
            level.ColorChoices
                .Select(choice => new SortDialogComboItem<SortColorChoice>(string.IsNullOrWhiteSpace(choice.Label) ? "None" : choice.Label, choice))
                .ToList();

        void SelectColumn(ComboBox comboBox, IReadOnlyList<SortDialogComboItem<SortColumnChoice>> choices, SortDialogLevel level)
        {
            var selected = choices.Count > 0 ? choices[0] : null;
            foreach (var choice in choices)
            {
                if (choice.Value.ColumnOffset == level.ColumnOffset)
                {
                    selected = choice;
                    break;
                }
            }

            comboBox.SelectedItem = selected;
        }

        void SelectSortOn(ComboBox comboBox, IReadOnlyList<SortDialogComboItem<SortOnChoice>> choices, SortDialogLevel level)
        {
            var selected = choices.Count > 0 ? choices[0] : null;
            foreach (var choice in choices)
            {
                if (string.Equals(choice.Value.Label, level.SortOn, StringComparison.Ordinal))
                {
                    selected = choice;
                    break;
                }
            }

            comboBox.SelectedItem = selected;
        }

        void SelectOrder(ComboBox comboBox, IReadOnlyList<SortDialogComboItem<SortDirectionChoice>> choices, SortDialogLevel level)
        {
            var selected = choices.Count > 0 ? choices[0] : null;
            foreach (var choice in choices)
            {
                if (choice.Value.Ascending == level.Ascending)
                {
                    selected = choice;
                    break;
                }
            }

            comboBox.SelectedItem = selected;
        }

        void SelectColor(ComboBox comboBox, IReadOnlyList<SortDialogComboItem<SortColorChoice>> choices, SortDialogLevel level)
        {
            var selected = choices.Count > 0 ? choices[0] : null;
            foreach (var choice in choices)
            {
                if (string.Equals(choice.Value.Label, level.TargetColor, StringComparison.OrdinalIgnoreCase))
                {
                    selected = choice;
                    break;
                }
            }

            comboBox.SelectedItem = selected;
        }

        void ApplyColorChoices(SortDialogLevel level)
        {
            level.SetColorChoices(SortDialogPlanner.BuildColorChoicesForSortOn(
                level.SortOn,
                cellColorChoices,
                fontColorChoices));
        }

        static bool IsColorSort(SortDialogLevel level) =>
            SortDialogPlanner.SortOnFromLabel(level.SortOn) is SortOn.CellColor or SortOn.FontColor;

        StackPanel CreateSortField(string label, Control control, double width) =>
            new()
            {
                Width = width,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = label },
                    control,
                },
            };

        void RebuildLevels()
        {
            levels = SortDialogPlanner.NormalizeLevels(levels).ToList();
            levelsPanel.Children.Clear();
            firstSortOnBox = null;
            firstColorBox = null;
            var columnChoices = CreateColumnItems();
            var sortOnItems = CreateSortOnItems();
            for (var index = 0; index < levels.Count; index++)
            {
                var levelIndex = index;
                var level = levels[index];
                ApplyColorChoices(level);
                var directionChoices = CreateOrderItems(level);
                var colorChoices = CreateColorItems(level);
                var columnBox = new ComboBox
                {
                    ItemsSource = columnChoices,
                    MinWidth = 150,
                };
                AutomationProperties.SetName(columnBox, "Sort by");
                AutomationProperties.SetAutomationId(columnBox, $"SortLevel{levelIndex + 1}ColumnBox");
                SelectColumn(columnBox, columnChoices, level);
                columnBox.SelectionChanged += (_, _) =>
                {
                    if (columnBox.SelectedItem is SortDialogComboItem<SortColumnChoice> columnChoice)
                        levels[levelIndex].ColumnOffset = columnChoice.Value.ColumnOffset;
                };

                var sortOnBox = new ComboBox
                {
                    ItemsSource = sortOnItems,
                    MinWidth = 120,
                };
                AutomationProperties.SetName(sortOnBox, "Sort On");
                AutomationProperties.SetAutomationId(sortOnBox, $"SortLevel{levelIndex + 1}SortOnBox");
                SelectSortOn(sortOnBox, sortOnItems, level);

                var orderBox = new ComboBox
                {
                    ItemsSource = directionChoices,
                    MinWidth = 105,
                };
                AutomationProperties.SetName(orderBox, "Order");
                AutomationProperties.SetAutomationId(orderBox, $"SortLevel{levelIndex + 1}OrderBox");
                SelectOrder(orderBox, directionChoices, level);

                var colorBox = new ComboBox
                {
                    ItemsSource = colorChoices,
                    MinWidth = 105,
                    IsEnabled = IsColorSort(level),
                };
                AutomationProperties.SetName(colorBox, "Color");
                AutomationProperties.SetAutomationId(colorBox, $"SortLevel{levelIndex + 1}ColorBox");
                SelectColor(colorBox, colorChoices, level);

                if (levelIndex == 0)
                {
                    firstSortOnBox = sortOnBox;
                    firstColorBox = colorBox;
                }

                void RefreshSortOnDependentControls()
                {
                    var currentLevel = levels[levelIndex];
                    ApplyColorChoices(currentLevel);

                    var currentDirectionChoices = CreateOrderItems(currentLevel);
                    orderBox.ItemsSource = currentDirectionChoices;
                    SelectOrder(orderBox, currentDirectionChoices, currentLevel);

                    var currentColorChoices = CreateColorItems(currentLevel);
                    colorBox.ItemsSource = currentColorChoices;
                    colorBox.IsEnabled = IsColorSort(currentLevel);
                    SelectColor(colorBox, currentColorChoices, currentLevel);
                }

                sortOnBox.SelectionChanged += (_, _) =>
                {
                    if (sortOnBox.SelectedItem is not SortDialogComboItem<SortOnChoice> sortOnChoice)
                        return;

                    levels[levelIndex].SortOn = sortOnChoice.Value.Label;
                    RefreshSortOnDependentControls();
                };

                orderBox.SelectionChanged += (_, _) =>
                {
                    if (orderBox.SelectedItem is SortDialogComboItem<SortDirectionChoice> directionChoice)
                        levels[levelIndex].Ascending = directionChoice.Value.Ascending;
                };

                colorBox.SelectionChanged += (_, _) =>
                {
                    if (colorBox.SelectedItem is SortDialogComboItem<SortColorChoice> colorChoice)
                        levels[levelIndex].TargetColor = colorChoice.Value.Label;
                };

                var removeButton = new Button
                {
                    Content = "Remove",
                    IsEnabled = levels.Count > 1,
                    MinWidth = 82,
                    Padding = new Thickness(10, 4),
                    VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
                };
                AutomationProperties.SetAutomationId(removeButton, $"SortLevel{levelIndex + 1}RemoveButton");
                removeButton.Click += (_, _) =>
                {
                    var removeIndex = Math.Min(levelIndex, levels.Count - 1);
                    levels = SortDialogPlanner.RemoveLevel(levels, removeIndex).ToList();
                    RebuildLevels();
                };

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        CreateSortField("Sort by", columnBox, 190),
                        CreateSortField("Sort On", sortOnBox, 130),
                        CreateSortField("Order", orderBox, 115),
                        CreateSortField("Color", colorBox, 120),
                        removeButton,
                    },
                };
                AutomationProperties.SetAutomationId(row, $"SortLevel{levelIndex + 1}Row");
                levelsPanel.Children.Add(row);
            }
        }

        void Accept()
        {
            result = new SortDialogResult(
                SortDialogPlanner.NormalizeLevels(levels),
                headersCheck.IsChecked == true);
            dialog.Close();
        }

        addLevelButton.Click += (_, _) =>
        {
            levels = SortDialogPlanner.AddLevel(levels).ToList();
            RebuildLevels();
        };
        headersCheck.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                RebuildLevels();
        };
        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
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

        RebuildLevels();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                headersCheck,
                new ScrollViewer
                {
                    MaxHeight = 240,
                    Content = levelsPanel,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        addLevelButton,
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Children =
                    {
                        cancelButton,
                        okButton,
                    },
                },
            },
        };
        if (launchSmokeProbe is not null)
        {
            dialog.Opened += (_, _) =>
            {
                RunLaunchSmokeDialogProbe(
                    dialog,
                    () => launchSmokeProbe(new SortDialogSmokeProbe(
                        dialog,
                        headersCheck,
                        levelsPanel,
                        firstSortOnBox!,
                        firstColorBox!,
                        addLevelButton,
                        okButton,
                        cancelButton)));
            };
        }

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task ShowGoalSeekDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var request = await ShowGoalSeekInputDialogAsync();
        if (request is null)
            return;

        var result = _session.ExecuteGoalSeek(request);
        var choice = await ShowGoalSeekStatusDialogAsync(result);
        if (result.Status == WorkbookGoalSeekStatus.Applied)
        {
            if (choice == GoalSeekStatusDialogChoice.RestoreOriginalValues)
            {
                var restoreResult = _session.UndoLastEdit();
                if (!restoreResult.Success)
                {
                    ShowEditIssue(restoreResult.ErrorMessage ?? "Goal Seek restore failed.");
                    return;
                }

                RefreshShell($"Restored original Goal Seek values for {FormatCellReference(result.Request.ChangingCell)}");
                return;
            }

            RefreshShell(FormatGoalSeekStatus(result));
            return;
        }

        ShowEditIssue(FormatGoalSeekStatus(result));
    }

    private async Task<GoalSeekRequest?> ShowGoalSeekInputDialogAsync()
    {
        GoalSeekRequest? result = null;
        var dialog = new Window
        {
            Title = "Goal Seek",
            Width = 420,
            Height = 270,
            MinWidth = 360,
            MinHeight = 245,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "GoalSeekCompactDialog");

        var setCellBox = new TextBox
        {
            Text = FormatCellReference(_session.ActiveCell),
            MinWidth = 220,
        };
        AutomationProperties.SetName(setCellBox, "Set cell");
        AutomationProperties.SetAutomationId(setCellBox, "GoalSeekSetCellBox");
        AutomationProperties.SetHelpText(setCellBox, "Formula cell to solve.");

        var targetValueBox = new TextBox
        {
            MinWidth = 220,
        };
        AutomationProperties.SetName(targetValueBox, "To value");
        AutomationProperties.SetAutomationId(targetValueBox, "GoalSeekTargetValueBox");
        AutomationProperties.SetHelpText(targetValueBox, "Target value for the set cell.");

        var changingCellBox = new TextBox
        {
            MinWidth = 220,
        };
        AutomationProperties.SetName(changingCellBox, "By changing cell");
        AutomationProperties.SetAutomationId(changingCellBox, "GoalSeekChangingCellBox");
        AutomationProperties.SetHelpText(changingCellBox, "Input cell Goal Seek can change.");

        var errorText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(errorText, "Goal Seek validation");
        AutomationProperties.SetAutomationId(errorText, "GoalSeekErrorText");
        AutomationProperties.SetHelpText(errorText, "Shows Goal Seek input validation messages.");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(okButton, "OK");
        AutomationProperties.SetAutomationId(okButton, "GoalSeekOkButton");
        AutomationProperties.SetHelpText(okButton, "Run Goal Seek with these inputs.");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(cancelButton, "Cancel");
        AutomationProperties.SetAutomationId(cancelButton, "GoalSeekCancelButton");
        AutomationProperties.SetHelpText(cancelButton, "Close Goal Seek without running.");

        void Accept()
        {
            var parseResult = GoalSeekRequestParser.Parse(
                _session.ActiveSheet.Id,
                setCellBox.Text,
                targetValueBox.Text,
                changingCellBox.Text);
            if (!parseResult.Success)
            {
                errorText.Text = FormatGoalSeekParseError(parseResult);
                FocusGoalSeekErrorField(parseResult.Error, setCellBox, targetValueBox, changingCellBox);
                return;
            }

            result = parseResult.Request;
            dialog.Close();
        }

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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                cancelButton,
                okButton,
            },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        CreateGoalSeekField("Set cell", setCellBox),
                        CreateGoalSeekField("To value", targetValueBox),
                        CreateGoalSeekField("By changing cell", changingCellBox),
                        errorText,
                    },
                },
            },
        };
        dialog.Opened += (_, _) =>
        {
            setCellBox.Focus();
            setCellBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<GoalSeekStatusDialogChoice> ShowGoalSeekStatusDialogAsync(WorkbookGoalSeekResult result)
    {
        var choice = result.Status == WorkbookGoalSeekStatus.Applied
            ? GoalSeekStatusDialogChoice.KeepResult
            : GoalSeekStatusDialogChoice.Dismiss;
        var dialog = new Window
        {
            Title = "Goal Seek Status",
            Width = 420,
            Height = 220,
            MinWidth = 360,
            MinHeight = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "GoalSeekStatusDialog");

        var summaryBlock = new TextBlock
        {
            Text = FormatGoalSeekStatus(result),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
        };
        AutomationProperties.SetName(summaryBlock, "Goal Seek Status");
        AutomationProperties.SetAutomationId(summaryBlock, "GoalSeekStatusText");
        AutomationProperties.SetHelpText(summaryBlock, "Shows the Goal Seek result status.");

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        Button defaultButton;
        if (result.Status == WorkbookGoalSeekStatus.Applied)
        {
            var restoreButton = new Button
            {
                Content = "Restore Original Values",
                MinWidth = 150,
                Padding = new Thickness(10, 4),
            };
            AutomationProperties.SetName(restoreButton, "Restore Original Values");
            AutomationProperties.SetAutomationId(restoreButton, "GoalSeekRestoreOriginalValuesButton");
            AutomationProperties.SetHelpText(restoreButton, "Undo the Goal Seek result and restore the original changing cell value.");

            var keepButton = new Button
            {
                Content = "Keep Result",
                MinWidth = 104,
                Padding = new Thickness(10, 4),
            };
            AutomationProperties.SetName(keepButton, "Keep Result");
            AutomationProperties.SetAutomationId(keepButton, "GoalSeekKeepResultButton");
            AutomationProperties.SetHelpText(keepButton, "Keep the applied Goal Seek result in the workbook.");

            restoreButton.Click += (_, _) =>
            {
                choice = GoalSeekStatusDialogChoice.RestoreOriginalValues;
                dialog.Close();
            };
            keepButton.Click += (_, _) =>
            {
                choice = GoalSeekStatusDialogChoice.KeepResult;
                dialog.Close();
            };
            buttonRow.Children.Add(restoreButton);
            buttonRow.Children.Add(keepButton);
            defaultButton = keepButton;
        }
        else
        {
            var okButton = new Button
            {
                Content = "OK",
                MinWidth = 84,
                Padding = new Thickness(10, 4),
            };
            AutomationProperties.SetName(okButton, "OK");
            AutomationProperties.SetAutomationId(okButton, "GoalSeekStatusOkButton");
            AutomationProperties.SetHelpText(okButton, "Close the Goal Seek status dialog.");
            okButton.Click += (_, _) =>
            {
                choice = GoalSeekStatusDialogChoice.Dismiss;
                dialog.Close();
            };
            buttonRow.Children.Add(okButton);
            defaultButton = okButton;
        }

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = summaryBlock,
                },
            },
        };
        dialog.Opened += (_, _) => defaultButton.Focus();

        await dialog.ShowDialog(this);
        return choice;
    }

    private static string FormatGoalSeekParseError(GoalSeekRequestParseResult result) =>
        result.Error switch
        {
            GoalSeekRequestParseError.SetCellRequired => "Set cell is required.",
            GoalSeekRequestParseError.InvalidSetCellAddress => $"Set cell '{result.InvalidText}' is not a valid cell reference.",
            GoalSeekRequestParseError.InvalidTargetValue => "Target value must be a finite number.",
            GoalSeekRequestParseError.ChangingCellRequired => "Changing cell is required.",
            GoalSeekRequestParseError.InvalidChangingCellAddress => $"Changing cell '{result.InvalidText}' is not a valid cell reference.",
            GoalSeekRequestParseError.CellsMustDiffer => "Set cell and changing cell must be different.",
            _ => "Goal Seek request is invalid."
        };

    private static string FormatGoalSeekStatus(WorkbookGoalSeekResult result)
    {
        var setCell = FormatCellReference(result.Request.SetCell);
        var changingCell = FormatCellReference(result.Request.ChangingCell);
        return result.Status switch
        {
            WorkbookGoalSeekStatus.Applied when result.SeekResult is { } seekResult =>
                string.Join(
                    Environment.NewLine,
                    "Goal Seek found a solution.",
                    $"Target value: {FormatGoalSeekNumber(result.Request.TargetValue)}",
                    $"Current value: {FormatGoalSeekNumber(seekResult.ActualResult)}",
                    $"Changing cell {changingCell}: {FormatGoalSeekNumber(seekResult.FoundValue)}"),
            WorkbookGoalSeekStatus.NotConverged when result.SeekResult is { } seekResult =>
                string.Join(
                    Environment.NewLine,
                    "Goal Seek could not find a solution.",
                    $"Target value: {FormatGoalSeekNumber(result.Request.TargetValue)}",
                    $"Current value: {FormatGoalSeekNumber(seekResult.ActualResult)}",
                    $"Changing cell {changingCell}: {FormatGoalSeekNumber(seekResult.FoundValue)}"),
            WorkbookGoalSeekStatus.InvalidRequest =>
                result.ErrorMessage ?? $"Goal Seek request for {setCell} is invalid.",
            WorkbookGoalSeekStatus.ApplyFailed =>
                result.ErrorMessage ?? $"Goal Seek result for {changingCell} could not be applied.",
            _ => "Goal Seek could not complete."
        };
    }

    private static string FormatGoalSeekNumber(double value) =>
        value.ToString("G12", CultureInfo.CurrentCulture);

    private static void FocusGoalSeekErrorField(
        GoalSeekRequestParseError error,
        TextBox setCellBox,
        TextBox targetValueBox,
        TextBox changingCellBox)
    {
        var target = error switch
        {
            GoalSeekRequestParseError.SetCellRequired or
            GoalSeekRequestParseError.InvalidSetCellAddress => setCellBox,
            GoalSeekRequestParseError.InvalidTargetValue => targetValueBox,
            GoalSeekRequestParseError.ChangingCellRequired or
            GoalSeekRequestParseError.InvalidChangingCellAddress or
            GoalSeekRequestParseError.CellsMustDiffer => changingCellBox,
            _ => setCellBox
        };
        target.Focus();
        target.SelectAll();
    }

    private static StackPanel CreateGoalSeekField(string label, Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label },
                control,
            },
        };

    private async Task ShowAdvancedFilterDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var plan = await ShowAdvancedFilterInputDialogAsync();
        if (plan is null)
            return;

        var result = _session.ExecuteAdvancedFilterPlan(plan);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Advanced Filter failed.");
            return;
        }

        RefreshShell(FormatAdvancedFilterStatus(plan));
    }

    private async Task<AdvancedFilterPlan?> ShowAdvancedFilterInputDialogAsync()
    {
        AdvancedFilterPlan? result = null;
        var dialog = new Window
        {
            Title = "Advanced Filter",
            Width = 500,
            Height = 390,
            MinWidth = 420,
            MinHeight = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "AdvancedFilterCompactDialog");

        var listRangeBox = new TextBox
        {
            Text = FormatRangeReference(_session.SelectedRange),
            MinWidth = 280,
        };
        AutomationProperties.SetName(listRangeBox, "List range");
        AutomationProperties.SetAutomationId(listRangeBox, "AdvancedFilterListRangeBox");
        AutomationProperties.SetHelpText(listRangeBox, "Range containing list headers and records.");

        var criteriaRangeBox = new TextBox
        {
            MinWidth = 280,
        };
        AutomationProperties.SetName(criteriaRangeBox, "Criteria range");
        AutomationProperties.SetAutomationId(criteriaRangeBox, "AdvancedFilterCriteriaRangeBox");
        AutomationProperties.SetHelpText(criteriaRangeBox, "Range containing criteria headers and criteria rows.");

        var inPlaceButton = new RadioButton
        {
            Content = "Filter in-place",
            GroupName = "AdvancedFilterOutputMode",
            IsChecked = true,
        };
        AutomationProperties.SetName(inPlaceButton, "Filter in-place");
        AutomationProperties.SetAutomationId(inPlaceButton, "AdvancedFilterInPlaceButton");
        AutomationProperties.SetHelpText(inPlaceButton, "Filter the list range without copying results.");

        var copyToAnotherLocationButton = new RadioButton
        {
            Content = "Copy to another location",
            GroupName = "AdvancedFilterOutputMode",
        };
        AutomationProperties.SetName(copyToAnotherLocationButton, "Copy to another location");
        AutomationProperties.SetAutomationId(copyToAnotherLocationButton, "AdvancedFilterCopyToAnotherLocationButton");
        AutomationProperties.SetHelpText(copyToAnotherLocationButton, "Copy filtered rows to the Copy to range.");

        var copyToBox = new TextBox
        {
            IsEnabled = false,
            MinWidth = 280,
        };
        AutomationProperties.SetName(copyToBox, "Copy to");
        AutomationProperties.SetAutomationId(copyToBox, "AdvancedFilterCopyToBox");
        AutomationProperties.SetHelpText(copyToBox, "Destination cell or one-row header range on the list sheet.");

        var uniqueBox = new CheckBox
        {
            Content = "Unique records only",
        };
        AutomationProperties.SetName(uniqueBox, "Unique records only");
        AutomationProperties.SetAutomationId(uniqueBox, "AdvancedFilterUniqueRecordsOnlyBox");
        AutomationProperties.SetHelpText(uniqueBox, "Return only unique matching records.");

        var errorText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(errorText, "Advanced Filter validation");
        AutomationProperties.SetAutomationId(errorText, "AdvancedFilterErrorText");
        AutomationProperties.SetHelpText(errorText, "Shows Advanced Filter readiness and validation messages.");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(okButton, "OK");
        AutomationProperties.SetAutomationId(okButton, "AdvancedFilterOkButton");
        AutomationProperties.SetHelpText(okButton, "Run Advanced Filter.");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(cancelButton, "Cancel");
        AutomationProperties.SetAutomationId(cancelButton, "AdvancedFilterCancelButton");
        AutomationProperties.SetHelpText(cancelButton, "Close Advanced Filter without running.");

        AdvancedFilterPlanResult CreatePlan()
        {
            var selectedOutputMode = copyToAnotherLocationButton.IsChecked == true
                ? AdvancedFilterOutputMode.CopyToAnotherLocation
                : AdvancedFilterOutputMode.FilterInPlace;

            return AdvancedFilterPlanner.CreatePlan(
                _session.ActiveSheet.Id,
                listRangeBox.Text,
                criteriaRangeBox.Text,
                copyToBox.Text,
                selectedOutputMode,
                uniqueBox.IsChecked == true,
                sheetName => _session.Workbook.GetSheet(sheetName)?.Id);
        }

        void RefreshPlanStatus()
        {
            var planResult = CreatePlan();
            errorText.Text = planResult.Success
                ? "Ready to run Advanced Filter."
                : FormatAdvancedFilterPlanError(planResult);
        }

        void RefreshCopyToState()
        {
            copyToBox.IsEnabled = copyToAnotherLocationButton.IsChecked == true;
            RefreshPlanStatus();
        }

        void Accept()
        {
            var planResult = CreatePlan();
            if (!planResult.Success || planResult.Plan is null)
            {
                errorText.Text = FormatAdvancedFilterPlanError(planResult);
                FocusAdvancedFilterErrorField(planResult.Error, listRangeBox, criteriaRangeBox, copyToBox);
                return;
            }

            result = planResult.Plan;
            dialog.Close();
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        listRangeBox.TextChanged += (_, _) => RefreshPlanStatus();
        criteriaRangeBox.TextChanged += (_, _) => RefreshPlanStatus();
        copyToBox.TextChanged += (_, _) => RefreshPlanStatus();
        uniqueBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                RefreshPlanStatus();
        };
        inPlaceButton.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                RefreshCopyToState();
        };
        copyToAnotherLocationButton.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                RefreshCopyToState();
        };
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                cancelButton,
                okButton,
            },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        RefreshCopyToState();
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        CreateAdvancedFilterField("List range", listRangeBox),
                        CreateAdvancedFilterField("Criteria range", criteriaRangeBox),
                        new StackPanel
                        {
                            Spacing = 6,
                            Children =
                            {
                                inPlaceButton,
                                copyToAnotherLocationButton,
                            },
                        },
                        CreateAdvancedFilterField("Copy to", copyToBox),
                        uniqueBox,
                        errorText,
                    },
                },
            },
        };
        dialog.Opened += (_, _) =>
        {
            criteriaRangeBox.Focus();
            criteriaRangeBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private static string FormatAdvancedFilterStatus(AdvancedFilterPlan plan)
    {
        var listRange = FormatRangeReference(plan.ListRange);
        return plan is
        {
            OutputMode: AdvancedFilterOutputMode.CopyToAnotherLocation,
            CopyToRange: { } copyToRange
        }
            ? $"Advanced Filter copied {listRange} to {FormatRangeReference(copyToRange)}"
            : $"Advanced Filter applied to {listRange}";
    }

    private static string FormatAdvancedFilterPlanError(AdvancedFilterPlanResult result)
    {
        var message = result.Error switch
        {
            AdvancedFilterPlanError.None => "Ready to run Advanced Filter.",
            AdvancedFilterPlanError.InvalidListRange => "Enter a valid list range.",
            AdvancedFilterPlanError.ListRangeRequiresDataRows => "List range must include headers and at least one data row.",
            AdvancedFilterPlanError.ListRangeTooLarge => AdvancedFilterCommand.ListRangeTooLargeMessage,
            AdvancedFilterPlanError.InvalidCriteriaRange => "Enter a valid criteria range.",
            AdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows => "Criteria range must include headers and at least one criteria row.",
            AdvancedFilterPlanError.CriteriaRangeTooLarge => AdvancedFilterCommand.CriteriaRangeTooLargeMessage,
            AdvancedFilterPlanError.CopyDestinationRequired => "Enter a copy-to range.",
            AdvancedFilterPlanError.InvalidCopyDestinationRange => "Enter a valid one-row copy-to range on the active sheet.",
            AdvancedFilterPlanError.CopyDestinationRangeTooLarge => AdvancedFilterCommand.CopyOutputTooLargeMessage,
            AdvancedFilterPlanError.CopyDestinationMustBeOnListSheet => "Copy-to range must be on the list sheet.",
            _ => "Advanced Filter request is invalid."
        };

        return string.IsNullOrWhiteSpace(result.InvalidText)
            ? message
            : $"{message} ({result.InvalidText})";
    }

    private static void FocusAdvancedFilterErrorField(
        AdvancedFilterPlanError error,
        TextBox listRangeBox,
        TextBox criteriaRangeBox,
        TextBox copyToBox)
    {
        var target = error switch
        {
            AdvancedFilterPlanError.InvalidListRange or
            AdvancedFilterPlanError.ListRangeRequiresDataRows or
            AdvancedFilterPlanError.ListRangeTooLarge => listRangeBox,
            AdvancedFilterPlanError.InvalidCriteriaRange or
            AdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows or
            AdvancedFilterPlanError.CriteriaRangeTooLarge => criteriaRangeBox,
            AdvancedFilterPlanError.CopyDestinationRequired or
            AdvancedFilterPlanError.InvalidCopyDestinationRange or
            AdvancedFilterPlanError.CopyDestinationRangeTooLarge or
            AdvancedFilterPlanError.CopyDestinationMustBeOnListSheet => copyToBox,
            _ => criteriaRangeBox
        };
        target.Focus();
        target.SelectAll();
    }

    private static StackPanel CreateAdvancedFilterField(string label, Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label },
                control,
            },
        };

    private async Task ShowSubtotalDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var selection = await ShowSubtotalInputDialogAsync();
        if (selection is null)
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = selection.Action == SubtotalDialogAction.RemoveAll
            ? _session.RemoveSelectedRangeSubtotals()
            : _session.ExecuteSubtotalOptions(selection.Options!);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Subtotal failed.");
            return;
        }

        RefreshShell(selection.Action == SubtotalDialogAction.RemoveAll
            ? $"Removed subtotals from {rangeReference}"
            : $"Added subtotals to {rangeReference}");
    }

    private async Task<SubtotalDialogResult?> ShowSubtotalInputDialogAsync()
    {
        SubtotalDialogResult? result = null;
        var range = _session.SelectedRange;
        var columns = BuildSubtotalColumnChoices(_session.ActiveSheet, range);

        var dialog = new Window
        {
            Title = "Subtotal",
            Width = 460,
            Height = 480,
            MinWidth = 400,
            MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "SubtotalCompactDialog");

        var rangeText = new TextBlock
        {
            Text = $"Range: {FormatRangeReference(range)}",
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(rangeText, "Subtotal range");
        AutomationProperties.SetAutomationId(rangeText, "SubtotalRangeSummaryText");
        AutomationProperties.SetHelpText(rangeText, "Shows the selected range for subtotaling.");

        var groupColumnBox = new ComboBox
        {
            ItemsSource = columns,
            SelectedIndex = 0,
            MinWidth = 240,
        };
        AutomationProperties.SetName(groupColumnBox, "At each change in");
        AutomationProperties.SetAutomationId(groupColumnBox, "SubtotalGroupColumnBox");
        AutomationProperties.SetHelpText(groupColumnBox, "Choose the column used to group subtotal rows.");

        var functionBox = new ComboBox
        {
            ItemsSource = CreateSubtotalFunctionChoices(),
            SelectedIndex = 0,
            MinWidth = 240,
        };
        AutomationProperties.SetName(functionBox, "Use function");
        AutomationProperties.SetAutomationId(functionBox, "SubtotalFunctionBox");
        AutomationProperties.SetHelpText(functionBox, "Choose the subtotal calculation function.");

        var columnsPanel = new StackPanel
        {
            Spacing = 4,
        };
        AutomationProperties.SetName(columnsPanel, "Add subtotal to");
        AutomationProperties.SetAutomationId(columnsPanel, "SubtotalColumnsPanel");
        AutomationProperties.SetHelpText(columnsPanel, "Columns that receive subtotal calculations.");

        var columnBoxes = new List<CheckBox>();
        foreach (var column in columns)
        {
            var box = new CheckBox
            {
                Content = column.Header,
                IsChecked = column.IsSelected,
            };
            AutomationProperties.SetName(box, $"{column.Header} subtotal column");
            AutomationProperties.SetAutomationId(box, $"SubtotalColumn{column.Offset}Box");
            AutomationProperties.SetHelpText(box, "Select to add a subtotal calculation to this column.");
            columnBoxes.Add(box);
            columnsPanel.Children.Add(box);
        }

        var replaceBox = new CheckBox
        {
            Content = "Replace current subtotals",
            IsChecked = true,
        };
        AutomationProperties.SetName(replaceBox, "Replace current subtotals");
        AutomationProperties.SetAutomationId(replaceBox, "SubtotalReplaceCurrentBox");
        AutomationProperties.SetHelpText(replaceBox, "Replace existing subtotals before applying new ones.");

        var pageBreakBox = new CheckBox
        {
            Content = "Page break between groups",
            IsChecked = false,
        };
        AutomationProperties.SetName(pageBreakBox, "Page break between groups");
        AutomationProperties.SetAutomationId(pageBreakBox, "SubtotalPageBreakBox");
        AutomationProperties.SetHelpText(pageBreakBox, "Insert a page break after each subtotal group.");

        var summaryBelowBox = new CheckBox
        {
            Content = "Summary below data",
            IsChecked = true,
        };
        AutomationProperties.SetName(summaryBelowBox, "Summary below data");
        AutomationProperties.SetAutomationId(summaryBelowBox, "SubtotalSummaryBelowBox");
        AutomationProperties.SetHelpText(summaryBelowBox, "Place summary rows below the grouped data.");

        var errorText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(errorText, "Subtotal validation");
        AutomationProperties.SetAutomationId(errorText, "SubtotalErrorText");
        AutomationProperties.SetHelpText(errorText, "Shows Subtotal validation messages.");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(okButton, "OK");
        AutomationProperties.SetAutomationId(okButton, "SubtotalOkButton");
        AutomationProperties.SetHelpText(okButton, "Apply subtotal options.");

        var removeAllButton = new Button
        {
            Content = "Remove All",
            MinWidth = 96,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(removeAllButton, "Remove All");
        AutomationProperties.SetAutomationId(removeAllButton, "SubtotalRemoveAllButton");
        AutomationProperties.SetHelpText(removeAllButton, "Remove subtotals from the selected range.");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(cancelButton, "Cancel");
        AutomationProperties.SetAutomationId(cancelButton, "SubtotalCancelButton");
        AutomationProperties.SetHelpText(cancelButton, "Close Subtotal without applying changes.");

        void Accept()
        {
            if (groupColumnBox.SelectedItem is not SubtotalColumnChoice groupColumn ||
                functionBox.SelectedItem is not SubtotalFunctionChoice functionChoice ||
                !SubtotalFunctionService.TryParse(functionChoice.FunctionText, out var functionNumber))
            {
                errorText.Text = "Choose a group column and subtotal function.";
                groupColumnBox.Focus();
                return;
            }

            var selectedOffsets = columns
                .Where((_, index) => columnBoxes.ElementAtOrDefault(index)?.IsChecked == true)
                .Select(static column => column.Offset)
                .ToArray();
            if (selectedOffsets.Length == 0)
            {
                errorText.Text = "Select at least one subtotal column.";
                Control focusTarget = okButton;
                foreach (var columnBox in columnBoxes)
                {
                    focusTarget = columnBox;
                    break;
                }

                focusTarget.Focus();
                return;
            }

            result = new SubtotalDialogResult(
                SubtotalDialogAction.Apply,
                new SubtotalInputOptions(
                    groupColumn.Offset,
                    selectedOffsets,
                    functionNumber,
                    replaceBox.IsChecked == true,
                    pageBreakBox.IsChecked == true,
                    summaryBelowBox.IsChecked != false));
            dialog.Close();
        }

        okButton.Click += (_, _) => Accept();
        removeAllButton.Click += (_, _) =>
        {
            result = new SubtotalDialogResult(SubtotalDialogAction.RemoveAll, Options: null);
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
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
                removeAllButton,
                cancelButton,
                okButton,
            },
        };

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            rangeText,
                            CreateSubtotalField("At each change in", groupColumnBox),
                            CreateSubtotalField("Use function", functionBox),
                            new GroupBox
                            {
                                Header = "Add subtotal to",
                                Content = columnsPanel,
                            },
                            replaceBox,
                            pageBreakBox,
                            summaryBelowBox,
                            errorText,
                        },
                    },
                },
            },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        dialog.Opened += (_, _) => groupColumnBox.Focus();

        await dialog.ShowDialog(this);
        return result;
    }

    private static IReadOnlyList<SubtotalColumnChoice> BuildSubtotalColumnChoices(Sheet sheet, GridRange range)
    {
        var choices = new List<SubtotalColumnChoice>();
        for (uint offset = 0; offset < range.ColCount; offset++)
        {
            var absoluteColumn = range.Start.Col + offset;
            var header = FormatScalarValue(sheet.GetValue(range.Start.Row, absoluteColumn));
            if (string.IsNullOrWhiteSpace(header))
                header = $"Column {CellAddress.NumberToColumnName(absoluteColumn)}";

            choices.Add(new SubtotalColumnChoice(offset, header, IsSelected: offset != 0));
        }

        return choices.Count == 0
            ? [new SubtotalColumnChoice(0, "Column A", IsSelected: false)]
            : choices;
    }

    private static IReadOnlyList<SubtotalFunctionChoice> CreateSubtotalFunctionChoices() =>
    [
        new("Sum", "Sum"),
        new("Count", "Count"),
        new("Average", "Average"),
        new("Max", "Max"),
        new("Min", "Min"),
        new("Product", "Product"),
        new("Count Numbers", "CountA"),
        new("StdDev", "StdDev"),
        new("StdDevp", "StdDevp"),
        new("Var", "Var"),
        new("Varp", "Varp"),
    ];

    private static StackPanel CreateSubtotalField(string label, Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label },
                control,
            },
        };

    private async Task ShowRemoveDuplicatesDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var plan = await ShowRemoveDuplicatesInputDialogAsync();
        if (plan is null)
            return;

        var result = _session.ExecuteRemoveDuplicatesPlan(plan);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Remove Duplicates failed.");
            return;
        }

        var status = FormatRemoveDuplicatesStatus(plan, result);
        RefreshShell(status);
        await ShowTextDialogAsync("Remove Duplicates", status, 420, 220);
    }

    private async Task<RemoveDuplicatesPlan?> ShowRemoveDuplicatesInputDialogAsync()
    {
        RemoveDuplicatesPlan? result = null;
        var range = _session.SelectedRange;
        var hasHeaders = RemoveDuplicatesPlanner.GuessHasHeaders(_session.ActiveSheet, range);
        IReadOnlyList<RemoveDuplicateColumnChoice> columns =
            RemoveDuplicatesPlanner.BuildColumnChoices(_session.ActiveSheet, range, hasHeaders);

        var dialog = new Window
        {
            Title = "Remove Duplicates",
            Width = 440,
            Height = 430,
            MinWidth = 380,
            MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "RemoveDuplicatesCompactDialog");

        var rangeText = new TextBlock
        {
            Text = $"Range: {FormatRangeReference(range)}",
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(rangeText, "Remove Duplicates range");
        AutomationProperties.SetAutomationId(rangeText, "RemoveDuplicatesRangeSummaryText");
        AutomationProperties.SetHelpText(rangeText, "Shows the selected range checked for duplicates.");

        var hasHeadersBox = new CheckBox
        {
            Content = "My data has headers",
            IsChecked = hasHeaders,
        };
        AutomationProperties.SetName(hasHeadersBox, "My data has headers");
        AutomationProperties.SetAutomationId(hasHeadersBox, "RemoveDuplicatesHasHeadersBox");
        AutomationProperties.SetHelpText(hasHeadersBox, "Treat the first row as headers when comparing duplicates.");

        var columnsPanel = new StackPanel
        {
            Spacing = 4,
        };
        AutomationProperties.SetName(columnsPanel, "Columns");
        AutomationProperties.SetAutomationId(columnsPanel, "RemoveDuplicatesColumnsPanel");
        AutomationProperties.SetHelpText(columnsPanel, "Columns used to identify duplicate rows.");

        var columnBoxes = new List<CheckBox>();

        IReadOnlyList<RemoveDuplicateColumnChoice> CaptureColumns() =>
            columns.Select((column, index) =>
                column with { IsSelected = columnBoxes.ElementAtOrDefault(index)?.IsChecked == true }).ToArray();

        void RenderColumns(IReadOnlyList<RemoveDuplicateColumnChoice> nextColumns)
        {
            columns = nextColumns;
            columnBoxes.Clear();
            columnsPanel.Children.Clear();
            foreach (var column in columns)
            {
                var box = new CheckBox
                {
                    Content = column.Label,
                    IsChecked = column.IsSelected,
                };
                AutomationProperties.SetName(box, column.Label);
                AutomationProperties.SetAutomationId(box, $"RemoveDuplicatesColumn{column.Offset}Box");
                AutomationProperties.SetHelpText(box, "Include this column when comparing duplicate rows.");
                columnBoxes.Add(box);
                columnsPanel.Children.Add(box);
            }
        }

        RenderColumns(columns);

        var selectAllButton = new Button
        {
            Content = "Select All",
            MinWidth = 92,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(selectAllButton, "Select All");
        AutomationProperties.SetAutomationId(selectAllButton, "RemoveDuplicatesSelectAllButton");
        AutomationProperties.SetHelpText(selectAllButton, "Select all columns for duplicate comparison.");

        var unselectAllButton = new Button
        {
            Content = "Unselect All",
            MinWidth = 92,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(unselectAllButton, "Unselect All");
        AutomationProperties.SetAutomationId(unselectAllButton, "RemoveDuplicatesUnselectAllButton");
        AutomationProperties.SetHelpText(unselectAllButton, "Clear all selected duplicate comparison columns.");

        var errorText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(errorText, "Remove Duplicates validation");
        AutomationProperties.SetAutomationId(errorText, "RemoveDuplicatesErrorText");
        AutomationProperties.SetHelpText(errorText, "Shows Remove Duplicates validation messages.");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(okButton, "OK");
        AutomationProperties.SetAutomationId(okButton, "RemoveDuplicatesOkButton");
        AutomationProperties.SetHelpText(okButton, "Remove duplicate rows using the selected columns.");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(cancelButton, "Cancel");
        AutomationProperties.SetAutomationId(cancelButton, "RemoveDuplicatesCancelButton");
        AutomationProperties.SetHelpText(cancelButton, "Close Remove Duplicates without changes.");

        void RebuildColumnsForHeaderState()
        {
            var previous = CaptureColumns().ToDictionary(static column => column.Offset, static column => column.IsSelected);
            var rebuilt = RemoveDuplicatesPlanner
                .BuildColumnChoices(_session.ActiveSheet, range, hasHeadersBox.IsChecked == true)
                .Select(column => column with
                {
                    IsSelected = previous.TryGetValue(column.Offset, out var selected)
                        ? selected
                        : column.IsSelected,
                })
                .ToArray();
            RenderColumns(rebuilt);
        }

        void Accept()
        {
            var planResult = RemoveDuplicatesPlanner.CreatePlan(
                range,
                hasHeadersBox.IsChecked == true,
                CaptureColumns());
            if (!planResult.IsReady || planResult.Plan is null)
            {
                errorText.Text = planResult.StatusText;
                Control focusTarget = selectAllButton;
                foreach (var columnBox in columnBoxes)
                {
                    focusTarget = columnBox;
                    break;
                }

                focusTarget.Focus();
                return;
            }

            result = planResult.Plan;
            dialog.Close();
        }

        hasHeadersBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                RebuildColumnsForHeaderState();
        };
        selectAllButton.Click += (_, _) =>
        {
            errorText.Text = "";
            RenderColumns(RemoveDuplicatesPlanner.SelectAll(CaptureColumns()));
        };
        unselectAllButton.Click += (_, _) =>
        {
            errorText.Text = "";
            RenderColumns(RemoveDuplicatesPlanner.ClearAll(CaptureColumns()));
        };
        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.KeyDown += (_, e) =>
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

        dialog.Content = new Border
        {
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    rangeText,
                    hasHeadersBox,
                    new TextBlock
                    {
                        Text = "Columns",
                        FontWeight = FontWeight.SemiBold,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            selectAllButton,
                            unselectAllButton,
                        },
                    },
                    new Border
                    {
                        BorderBrush = ToolbarBorder,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8),
                        Child = new ScrollViewer
                        {
                            MaxHeight = 170,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            Content = columnsPanel,
                        },
                    },
                    errorText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            okButton,
                            cancelButton,
                        },
                    },
                },
            },
        };
        dialog.Opened += (_, _) => hasHeadersBox.Focus();

        await dialog.ShowDialog(this);
        return result;
    }

    private static string FormatRemoveDuplicatesStatus(
        RemoveDuplicatesPlan plan,
        WorkbookRemoveDuplicatesResult result)
    {
        var rowLabel = result.RemovedRowCount == 1 ? "row" : "rows";
        return $"Removed {result.RemovedRowCount} duplicate {rowLabel} from {FormatRangeReference(plan.SourceRange)}";
    }

    private async Task ShowScenarioManagerDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var initialPlan = ScenarioManagerPlanner.CreateDialogPlan(_session.Workbook);
        if (!initialPlan.IsReady)
        {
            ShowEditIssue(initialPlan.StatusText ?? "Scenario Manager failed.");
            return;
        }

        await ShowScenarioManagerCompactDialogAsync(initialPlan);
    }

    private async Task ShowScenarioManagerCompactDialogAsync(ScenarioManagerPlan initialPlan)
    {
        var plan = initialPlan;
        string? selectedScenarioName = plan.SelectedScenario?.Name;
        var dialog = new Window
        {
            Title = "Scenario Manager",
            Width = 560,
            Height = 500,
            MinWidth = 460,
            MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ScenarioManagerCompactDialog");

        var statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = HeaderForeground,
        };
        AutomationProperties.SetName(statusText, "Scenario Manager status");
        AutomationProperties.SetHelpText(statusText, "Shows Scenario Manager availability and status.");

        var selectionText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = HeaderForeground,
        };
        AutomationProperties.SetName(selectionText, "Scenario Manager selection");
        AutomationProperties.SetHelpText(selectionText, "Shows the current selection saved into new scenarios.");

        var scenarioList = new ListBox
        {
            MinHeight = 120,
            MaxHeight = 150,
        };
        AutomationProperties.SetName(scenarioList, "Scenarios");
        AutomationProperties.SetAutomationId(scenarioList, "ScenarioManagerScenarioList");
        AutomationProperties.SetHelpText(scenarioList, "Select a saved scenario.");

        var scenarioDetailsText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            MinHeight = 58,
        };
        AutomationProperties.SetName(scenarioDetailsText, "Scenario details");
        AutomationProperties.SetHelpText(scenarioDetailsText, "Shows details for the selected scenario.");

        var nameBox = new TextBox
        {
            MinWidth = 260,
        };
        AutomationProperties.SetName(nameBox, "Scenario name");
        AutomationProperties.SetAutomationId(nameBox, "ScenarioManagerNameBox");
        AutomationProperties.SetHelpText(nameBox, "Scenario name.");

        var commentBox = new TextBox
        {
            MinWidth = 260,
        };
        AutomationProperties.SetName(commentBox, "Comment");
        AutomationProperties.SetAutomationId(commentBox, "ScenarioManagerCommentBox");
        AutomationProperties.SetHelpText(commentBox, "Scenario comment.");

        var errorText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 22,
        };
        AutomationProperties.SetName(errorText, "Scenario Manager validation");
        AutomationProperties.SetAutomationId(errorText, "ScenarioManagerErrorText");
        AutomationProperties.SetHelpText(errorText, "Shows Scenario Manager validation and error messages.");

        var saveButton = new Button
        {
            Content = "Save/Add",
            MinWidth = 92,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(saveButton, "Save/Add");
        AutomationProperties.SetAutomationId(saveButton, "ScenarioManagerSaveButton");
        AutomationProperties.SetHelpText(saveButton, "Save the selected cells as a new or updated scenario.");

        var showButton = new Button
        {
            Content = "Show",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(showButton, "Show");
        AutomationProperties.SetAutomationId(showButton, "ScenarioManagerShowButton");
        AutomationProperties.SetHelpText(showButton, "Apply the selected scenario values to the workbook.");

        var deleteButton = new Button
        {
            Content = "Delete",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(deleteButton, "Delete");
        AutomationProperties.SetAutomationId(deleteButton, "ScenarioManagerDeleteButton");
        AutomationProperties.SetHelpText(deleteButton, "Delete the selected scenario.");

        var summaryButton = new Button
        {
            Content = "Summary Report",
            MinWidth = 128,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(summaryButton, "Summary Report");
        AutomationProperties.SetAutomationId(summaryButton, "ScenarioManagerSummaryButton");
        AutomationProperties.SetHelpText(summaryButton, "Create a scenario summary report sheet.");

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(closeButton, "Close");
        AutomationProperties.SetAutomationId(closeButton, "ScenarioManagerCloseButton");
        AutomationProperties.SetHelpText(closeButton, "Close Scenario Manager.");

        string? CurrentScenarioName() =>
            scenarioList.SelectedItem is ScenarioManagerDialogScenarioItem item
                ? item.Choice.Name
                : selectedScenarioName;

        void RefreshSelectionDetails()
        {
            var selected = scenarioList.SelectedItem is ScenarioManagerDialogScenarioItem item
                ? item.Choice
                : null;
            selectedScenarioName = selected?.Name;
            scenarioDetailsText.Text = FormatScenarioManagerScenarioDetails(selected);
            showButton.IsEnabled = selected is not null;
            deleteButton.IsEnabled = selected is not null;
        }

        void RefreshDialogPlan(string? preferredScenarioName = null)
        {
            plan = ScenarioManagerPlanner.CreateDialogPlan(_session.Workbook, preferredScenarioName ?? selectedScenarioName);
            var items = plan.Scenarios
                .Select(choice => new ScenarioManagerDialogScenarioItem(choice))
                .ToArray();
            scenarioList.ItemsSource = items;
            ScenarioManagerDialogScenarioItem? selectedItem = null;
            foreach (var item in items)
            {
                if (item.Choice.IsSelected)
                {
                    selectedItem = item;
                    break;
                }
            }

            scenarioList.SelectedItem = selectedItem;
            statusText.Text = plan.StatusText;
            selectionText.Text = FormatScenarioManagerSelectionSummary(_session.SelectedRange);
            summaryButton.IsEnabled = items.Length > 0;
            RefreshSelectionDetails();
        }

        void ReportScenarioManagerFailure(WorkbookCellEditResult result)
        {
            var message = result.ErrorMessage ?? "Scenario Manager failed.";
            errorText.Text = message;
            ShowEditIssue(message);
        }

        bool ApplyScenarioManagerResult(WorkbookCellEditResult result, string status)
        {
            if (!result.Success)
            {
                ReportScenarioManagerFailure(result);
                return false;
            }

            errorText.Text = "";
            RefreshShell(status);
            return true;
        }

        void SaveCurrentValues()
        {
            var changingCells = CaptureScenarioManagerChangingCells(_session.SelectedRange);
            var request = new ScenarioManagerSaveRequest(
                nameBox.Text ?? "",
                changingCells,
                Comment: commentBox.Text);
            var savePlan = ScenarioManagerPlanner.CreateSavePlan(_session.Workbook, request);
            var result = _session.ExecuteScenarioManagerSavePlan(savePlan, request);
            if (!ApplyScenarioManagerResult(
                    result,
                    $"Saved scenario '{(nameBox.Text ?? "").Trim()}' ({changingCells.Count} {FormatCountLabel(changingCells.Count, "cell")})"))
            {
                nameBox.Focus();
                nameBox.SelectAll();
                return;
            }

            RefreshDialogPlan((nameBox.Text ?? "").Trim());
            nameBox.Text = CreateScenarioManagerDefaultName(plan.Scenarios);
            commentBox.Text = "";
        }

        void ShowSelectedScenario()
        {
            var scenarioName = CurrentScenarioName();
            var showPlan = ScenarioManagerPlanner.CreateShowPlan(_session.Workbook, scenarioName);
            var result = _session.ExecuteScenarioManagerShowPlan(showPlan);
            if (!ApplyScenarioManagerResult(
                    result,
                    $"Showed scenario '{showPlan.SelectedScenario?.Name ?? scenarioName}'"))
                return;

            RefreshDialogPlan(showPlan.SelectedScenario?.Name ?? scenarioName);
        }

        void DeleteSelectedScenario()
        {
            var scenarioName = CurrentScenarioName();
            var deletePlan = ScenarioManagerPlanner.CreateDeletePlan(_session.Workbook, scenarioName);
            var deletedName = deletePlan.SelectedScenario?.Name ?? scenarioName ?? "scenario";
            var result = _session.ExecuteScenarioManagerDeletePlan(deletePlan);
            if (!ApplyScenarioManagerResult(result, $"Deleted scenario '{deletedName}'"))
                return;

            RefreshDialogPlan();
            nameBox.Text = CreateScenarioManagerDefaultName(plan.Scenarios);
        }

        void CreateSummaryReport()
        {
            var summaryPlan = ScenarioManagerPlanner.CreateSummaryReportPlan(_session.Workbook);
            var result = _session.ExecuteScenarioManagerSummaryReportPlan(summaryPlan);
            if (!ApplyScenarioManagerResult(
                    result,
                    $"Created Scenario Summary for {summaryPlan.Scenarios.Count} {FormatCountLabel(summaryPlan.Scenarios.Count, "scenario")}"))
                return;

            RefreshDialogPlan(selectedScenarioName);
        }

        scenarioList.SelectionChanged += (_, _) => RefreshSelectionDetails();
        saveButton.Click += (_, _) => SaveCurrentValues();
        showButton.Click += (_, _) => ShowSelectedScenario();
        deleteButton.Click += (_, _) => DeleteSelectedScenario();
        summaryButton.Click += (_, _) => CreateSummaryReport();
        closeButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
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
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                closeButton,
            },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                saveButton,
                showButton,
                deleteButton,
                summaryButton,
            },
        };

        RefreshDialogPlan(selectedScenarioName);
        nameBox.Text = CreateScenarioManagerDefaultName(plan.Scenarios);
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        statusText,
                        selectionText,
                        scenarioList,
                        scenarioDetailsText,
                        CreateScenarioManagerField("Name", nameBox),
                        CreateScenarioManagerField("Comment", commentBox),
                        actionRow,
                        errorText,
                    },
                },
            },
        };
        dialog.Opened += (_, _) =>
        {
            if (plan.Scenarios.Count > 0)
                scenarioList.Focus();
            else
                nameBox.Focus();
        };

        await dialog.ShowDialog(this);
    }

    private IReadOnlyList<ScenarioCellValue> CaptureScenarioManagerChangingCells(GridRange range)
    {
        var sheet = _session.Workbook.GetSheet(range.Start.Sheet) ?? _session.ActiveSheet;
        var values = new List<ScenarioCellValue>();
        foreach (var address in range.AllCells())
            values.Add(new ScenarioCellValue(address, sheet.GetValue(address)));

        return values;
    }

    private static string FormatScenarioManagerSelectionSummary(GridRange range) =>
        $"Current selection: {FormatRangeReference(range)} ({range.CellCount} {FormatCountLabel(range.CellCount, "cell")})";

    private static string FormatScenarioManagerScenarioDetails(ScenarioManagerScenarioChoice? choice)
    {
        if (choice is null)
            return "No scenario selected.";

        var comment = string.IsNullOrWhiteSpace(choice.Comment)
            ? "No comment."
            : choice.Comment.Trim();
        var flags = new List<string>();
        if (choice.Hidden)
            flags.Add("hidden");
        if (choice.Locked)
            flags.Add("locked");

        var flagText = flags.Count == 0
            ? "Visible, editable."
            : string.Join(", ", flags.Select(flag => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(flag))) + ".";
        return string.Join(
            Environment.NewLine,
            $"{choice.Name}: {choice.ChangingCellCount} {FormatCountLabel(choice.ChangingCellCount, "changing cell")}.",
            comment,
            flagText);
    }

    private static string CreateScenarioManagerDefaultName(IReadOnlyList<ScenarioManagerScenarioChoice> scenarios)
    {
        var existingNames = new HashSet<string>(
            scenarios.Select(scenario => scenario.Name),
            StringComparer.OrdinalIgnoreCase);
        var index = scenarios.Count + 1;
        while (true)
        {
            var candidate = $"Scenario {index}";
            if (!existingNames.Contains(candidate))
                return candidate;

            index++;
        }
    }

    private static string FormatCountLabel(long count, string singular) =>
        count == 1 ? singular : $"{singular}s";

    private static StackPanel CreateScenarioManagerField(string label, Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label },
                control,
            },
        };

    private async Task ShowDataTableDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var plan = await ShowDataTableInputDialogAsync();
        if (plan is null)
            return;

        var tableRange = FormatRangeReference(plan.TableRange);
        var result = _session.ExecuteDataTablePlan(plan);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Data Table failed.");
            return;
        }

        RefreshShell($"Created {FormatDataTableMode(plan)} Data Table for {tableRange}");
    }

    private async Task<DataTablePlan?> ShowDataTableInputDialogAsync()
    {
        DataTablePlan? result = null;
        var dialog = new Window
        {
            Title = "Data Table",
            Width = 460,
            Height = 290,
            MinWidth = 380,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "DataTableCompactDialog");

        var rangeText = new TextBlock
        {
            Text = $"Table range: {FormatRangeReference(_session.SelectedRange)}",
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(rangeText, "Data Table range");
        AutomationProperties.SetAutomationId(rangeText, "DataTableRangeSummaryText");
        AutomationProperties.SetHelpText(rangeText, "Shows the selected range used for the Data Table.");

        var rowInputBox = new TextBox
        {
            MinWidth = 240,
        };
        AutomationProperties.SetName(rowInputBox, "Row input cell");
        AutomationProperties.SetAutomationId(rowInputBox, "DataTableRowInputCellBox");
        AutomationProperties.SetHelpText(rowInputBox, "Cell whose value is substituted from the top row.");

        var columnInputBox = new TextBox
        {
            MinWidth = 240,
        };
        AutomationProperties.SetName(columnInputBox, "Column input cell");
        AutomationProperties.SetAutomationId(columnInputBox, "DataTableColumnInputCellBox");
        AutomationProperties.SetHelpText(columnInputBox, "Cell whose value is substituted from the first column.");

        var errorText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(errorText, "Data Table validation");
        AutomationProperties.SetAutomationId(errorText, "DataTableErrorText");
        AutomationProperties.SetHelpText(errorText, "Shows Data Table readiness and validation messages.");

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(okButton, "OK");
        AutomationProperties.SetAutomationId(okButton, "DataTableOkButton");
        AutomationProperties.SetHelpText(okButton, "Create the Data Table.");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(cancelButton, "Cancel");
        AutomationProperties.SetAutomationId(cancelButton, "DataTableCancelButton");
        AutomationProperties.SetHelpText(cancelButton, "Close Data Table without creating it.");

        DataTablePlanResult CreatePlan() =>
            DataTablePlanner.CreatePlan(
                _session.ActiveSheet,
                _session.SelectedRange,
                rowInputBox.Text,
                columnInputBox.Text,
                sheetName => _session.Workbook.GetSheet(sheetName)?.Id);

        void RefreshPlanStatus()
        {
            var planResult = CreatePlan();
            errorText.Text = planResult.IsReady
                ? planResult.StatusText
                : FormatDataTablePlanError(planResult);
        }

        void Accept()
        {
            var planResult = CreatePlan();
            if (!planResult.IsReady || planResult.Plan is null)
            {
                errorText.Text = FormatDataTablePlanError(planResult);
                FocusDataTableErrorField(planResult.Status, rowInputBox, columnInputBox);
                return;
            }

            result = planResult.Plan;
            dialog.Close();
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        rowInputBox.TextChanged += (_, _) => RefreshPlanStatus();
        columnInputBox.TextChanged += (_, _) => RefreshPlanStatus();
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                cancelButton,
                okButton,
            },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        RefreshPlanStatus();
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        rangeText,
                        CreateDataTableField("Row input cell", rowInputBox),
                        CreateDataTableField("Column input cell", columnInputBox),
                        errorText,
                    },
                },
            },
        };
        dialog.Opened += (_, _) =>
        {
            rowInputBox.Focus();
            rowInputBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private static string FormatDataTableMode(DataTablePlan plan) =>
        plan.Mode == DataTablePlanMode.TwoVariable
            ? "two-variable"
            : plan.Orientation == DataTableInputOrientation.Row
                ? "one-variable row-input"
                : "one-variable column-input";

    private static string FormatDataTablePlanError(DataTablePlanResult result) =>
        string.IsNullOrWhiteSpace(result.InvalidText)
            ? result.StatusText
            : $"{result.StatusText} ({result.InvalidText})";

    private static void FocusDataTableErrorField(
        DataTablePlanStatus status,
        TextBox rowInputBox,
        TextBox columnInputBox)
    {
        var target = status switch
        {
            DataTablePlanStatus.InvalidRowInputCell or
            DataTablePlanStatus.RowInputCellInsideTableRange => rowInputBox,
            DataTablePlanStatus.InvalidColumnInputCell or
            DataTablePlanStatus.ColumnInputCellInsideTableRange or
            DataTablePlanStatus.InputCellsMustBeDifferent => columnInputBox,
            _ => rowInputBox
        };
        target.Focus();
        target.SelectAll();
    }

    private static StackPanel CreateDataTableField(string label, Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label },
                control,
            },
        };

    private async Task ShowForecastSheetDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var plan = await ShowForecastSheetInputDialogAsync();
        if (plan is null)
            return;

        var sourceRange = FormatRangeReference(plan.SourceRange ?? _session.SelectedRange);
        var result = _session.ExecuteForecastSheetPlan(plan);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Forecast Sheet failed.");
            return;
        }

        RefreshShell($"Created Forecast Sheet from {sourceRange}");
    }

    private async Task<ForecastSheetPlan?> ShowForecastSheetInputDialogAsync()
    {
        ForecastSheetPlan? result = null;
        var dialog = new Window
        {
            Title = "Forecast Sheet",
            Width = 420,
            Height = 250,
            MinWidth = 360,
            MinHeight = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ForecastSheetCompactDialog");

        var sourceRangeText = new TextBlock
        {
            Text = $"Source range: {FormatRangeReference(_session.SelectedRange)}",
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(sourceRangeText, "Forecast source range");
        AutomationProperties.SetAutomationId(sourceRangeText, "ForecastSheetSourceRangeSummaryText");
        AutomationProperties.SetHelpText(sourceRangeText, "Shows the selected source range for the forecast.");

        var periodsBox = new TextBox
        {
            Text = ForecastSheetPlanner.DefaultForecastPeriods.ToString(CultureInfo.InvariantCulture),
            MinWidth = 160,
        };
        AutomationProperties.SetName(periodsBox, "Forecast periods");
        AutomationProperties.SetAutomationId(periodsBox, "ForecastPeriodsBox");
        AutomationProperties.SetHelpText(periodsBox, "Enter the positive whole number of periods to forecast.");

        var errorText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(errorText, "Forecast Sheet validation");
        AutomationProperties.SetAutomationId(errorText, "ForecastSheetErrorText");
        AutomationProperties.SetHelpText(errorText, "Shows Forecast Sheet readiness and validation messages.");

        var createButton = new Button
        {
            Content = "Create",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(createButton, "Create");
        AutomationProperties.SetAutomationId(createButton, "ForecastSheetCreateButton");
        AutomationProperties.SetHelpText(createButton, "Create the Forecast Sheet.");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(cancelButton, "Cancel");
        AutomationProperties.SetAutomationId(cancelButton, "ForecastSheetCancelButton");
        AutomationProperties.SetHelpText(cancelButton, "Close Forecast Sheet without creating it.");

        ForecastSheetPlan CreatePlan() =>
            ForecastSheetPlanner.CreatePlan(
                _session.Workbook,
                _session.SelectedRange,
                periodsBox.Text);

        void RefreshPlanStatus()
        {
            var forecastPlan = CreatePlan();
            errorText.Text = forecastPlan.IsReady
                ? forecastPlan.StatusText
                : FormatForecastSheetPlanError(forecastPlan);
        }

        void Accept()
        {
            var forecastPlan = CreatePlan();
            if (!forecastPlan.IsReady)
            {
                errorText.Text = FormatForecastSheetPlanError(forecastPlan);
                periodsBox.Focus();
                periodsBox.SelectAll();
                return;
            }

            result = forecastPlan;
            dialog.Close();
        }

        createButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        periodsBox.TextChanged += (_, _) => RefreshPlanStatus();
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                cancelButton,
                createButton,
            },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        RefreshPlanStatus();
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        sourceRangeText,
                        CreateForecastSheetField("Forecast periods", periodsBox),
                        errorText,
                    },
                },
            },
        };
        dialog.Opened += (_, _) =>
        {
            periodsBox.Focus();
            periodsBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private static string FormatForecastSheetPlanError(ForecastSheetPlan plan) =>
        string.IsNullOrWhiteSpace(plan.InvalidText)
            ? plan.StatusText
            : $"{plan.StatusText} ({plan.InvalidText})";

    private static StackPanel CreateForecastSheetField(string label, Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label },
                control,
            },
        };

    private async Task ShowDataValidationDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var selection = await ShowDataValidationInputDialogAsync();
        if (selection is null)
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        if (selection.Action == DataValidationDialogAction.Clear)
        {
            var clearResult = _session.ClearSelectedRangeDataValidation();
            if (!clearResult.Success)
            {
                ShowEditIssue(clearResult.ErrorMessage ?? "Clear Data Validation failed.");
                return;
            }

            RefreshShell(clearResult.Mutated
                ? $"Cleared data validation from {rangeReference}"
                : $"No data validation to clear from {rangeReference}");
            return;
        }

        if (selection.Rule is not { } rule)
            return;

        var result = _session.ApplyDataValidationToSelectedRange(rule);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Data Validation failed.");
            return;
        }

        RefreshShell(result.Mutated
            ? $"Applied {DataValidationPresetPlanner.GetDisplayName(rule.Type)} data validation to {rangeReference}"
            : $"Data validation already matches {rangeReference}");
    }

    private async Task ShowDataValidationPreviewDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        var preview = DataValidationPreviewPlanner.Create(
            _session.Workbook,
            _session.ActiveSheet,
            _session.ActiveCell,
            _session.SelectedRange);
        await ShowTextDialogAsync("Data Validation Preview", preview.Text, 520, 360);
    }

    private async Task<DataValidationDialogResult?> ShowDataValidationInputDialogAsync()
    {
        return await ShowDataValidationInputDialogAsync(null);
    }

    private async Task<DataValidationDialogResult?> ShowDataValidationInputDialogAsync(Action<DataValidationDialogSmokeProbe>? launchSmokeProbe)
    {
        DataValidationDialogResult? result = null;
        var summary = DataValidationPresetPlanner.CreateSelectionSummary(
            _session.Workbook,
            _session.ActiveSheet,
            _session.ActiveCell,
            _session.SelectedRange);
        var dialog = new Window
        {
            Title = "Data Validation",
            Width = 540,
            Height = 560,
            MinWidth = 460,
            MinHeight = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "DataValidationCompactDialog");

        var typeChoices = CreateDataValidationTypeChoices();
        var operatorChoices = CreateDataValidationOperatorChoices();
        var alertStyleChoices = CreateDataValidationAlertStyleChoices();
        var activeRule = summary.ActiveCellRule;
        var activeTypeChoice = activeRule is null
            ? null
            : FindDataValidationTypeChoice(typeChoices, activeRule.Type);
        var initialType = activeTypeChoice?.Type ?? DvType.WholeNumber;
        var initialRule = activeRule is not null && activeTypeChoice is not null
            ? activeRule
            : CreateDefaultDataValidationRule(initialType, _session.SelectedRange);

        var summaryText = new TextBlock
        {
            Text = summary.Text,
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(summaryText, "DataValidationSelectionSummaryText");

        var typeBox = new ComboBox
        {
            ItemsSource = typeChoices,
            MinWidth = 220,
        };
        AutomationProperties.SetName(typeBox, "Allow");
        AutomationProperties.SetAutomationId(typeBox, "DataValidationTypeBox");

        var operatorBox = new ComboBox
        {
            ItemsSource = operatorChoices,
            MinWidth = 220,
        };
        AutomationProperties.SetName(operatorBox, "Data");
        AutomationProperties.SetAutomationId(operatorBox, "DataValidationOperatorBox");
        var operatorField = CreateDataValidationField("Data", operatorBox);

        var formula1Label = new TextBlock();
        var formula1Box = new TextBox
        {
            MinWidth = 240,
        };
        AutomationProperties.SetName(formula1Box, "Value");
        AutomationProperties.SetAutomationId(formula1Box, "DataValidationFormula1Box");
        var formula1Field = CreateDataValidationField(formula1Label, formula1Box);

        var formula2Label = new TextBlock();
        var formula2Box = new TextBox
        {
            MinWidth = 240,
        };
        AutomationProperties.SetName(formula2Box, "Maximum");
        AutomationProperties.SetAutomationId(formula2Box, "DataValidationFormula2Box");
        var formula2Field = CreateDataValidationField(formula2Label, formula2Box);

        var allowBlankBox = new CheckBox
        {
            Content = "Allow blank",
        };
        AutomationProperties.SetAutomationId(allowBlankBox, "DataValidationAllowBlankBox");

        var showDropdownBox = new CheckBox
        {
            Content = "In-cell dropdown",
        };
        AutomationProperties.SetAutomationId(showDropdownBox, "DataValidationShowDropdownBox");

        var showInputMessageBox = new CheckBox
        {
            Content = "Show input message",
        };
        AutomationProperties.SetAutomationId(showInputMessageBox, "DataValidationShowInputMessageBox");

        var promptTitleBox = new TextBox
        {
            MinWidth = 240,
        };
        AutomationProperties.SetName(promptTitleBox, "Input title");
        AutomationProperties.SetAutomationId(promptTitleBox, "DataValidationPromptTitleBox");

        var promptMessageBox = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 54,
            MinWidth = 240,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(promptMessageBox, "Input message");
        AutomationProperties.SetAutomationId(promptMessageBox, "DataValidationPromptMessageBox");

        var showErrorMessageBox = new CheckBox
        {
            Content = "Show error alert",
        };
        AutomationProperties.SetAutomationId(showErrorMessageBox, "DataValidationShowErrorMessageBox");

        var alertStyleBox = new ComboBox
        {
            ItemsSource = alertStyleChoices,
            MinWidth = 220,
        };
        AutomationProperties.SetName(alertStyleBox, "Style");
        AutomationProperties.SetAutomationId(alertStyleBox, "DataValidationAlertStyleBox");

        var errorTitleBox = new TextBox
        {
            MinWidth = 240,
        };
        AutomationProperties.SetName(errorTitleBox, "Error title");
        AutomationProperties.SetAutomationId(errorTitleBox, "DataValidationErrorTitleBox");

        var errorMessageBox = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 54,
            MinWidth = 240,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(errorMessageBox, "Error message");
        AutomationProperties.SetAutomationId(errorMessageBox, "DataValidationErrorMessageBox");

        var errorText = new TextBlock
        {
            Foreground = Brush(143, 74, 18),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(errorText, "DataValidationErrorText");

        var applyButton = new Button
        {
            Content = "Apply",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(applyButton, "DataValidationApplyButton");

        var clearButton = new Button
        {
            Content = "Clear Validation",
            MinWidth = 112,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(clearButton, "DataValidationClearButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(cancelButton, "DataValidationCancelButton");

        DvType SelectedType() =>
            typeBox.SelectedItem is DataValidationTypeChoice choice
                ? choice.Type
                : DvType.WholeNumber;

        DvOperator SelectedOperator() =>
            operatorBox.SelectedItem is DataValidationOperatorChoice choice
                ? choice.Operator
                : GetDefaultDataValidationOperator(SelectedType());

        DvAlertStyle SelectedAlertStyle() =>
            alertStyleBox.SelectedItem is DataValidationAlertStyleChoice choice
                ? choice.AlertStyle
                : DvAlertStyle.Stop;

        void SelectOperator(DvOperator op)
        {
            operatorBox.SelectedItem = FindDataValidationOperatorChoice(operatorChoices, op);
        }

        void LoadRule(DataValidation rule)
        {
            typeBox.SelectedItem = FindDataValidationTypeChoice(typeChoices, rule.Type) ?? typeChoices[0];
            SelectOperator(rule.Operator);
            formula1Box.Text = rule.Formula1 ?? "";
            formula2Box.Text = rule.Formula2 ?? "";
            allowBlankBox.IsChecked = rule.AllowBlank;
            showDropdownBox.IsChecked = rule.ShowDropdown;
            showInputMessageBox.IsChecked = rule.ShowInputMessage;
            showErrorMessageBox.IsChecked = rule.ShowErrorMessage;
            alertStyleBox.SelectedItem = FindDataValidationAlertStyleChoice(alertStyleChoices, rule.AlertStyle);
            promptTitleBox.Text = rule.PromptTitle ?? "";
            promptMessageBox.Text = rule.PromptMessage ?? "";
            errorTitleBox.Text = rule.ErrorTitle ?? "";
            errorMessageBox.Text = rule.ErrorMessage ?? "";
        }

        void UpdateCriteriaVisibility()
        {
            var type = SelectedType();
            var op = SelectedOperator();
            var showSecondFormula = DataValidationPresetPlanner.RequiresSecondFormula(type, op);
            var isList = type == DvType.List;

            formula1Label.Text = isList
                ? "Source"
                : showSecondFormula
                    ? "Minimum"
                    : "Value";
            AutomationProperties.SetName(formula1Box, formula1Label.Text);
            AutomationProperties.SetHelpText(
                formula1Box,
                isList
                    ? "List source range or comma-separated values."
                    : showSecondFormula
                        ? "Minimum value for the validation rule."
                        : "Value for the validation rule.");
            formula2Label.Text = "Maximum";
            operatorField.IsVisible = !isList;
            formula2Field.IsVisible = showSecondFormula;
            showDropdownBox.IsVisible = isList;
        }

        void RefreshMessageEditorStates()
        {
            var inputEnabled = showInputMessageBox.IsChecked == true;
            promptTitleBox.IsEnabled = inputEnabled;
            promptMessageBox.IsEnabled = inputEnabled;

            var errorEnabled = showErrorMessageBox.IsChecked == true;
            alertStyleBox.IsEnabled = errorEnabled;
            errorTitleBox.IsEnabled = errorEnabled;
            errorMessageBox.IsEnabled = errorEnabled;
        }

        void Accept()
        {
            var type = SelectedType();
            var op = SelectedOperator();
            if (!TryValidateDataValidationCriteria(type, op, formula1Box.Text, formula2Box.Text, out var message))
            {
                errorText.Text = message;
                return;
            }

            var rule = DataValidationPresetPlanner.CreateDefaultRule(type, _session.SelectedRange);
            rule.Operator = op;
            rule.Formula1 = formula1Box.Text?.Trim() ?? "";
            rule.Formula2 = DataValidationPresetPlanner.RequiresSecondFormula(type, op)
                ? formula2Box.Text?.Trim() ?? ""
                : "";
            rule.AllowBlank = allowBlankBox.IsChecked == true;
            rule.ShowDropdown = type == DvType.List && showDropdownBox.IsChecked == true;
            rule.ShowInputMessage = showInputMessageBox.IsChecked == true;
            rule.ShowErrorMessage = showErrorMessageBox.IsChecked == true;
            rule.AlertStyle = SelectedAlertStyle();
            rule.PromptTitle = promptTitleBox.Text?.Trim() ?? "";
            rule.PromptMessage = promptMessageBox.Text?.Trim() ?? "";
            rule.ErrorTitle = errorTitleBox.Text?.Trim() ?? "";
            rule.ErrorMessage = errorMessageBox.Text?.Trim() ?? "";

            result = new DataValidationDialogResult(DataValidationDialogAction.Apply, rule);
            dialog.Close();
        }

        applyButton.Click += (_, _) => Accept();
        clearButton.Click += (_, _) =>
        {
            result = new DataValidationDialogResult(DataValidationDialogAction.Clear, null);
            dialog.Close();
        };
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

        LoadRule(initialRule);
        UpdateCriteriaVisibility();
        RefreshMessageEditorStates();

        typeBox.SelectionChanged += (_, _) =>
        {
            var type = SelectedType();
            LoadRule(CreateDefaultDataValidationRule(type, _session.SelectedRange));
            UpdateCriteriaVisibility();
            RefreshMessageEditorStates();
        };
        operatorBox.SelectionChanged += (_, _) => UpdateCriteriaVisibility();
        showInputMessageBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                RefreshMessageEditorStates();
        };
        showErrorMessageBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                RefreshMessageEditorStates();
        };

        var criteriaPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateDataValidationField("Allow", typeBox),
                operatorField,
                formula1Field,
                formula2Field,
                allowBlankBox,
                showDropdownBox,
            },
        };

        var messagePanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                showInputMessageBox,
                CreateDataValidationField("Input title", promptTitleBox),
                CreateDataValidationField("Input message", promptMessageBox),
                showErrorMessageBox,
                CreateDataValidationField("Style", alertStyleBox),
                CreateDataValidationField("Error title", errorTitleBox),
                CreateDataValidationField("Error message", errorMessageBox),
            },
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                clearButton,
                cancelButton,
                applyButton,
            },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            summaryText,
                            criteriaPanel,
                            messagePanel,
                            errorText,
                        },
                    },
                },
            },
        };
        dialog.Opened += (_, _) => typeBox.Focus();
        if (launchSmokeProbe is not null)
        {
            dialog.Opened += (_, _) =>
            {
                RunLaunchSmokeDialogProbe(
                    dialog,
                    () => launchSmokeProbe(new DataValidationDialogSmokeProbe(
                        dialog,
                        summaryText,
                        typeBox,
                        operatorBox,
                        formula1Box,
                        formula2Box,
                        allowBlankBox,
                        showDropdownBox,
                        showInputMessageBox,
                        promptTitleBox,
                        promptMessageBox,
                        showErrorMessageBox,
                        alertStyleBox,
                        errorTitleBox,
                        errorMessageBox,
                        applyButton,
                        clearButton,
                        cancelButton)));
            };
        }

        await dialog.ShowDialog(this);
        return result;
    }

    private static IReadOnlyList<DataValidationTypeChoice> CreateDataValidationTypeChoices() =>
        DataValidationPresetPlanner.GetRuleTypeMetadata()
            .Where(metadata => metadata.Type is DvType.WholeNumber or DvType.List or DvType.TextLength)
            .Select(metadata => new DataValidationTypeChoice(metadata.Type, metadata.DisplayName))
            .ToArray();

    private static IReadOnlyList<DataValidationOperatorChoice> CreateDataValidationOperatorChoices() =>
    [
        new(DvOperator.Between, "Between"),
        new(DvOperator.NotBetween, "Not between"),
        new(DvOperator.Equal, "Equal to"),
        new(DvOperator.NotEqual, "Not equal to"),
        new(DvOperator.GreaterThan, "Greater than"),
        new(DvOperator.LessThan, "Less than"),
        new(DvOperator.GreaterThanOrEqual, "Greater than or equal to"),
        new(DvOperator.LessThanOrEqual, "Less than or equal to"),
    ];

    private static IReadOnlyList<DataValidationAlertStyleChoice> CreateDataValidationAlertStyleChoices() =>
    [
        new(DvAlertStyle.Stop, "Stop"),
        new(DvAlertStyle.Warning, "Warning"),
        new(DvAlertStyle.Information, "Information"),
    ];

    private static DataValidationTypeChoice? FindDataValidationTypeChoice(
        IReadOnlyList<DataValidationTypeChoice> choices,
        DvType type)
    {
        foreach (var choice in choices)
        {
            if (choice.Type == type)
                return choice;
        }

        return null;
    }

    private static DataValidationOperatorChoice FindDataValidationOperatorChoice(
        IReadOnlyList<DataValidationOperatorChoice> choices,
        DvOperator op)
    {
        foreach (var choice in choices)
        {
            if (choice.Operator == op)
                return choice;
        }

        return choices[0];
    }

    private static DataValidationAlertStyleChoice FindDataValidationAlertStyleChoice(
        IReadOnlyList<DataValidationAlertStyleChoice> choices,
        DvAlertStyle alertStyle)
    {
        foreach (var choice in choices)
        {
            if (choice.AlertStyle == alertStyle)
                return choice;
        }

        return choices[0];
    }

    private static DataValidation CreateDefaultDataValidationRule(DvType type, GridRange selectedRange)
    {
        var rule = DataValidationPresetPlanner.CreateDefaultRule(type, selectedRange);
        rule.Operator = GetDefaultDataValidationOperator(type);
        rule.Formula1 = type switch
        {
            DvType.List => "Yes,No",
            DvType.TextLength => "50",
            _ => "1",
        };
        rule.Formula2 = type == DvType.WholeNumber ? "100" : "";
        rule.ShowDropdown = type == DvType.List;
        return rule;
    }

    private static DvOperator GetDefaultDataValidationOperator(DvType type) =>
        type == DvType.TextLength
            ? DvOperator.LessThanOrEqual
            : DvOperator.Between;

    private static bool TryValidateDataValidationCriteria(
        DvType type,
        DvOperator op,
        string? formula1,
        string? formula2,
        out string errorMessage)
    {
        var first = formula1?.Trim() ?? "";
        var second = formula2?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(first))
        {
            errorMessage = type == DvType.List
                ? "List source is required."
                : "Value is required.";
            return false;
        }

        if (DataValidationPresetPlanner.RequiresSecondFormula(type, op) &&
            string.IsNullOrWhiteSpace(second))
        {
            errorMessage = "Maximum is required.";
            return false;
        }

        if (type == DvType.List)
        {
            if (HasDataValidationListSource(first))
            {
                errorMessage = "";
                return true;
            }

            errorMessage = "List source must contain at least one item or range reference.";
            return false;
        }

        if (type == DvType.WholeNumber)
        {
            return TryValidateIntegralDataValidationCriterion(first, allowNegative: true, out errorMessage) &&
                (!DataValidationPresetPlanner.RequiresSecondFormula(type, op) ||
                    TryValidateIntegralDataValidationCriterion(second, allowNegative: true, out errorMessage));
        }

        if (type == DvType.TextLength)
        {
            return TryValidateIntegralDataValidationCriterion(first, allowNegative: false, out errorMessage) &&
                (!DataValidationPresetPlanner.RequiresSecondFormula(type, op) ||
                    TryValidateIntegralDataValidationCriterion(second, allowNegative: false, out errorMessage));
        }

        errorMessage = "";
        return true;
    }

    private static bool HasDataValidationListSource(string text) =>
        text.TrimStart().StartsWith('=') ||
        text.Split(',').Any(static item => item.Trim().Trim('"').Length > 0);

    private static bool TryValidateIntegralDataValidationCriterion(
        string text,
        bool allowNegative,
        out string errorMessage)
    {
        if (text.TrimStart().StartsWith('='))
        {
            errorMessage = "";
            return true;
        }

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) &&
            !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            errorMessage = "Value must be a whole number or formula.";
            return false;
        }

        if (!allowNegative && value < 0)
        {
            errorMessage = "Text length must be zero or greater.";
            return false;
        }

        errorMessage = "";
        return true;
    }

    private static StackPanel CreateDataValidationField(string label, Control control) =>
        CreateDataValidationField(new TextBlock { Text = label }, control);

    private static StackPanel CreateDataValidationField(TextBlock label, Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                label,
                control,
            },
        };

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

    private async void MergeAndCenterButton_Click(object? sender, RoutedEventArgs e)
    {
        await MergeAndCenterSelectedRangeAsync();
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
        var cutResult = _session.TryCutSelectedRangeText();
        if (!cutResult.Success)
        {
            ShowEditIssue(cutResult.ErrorMessage ?? "Cut failed.");
            return;
        }

        await clipboard.SetTextAsync(cutResult.Text);
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
        var copyResult = _session.TryCopySelectedRangeText();
        if (!copyResult.Success)
        {
            ShowEditIssue(copyResult.ErrorMessage ?? "Copy failed.");
            return;
        }

        await clipboard.SetTextAsync(copyResult.Text);
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
        ToggleSelectedRangeBold(trackLaunchSmokeLiveCommandKey: false);
    }

    private void ToggleSelectedRangeBold(bool trackLaunchSmokeLiveCommandKey)
    {
        var before = _session.IsSelectedRangeStartBold;
        ApplySelectedRangeBold(!before);
        if (trackLaunchSmokeLiveCommandKey)
            RecordLaunchSmokeLiveCommandKey(Key.B, before, _session.IsSelectedRangeStartBold);
    }

    private void ToggleSelectedRangeItalic()
    {
        ToggleSelectedRangeItalic(trackLaunchSmokeLiveCommandKey: false);
    }

    private void ToggleSelectedRangeItalic(bool trackLaunchSmokeLiveCommandKey)
    {
        var before = _session.IsSelectedRangeStartItalic;
        ApplySelectedRangeItalic(!before);
        if (trackLaunchSmokeLiveCommandKey)
            RecordLaunchSmokeLiveCommandKey(Key.I, before, _session.IsSelectedRangeStartItalic);
    }

    private void ToggleSelectedRangeUnderline()
    {
        ToggleSelectedRangeUnderline(trackLaunchSmokeLiveCommandKey: false);
    }

    private void ToggleSelectedRangeUnderline(bool trackLaunchSmokeLiveCommandKey)
    {
        var before = _session.IsSelectedRangeStartUnderline;
        ApplySelectedRangeUnderline(!before);
        if (trackLaunchSmokeLiveCommandKey)
            RecordLaunchSmokeLiveCommandKey(Key.U, before, _session.IsSelectedRangeStartUnderline);
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

    private async Task MergeAndCenterSelectedRangeAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var contentResolution = MergeCellContentResolution.KeepFirstCell;
        var contentPlan = CellMergePlanner.AnalyzeContent(_session.ActiveSheet, range);
        if (contentPlan.WouldLoseContent)
        {
            var choice = await ShowMergeCellsContentWarningDialogAsync(contentPlan);
            if (choice == MergeCellsWarningChoice.Cancel)
            {
                RefreshShell(_statusText.Text ?? "Ready");
                return;
            }

            contentResolution = choice == MergeCellsWarningChoice.ConcatenateAllCells
                ? MergeCellContentResolution.ConcatenateAllCells
                : MergeCellContentResolution.KeepFirstCell;
        }

        var rangeReference = FormatRangeReference(range);
        var result = _session.MergeAndCenterSelectedRange(contentResolution);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Merge & Center failed.");
            return;
        }

        RefreshShell($"Merged and centered {rangeReference}");
    }

    private async Task<MergeCellsWarningChoice> ShowMergeCellsContentWarningDialogAsync(MergeCellContentPlan contentPlan)
    {
        var choice = MergeCellsWarningChoice.Cancel;
        var dialog = new Window
        {
            Title = "Merge Cells",
            Width = 460,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        AutomationProperties.SetAutomationId(dialog, "MergeCellsContentWarningDialog");

        var root = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12
        };

        root.Children.Add(new TextBlock
        {
            Text = "Merging cells can discard cell contents.",
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(new TextBlock
        {
            Text = "Only the first cell is kept by default. Choose how FreeX should handle the other selected contents.",
            TextWrapping = TextWrapping.Wrap
        });

        if (contentPlan.Entries.Count > 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = $"Non-empty cells: {contentPlan.Entries.Count}",
                Foreground = Brushes.DimGray
            });
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var keepFirstButton = new Button
        {
            Content = "Keep only first cell",
            MinWidth = 136,
            IsDefault = true
        };
        AutomationProperties.SetAutomationId(keepFirstButton, "MergeCellsKeepFirstButton");
        keepFirstButton.Click += (_, _) =>
        {
            choice = MergeCellsWarningChoice.KeepFirstCell;
            dialog.Close();
        };

        var concatenateButton = new Button
        {
            Content = "Concatenate all cells",
            MinWidth = 136
        };
        AutomationProperties.SetAutomationId(concatenateButton, "MergeCellsConcatenateButton");
        concatenateButton.Click += (_, _) =>
        {
            choice = MergeCellsWarningChoice.ConcatenateAllCells;
            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 82,
            IsCancel = true
        };
        AutomationProperties.SetAutomationId(cancelButton, "MergeCellsCancelButton");
        cancelButton.Click += (_, _) =>
        {
            choice = MergeCellsWarningChoice.Cancel;
            dialog.Close();
        };

        buttonRow.Children.Add(keepFirstButton);
        buttonRow.Children.Add(concatenateButton);
        buttonRow.Children.Add(cancelButton);
        root.Children.Add(buttonRow);

        dialog.Content = root;
        dialog.Opened += (_, _) => cancelButton.Focus();
        await dialog.ShowDialog(this);
        return choice;
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
        if (!TrySelectDroppedWorkbookPath(e, out var path, out var storageItem, out var message))
        {
            ShowOpenIssue(message);
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        var fileAccessIdentity = await _workbookFileAccessService.CreateIdentityAsync(path!, storageItem);
        await OpenWorkbookPathAsync(path!, fileAccessIdentity);
    }

    public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files)
    {
        if (!TrySelectOpenableLocalWorkbookPath(files, out var path, out var storageItem, out var message))
        {
            ShowOpenIssue(message);
            return;
        }

        var fileAccessIdentity = await _workbookFileAccessService.CreateIdentityAsync(path!, storageItem);
        await OpenWorkbookPathAsync(path!, fileAccessIdentity);
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

        var hasFormatCellsDialog = false;
        var hasFormatCellsDialogTabStrip = false;
        var hasFormatCellsDialogDefaultNumberTab = false;
        var hasFormatCellsDialogNumberControls = false;
        var hasFormatCellsDialogActionButtons = false;
        var hasFormatCellsDialogCompactLayout = false;
        var formatCellsDialogResult = await ShowFormatCellsInputDialogAsync(probe =>
        {
            hasFormatCellsDialog = HasLaunchSmokeDialog(probe.Dialog, "Format Cells");
            hasFormatCellsDialogTabStrip =
                HasLaunchSmokeAutomationId(probe.TabStrip, "FormatCellsTabStrip") &&
                HasLaunchSmokeAutomationId(probe.NumberTab, "FormatCellsNumberTab") &&
                HasLaunchSmokeAutomationId(probe.AlignmentTab, "FormatCellsAlignmentTab") &&
                HasLaunchSmokeAutomationId(probe.FontTab, "FormatCellsFontTab") &&
                HasLaunchSmokeAutomationId(probe.FillTab, "FormatCellsFillTab") &&
                HasLaunchSmokeAutomationId(probe.BorderTab, "FormatCellsBorderTab") &&
                HasLaunchSmokeAutomationId(probe.ProtectionTab, "FormatCellsProtectionTab");
            hasFormatCellsDialogDefaultNumberTab =
                probe.TabStrip.SelectedIndex == 0 &&
                HasLaunchSmokeAutomationId(probe.NumberTab, "FormatCellsNumberTab");
            hasFormatCellsDialogNumberControls =
                HasLaunchSmokeAutomationId(probe.NumberCategoryList, "FormatCellsNumberCategoryList") &&
                HasLaunchSmokeAutomationId(probe.NumberFormatBox, "FormatCellsNumberFormatBox") &&
                HasLaunchSmokeAutomationId(probe.NumberPreview, "FormatCellsNumberPreview") &&
                probe.NumberFormatBox.MinWidth >= 260;
            hasFormatCellsDialogActionButtons =
                HasLaunchSmokeButton(probe.OkButton, "FormatCellsOkButton", "OK") &&
                HasLaunchSmokeButton(probe.CancelButton, "FormatCellsCancelButton", "Cancel");
            hasFormatCellsDialogCompactLayout = HasLaunchSmokeCompactDialog(probe.Dialog, width: 560, height: 560, minWidth: 480, minHeight: 500);
        });

        var hasSortDialog = false;
        var hasSortDialogSortOnControls = false;
        var hasSortDialogColorControls = false;
        var hasSortDialogActionButtons = false;
        var hasSortDialogCompactLayout = false;
        var sortDialogResult = await ShowSortInputDialogAsync(probe =>
        {
            hasSortDialog = HasLaunchSmokeDialog(probe.Dialog, "Sort");
            hasSortDialogSortOnControls =
                HasLaunchSmokeComboBox(probe.SortOnBox, "SortLevel1SortOnBox", "Sort On") &&
                probe.SortOnBox.MinWidth >= 120 &&
                string.Equals(probe.SortOnBox.SelectedItem?.ToString(), SortDialogPlannerText.Default.SortOnCellValues, StringComparison.Ordinal);
            hasSortDialogColorControls =
                HasLaunchSmokeComboBox(probe.ColorBox, "SortLevel1ColorBox", "Color") &&
                probe.ColorBox.MinWidth >= 105 &&
                !probe.ColorBox.IsEnabled &&
                string.Equals(probe.ColorBox.SelectedItem?.ToString(), "None", StringComparison.Ordinal);
            hasSortDialogActionButtons =
                HasLaunchSmokeCheckBox(probe.HeadersCheckBox, "SortHeadersCheckBox", "My data has headers") &&
                HasLaunchSmokeAutomationId(probe.LevelsPanel, "SortLevelsPanel") &&
                HasLaunchSmokeButton(probe.AddLevelButton, "SortAddLevelButton", "Add Level") &&
                HasLaunchSmokeButton(probe.OkButton, "SortOkButton", "OK") &&
                HasLaunchSmokeButton(probe.CancelButton, "SortCancelButton", "Cancel");
            hasSortDialogCompactLayout = HasLaunchSmokeCompactDialog(probe.Dialog, width: 760, height: 430, minWidth: 700, minHeight: 360);
        });

        var dropdown = CreateDataValidationDropdown(new DataValidationDropdownPlan(
            ["Yes", "No"],
            "Yes",
            new DataValidationDropdownBounds(12, 16, 48, 18)));
        var hasDataValidationDropdownControl =
            HasLaunchSmokeComboBox(dropdown, "WorksheetDataValidationDropdown", "Data validation list") &&
            string.Equals(
                AutomationProperties.GetHelpText(dropdown),
                "Pick a permitted value for the active cell.",
                StringComparison.Ordinal) &&
            dropdown.Width == 48 &&
            dropdown.Height == 18 &&
            dropdown.MinWidth == DataValidationDropdownPlanner.MinimumWidth &&
            dropdown.MinHeight == DataValidationDropdownPlanner.MinimumHeight;
        var hasDataValidationDropdownItems =
            dropdown.ItemsSource is IEnumerable<string> dropdownItems &&
            dropdownItems.SequenceEqual(["Yes", "No"], StringComparer.Ordinal) &&
            string.Equals(dropdown.SelectedItem?.ToString(), "Yes", StringComparison.Ordinal);

        var hasDataValidationDialog = false;
        var hasDataValidationDialogCriteriaControls = false;
        var hasDataValidationDialogMessageControls = false;
        var hasDataValidationDialogActionButtons = false;
        var hasDataValidationDialogCompactLayout = false;
        var dataValidationDialogResult = await ShowDataValidationInputDialogAsync(probe =>
        {
            hasDataValidationDialog = HasLaunchSmokeDialog(probe.Dialog, "Data Validation");
            hasDataValidationDialogCriteriaControls =
                HasLaunchSmokeAutomationId(probe.SummaryText, "DataValidationSelectionSummaryText") &&
                HasLaunchSmokeComboBox(probe.TypeBox, "DataValidationTypeBox", "Allow") &&
                HasLaunchSmokeComboBox(probe.OperatorBox, "DataValidationOperatorBox", "Data") &&
                HasLaunchSmokeAutomationId(probe.Formula1Box, "DataValidationFormula1Box") &&
                HasLaunchSmokeAutomationId(probe.Formula2Box, "DataValidationFormula2Box") &&
                HasLaunchSmokeCheckBox(probe.AllowBlankBox, "DataValidationAllowBlankBox", "Allow blank") &&
                HasLaunchSmokeCheckBox(probe.ShowDropdownBox, "DataValidationShowDropdownBox", "In-cell dropdown") &&
                !probe.ShowDropdownBox.IsVisible &&
                string.Equals(probe.TypeBox.SelectedItem?.ToString(), "Whole number", StringComparison.Ordinal) &&
                string.Equals(probe.Formula1Box.Text, "1", StringComparison.Ordinal) &&
                string.Equals(probe.Formula2Box.Text, "100", StringComparison.Ordinal);
            hasDataValidationDialogMessageControls =
                HasLaunchSmokeCheckBox(probe.ShowInputMessageBox, "DataValidationShowInputMessageBox", "Show input message") &&
                HasLaunchSmokeAutomationId(probe.PromptTitleBox, "DataValidationPromptTitleBox") &&
                HasLaunchSmokeAutomationId(probe.PromptMessageBox, "DataValidationPromptMessageBox") &&
                HasLaunchSmokeCheckBox(probe.ShowErrorMessageBox, "DataValidationShowErrorMessageBox", "Show error alert") &&
                HasLaunchSmokeComboBox(probe.AlertStyleBox, "DataValidationAlertStyleBox", "Style") &&
                HasLaunchSmokeAutomationId(probe.ErrorTitleBox, "DataValidationErrorTitleBox") &&
                HasLaunchSmokeAutomationId(probe.ErrorMessageBox, "DataValidationErrorMessageBox") &&
                string.Equals(probe.AlertStyleBox.SelectedItem?.ToString(), "Stop", StringComparison.Ordinal);
            hasDataValidationDialogActionButtons =
                HasLaunchSmokeButton(probe.ApplyButton, "DataValidationApplyButton", "Apply") &&
                HasLaunchSmokeButton(probe.ClearButton, "DataValidationClearButton", "Clear Validation") &&
                HasLaunchSmokeButton(probe.CancelButton, "DataValidationCancelButton", "Cancel");
            hasDataValidationDialogCompactLayout = HasLaunchSmokeCompactDialog(probe.Dialog, width: 540, height: 560, minWidth: 460, minHeight: 440);
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
            goToSpecialDialogResult is null,
            hasFormatCellsDialog,
            hasFormatCellsDialogTabStrip,
            hasFormatCellsDialogDefaultNumberTab,
            hasFormatCellsDialogNumberControls,
            hasFormatCellsDialogActionButtons,
            hasFormatCellsDialogCompactLayout,
            formatCellsDialogResult is null,
            hasSortDialog,
            hasSortDialogSortOnControls,
            hasSortDialogColorControls,
            hasSortDialogActionButtons,
            hasSortDialogCompactLayout,
            sortDialogResult is null,
            hasDataValidationDropdownControl,
            hasDataValidationDropdownItems,
            hasDataValidationDialog,
            hasDataValidationDialogCriteriaControls,
            hasDataValidationDialogMessageControls,
            hasDataValidationDialogActionButtons,
            hasDataValidationDialogCompactLayout,
            dataValidationDialogResult is null);
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

    private static bool HasLaunchSmokeComboBox(ComboBox comboBox, string automationId, string name) =>
        HasLaunchSmokeAutomationId(comboBox, automationId) &&
        string.Equals(AutomationProperties.GetName(comboBox), name, StringComparison.Ordinal);

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

    internal MacOsLaunchSmokeLiveCommandKeySnapshot BeginLaunchSmokeLiveCommandKeyProbe()
    {
        FocusShellRegion(ShellFocusRegion.Worksheet);
        _launchSmokeLiveCommandKeySnapshot = MacOsLaunchSmokeLiveCommandKeySnapshot.Ready(
            _session.IsSelectedRangeStartBold,
            _session.IsSelectedRangeStartItalic,
            _session.IsSelectedRangeStartUnderline);
        return _launchSmokeLiveCommandKeySnapshot;
    }

    internal MacOsLaunchSmokeLiveCommandKeySnapshot CreateLaunchSmokeLiveCommandKeySnapshot() =>
        _launchSmokeLiveCommandKeySnapshot;

    private void RecordLaunchSmokeLiveCommandKey(Key key, bool before, bool after)
    {
        if (!_launchSmokeLiveCommandKeySnapshot.IsReady)
            return;

        var changed = before != after;
        _launchSmokeLiveCommandKeySnapshot = key switch
        {
            Key.B => _launchSmokeLiveCommandKeySnapshot with
            {
                HasBoldCommandKey = true,
                HasBoldStateChange = _launchSmokeLiveCommandKeySnapshot.HasBoldStateChange || changed,
                CurrentBoldState = after
            },
            Key.I => _launchSmokeLiveCommandKeySnapshot with
            {
                HasItalicCommandKey = true,
                HasItalicStateChange = _launchSmokeLiveCommandKeySnapshot.HasItalicStateChange || changed,
                CurrentItalicState = after
            },
            Key.U => _launchSmokeLiveCommandKeySnapshot with
            {
                HasUnderlineCommandKey = true,
                HasUnderlineStateChange = _launchSmokeLiveCommandKeySnapshot.HasUnderlineStateChange || changed,
                CurrentUnderlineState = after
            },
            _ => _launchSmokeLiveCommandKeySnapshot
        };
    }

    private void RecordLaunchSmokeLiveSelectAllCommandKey(GridRange before, GridRange after)
    {
        if (!_launchSmokeLiveCommandKeySnapshot.IsReady)
            return;

        _launchSmokeLiveCommandKeySnapshot = _launchSmokeLiveCommandKeySnapshot with
        {
            HasSelectAllCommandKey = true,
            HasSelectAllStateChange = _launchSmokeLiveCommandKeySnapshot.HasSelectAllStateChange || before != after
        };
    }

    internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()
    {
        var hasNativeFileMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "File", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var hasNativeEditMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "Edit", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var hasNativeDataMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "Data", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var hasNativeReviewMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "Review", StringComparison.Ordinal) &&
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
        var hasNativeWindowMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "Window", StringComparison.Ordinal) &&
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
            HasFormulaBoxAutomationName: string.Equals(AutomationProperties.GetName(_formulaBox), "Formula bar", StringComparison.Ordinal),
            HasFormulaBoxAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_formulaBox), "Edit the active cell value or formula.", StringComparison.Ordinal),
            HasFormulaBoxAutomationId: string.Equals(AutomationProperties.GetAutomationId(_formulaBox), "FormulaBox", StringComparison.Ordinal),
            HasStatusTextAutomationName: string.Equals(AutomationProperties.GetName(_statusText), "Status", StringComparison.Ordinal),
            HasStatusTextAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_statusText), "Shows the current workbook status.", StringComparison.Ordinal),
            HasStatusTextAutomationId: string.Equals(AutomationProperties.GetAutomationId(_statusText), "StatusText", StringComparison.Ordinal),
            HasStatusTextValue: !string.IsNullOrWhiteSpace(_statusText.Text),
            HasCellAddressAutomationName: string.Equals(AutomationProperties.GetName(_cellAddressText), "Cell address", StringComparison.Ordinal),
            HasCellAddressAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_cellAddressText), "Shows the active cell address.", StringComparison.Ordinal),
            HasCellAddressAutomationId: string.Equals(AutomationProperties.GetAutomationId(_cellAddressText), "CellAddressText", StringComparison.Ordinal),
            HasSelectionStatsAutomationName: string.Equals(AutomationProperties.GetName(_selectionStatsText), "Selection statistics", StringComparison.Ordinal),
            HasSelectionStatsAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_selectionStatsText), "Shows statistics for the current selection.", StringComparison.Ordinal),
            HasSelectionStatsAutomationId: string.Equals(AutomationProperties.GetAutomationId(_selectionStatsText), "SelectionStatsText", StringComparison.Ordinal),
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
            HasNativeDataMenu: hasNativeDataMenu,
            HasNativeReviewMenu: hasNativeReviewMenu,
            HasNativeFormatMenu: hasNativeFormatMenu,
            HasNativeViewMenu: hasNativeViewMenu,
            HasNativeSheetMenu: hasNativeSheetMenu,
            HasNativeWindowMenu: hasNativeWindowMenu,
            HasNativeHelpMenu: hasNativeHelpMenu,
            HasNativeNewWorkbookMenuItem: HasNativeMenuItem(_newWorkbookMenuItem, "New Workbook"),
            HasNativeOpenMenuItem: HasNativeMenuItem(_openMenuItem, "Open..."),
            HasNativeOpenRecentMenuItem: HasNativeMenuItem(_openRecentMenuItem, "Open Recent", requireGesture: false),
            NativeOpenRecentItemCount: nativeOpenRecentItemCount,
            HasNativeSaveMenuItem: HasNativeMenuItem(_saveMenuItem, "Save"),
            HasNativeSaveAsMenuItem: HasNativeMenuItem(_saveAsMenuItem, "Save As..."),
            HasNativeExportPdfMenuItem: HasNativeMenuItem(_exportPdfMenuItem, "Export to PDF...", requireGesture: false),
            HasNativeShareWorkbookMenuItem: HasEnabledNativeMenuItem(_shareWorkbookMenuItem, "Share Workbook...", requireGesture: false),
            HasNativeWorkbookStatisticsMenuItem: HasNativeMenuItem(_workbookStatisticsMenuItem, "Workbook Statistics..."),
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
            HasNativeSortAscendingMenuItem: HasNativeMenuItem(_sortAscendingMenuItem, "Sort A to Z", requireGesture: false),
            HasNativeSortDescendingMenuItem: HasNativeMenuItem(_sortDescendingMenuItem, "Sort Z to A", requireGesture: false),
            HasNativeFlashFillMenuItem: HasNativeMenuItem(_flashFillMenuItem, "Flash Fill"),
            HasNativeAdvancedFilterMenuItem: HasNativeMenuItem(_advancedFilterMenuItem, "Advanced Filter...", requireGesture: false),
            HasNativeRemoveDuplicatesMenuItem: HasNativeMenuItem(_removeDuplicatesMenuItem, "Remove Duplicates...", requireGesture: false),
            HasNativeSubtotalMenuItem: HasNativeMenuItem(_subtotalMenuItem, "Subtotal...", requireGesture: false),
            HasNativeDataValidationPreviewMenuItem: HasNativeMenuItem(_dataValidationPreviewMenuItem, "Data Validation Preview...", requireGesture: false),
            HasNativeDataValidationMenuItem: HasNativeMenuItem(_dataValidationMenuItem, "Data Validation...", requireGesture: false),
            HasNativeWhatIfAnalysisMenuItem: HasNativeMenuItem(_whatIfAnalysisMenuItem, "What-If Analysis", requireGesture: false),
            HasNativeGoalSeekMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, "Goal Seek..."),
            HasNativeDataTableMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, "Data Table..."),
            HasNativeScenarioManagerMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, "Scenario Manager..."),
            HasNativeForecastSheetMenuItem: HasNativeMenuItem(_forecastSheetMenuItem, "Forecast Sheet...", requireGesture: false),
            HasNativeReviewSummaryMenuItem: HasNativeMenuItem(_reviewSummaryMenuItem, "Review Summary...", requireGesture: false),
            HasNativeCheckAccessibilityMenuItem: HasNativeMenuItem(_checkAccessibilityMenuItem, "Check Accessibility...", requireGesture: false),
            HasNativeNextNoteMenuItem: HasNativeMenuItem(_nextNoteMenuItem, "Next Note", requireGesture: false),
            HasNativePreviousNoteMenuItem: HasNativeMenuItem(_previousNoteMenuItem, "Previous Note", requireGesture: false),
            HasNativeNextCommentMenuItem: HasNativeMenuItem(_nextCommentMenuItem, "Next Comment", requireGesture: false),
            HasNativePreviousCommentMenuItem: HasNativeMenuItem(_previousCommentMenuItem, "Previous Comment", requireGesture: false),
            HasNativeFormatCellsMenuItem: HasNativeMenuItem(_formatCellsMenuItem, "Format Cells..."),
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
            HasNativeMinimizeWindowMenuItem: HasNativeMenuItem(_minimizeWindowMenuItem, "Minimize"),
            HasNativeZoomWindowMenuItem: HasNativeMenuItem(_zoomWindowMenuItem, "Zoom", requireGesture: false),
            HasNativeBringAllToFrontMenuItem: HasNativeMenuItem(_bringAllToFrontMenuItem, "Bring All to Front", requireGesture: false),
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

    private static bool HasEnabledNativeMenuItem(NativeMenuItem item, string expectedHeader, bool requireGesture = true) =>
        item.IsEnabled &&
        HasNativeMenuItem(item, expectedHeader, requireGesture);

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
        var plan = OpenRecentWorkbookMenuPlanner.Create(
            _recentFiles.Entries,
            File.Exists,
            path => _session.TryResolveOpenTarget(path, out var target, out _) ? target!.Path : null);
        if (plan.ItemCount == 0)
        {
            menu.Items.Add(new NativeMenuItem
            {
                Header = "(No Recent Workbooks)",
                IsEnabled = false,
            });
            return menu;
        }

        foreach (var entry in plan.Items)
        {
            var path = entry.Path;
            var fileAccessIdentity = entry.FileAccessIdentity;
            var item = new NativeMenuItem
            {
                Header = entry.Header,
                IsEnabled = isIdle,
            };
            item.Click += async (_, _) => await OpenRecentWorkbookAsync(path, fileAccessIdentity);
            menu.Items.Add(item);
        }

        return menu;
    }

    private async Task OpenRecentWorkbookAsync(
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity = null)
    {
        if (!_session.TryResolveOpenTarget(path, fileAccessIdentity, out var target, out _) ||
            target is null)
        {
            _recentFiles.Remove(path);
            RefreshNativeOpenRecentMenu(!_isOpening && !_isSaving);
            ShowOpenIssue($"Recent workbook no longer exists: {path}");
            return;
        }

        using var fileAccess = await _workbookFileAccessService.BeginAccessAsync(
            StorageProvider,
            target.FileAccessIdentity);
        if (!File.Exists(target.Path))
        {
            _recentFiles.Remove(path);
            RefreshNativeOpenRecentMenu(!_isOpening && !_isSaving);
            ShowOpenIssue($"Recent workbook no longer exists: {path}");
            return;
        }

        await OpenWorkbookPathAsync(target.Path, target.FileAccessIdentity);
    }

    private void RecordStartupRecentWorkbook(StartupWorkbookLoadResult source)
    {
        if (!source.IsFallback && !string.IsNullOrWhiteSpace(source.SourcePath))
            RecordRecentWorkbook(source.SourcePath, source.SourceFileAccessIdentity);
    }

    private void RecordRecentWorkbook(string path, WorkbookFileAccessIdentity? fileAccessIdentity = null)
    {
        if (!_session.TryResolveOpenTarget(path, out var target, out _) ||
            target is null ||
            !File.Exists(target.Path))
            return;

        _recentFiles.AddOrUpdate(target.Path, fileAccessIdentity ?? target.FileAccessIdentity);
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

    private static bool IsOpenActiveDropdownShortcut(KeyEventArgs args) =>
        args.Key == Key.Down && args.KeyModifiers == KeyModifiers.Alt;

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsShellFocusCycleKey(e))
        {
            e.Handled = true;
            CycleShellFocus(reverse: e.KeyModifiers == KeyModifiers.Shift);
            return;
        }

        if (IsOpenActiveDropdownShortcut(e))
        {
            if (!_formulaBox.IsFocused)
            {
                e.Handled = OpenActiveDataValidationDropdown();
            }

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

            if (e.Key == Key.F3 && e.KeyModifiers == KeyModifiers.Shift)
            {
                e.Handled = true;
                InsertFunction();
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
            e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A or Key.B or Key.D or Key.E or Key.I or Key.R or Key.U or Key.D4 or Key.NumPad4 or Key.D5 or Key.NumPad5)
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
        else if (e.Key == Key.G && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            e.Handled = true;
            await ShowWorkbookStatisticsDialogAsync();
        }
        else if (e.Key == Key.G && HasOnlyControlModifier(e.KeyModifiers))
        {
            e.Handled = true;
            await ShowGoToDialogAsync();
        }
        else if ((e.Key is Key.D1 or Key.NumPad1) && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            await ShowFormatCellsDialogAsync();
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
            var before = _session.SelectedRange;
            SelectCurrentRegionOrAll();
            RecordLaunchSmokeLiveSelectAllCommandKey(before, _session.SelectedRange);
        }
        else if (e.Key == Key.B && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            ToggleSelectedRangeBold(trackLaunchSmokeLiveCommandKey: true);
        }
        else if (e.Key == Key.I && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            ToggleSelectedRangeItalic(trackLaunchSmokeLiveCommandKey: true);
        }
        else if (e.Key == Key.U && HasOnlyCommandModifier(e.KeyModifiers))
        {
            e.Handled = true;
            ToggleSelectedRangeUnderline(trackLaunchSmokeLiveCommandKey: true);
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
        else if (e.Key == Key.E && HasOnlyControlModifier(e.KeyModifiers))
        {
            e.Handled = true;
            FlashFillSelectedRange();
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

    private bool OpenActiveDataValidationDropdown()
    {
        if (_isOpening || _isSaving)
            return true;

        if (!TryCommitPendingFormulaEdit())
            return true;

        RefreshShell("Ready");
        if (_activeDataValidationDropdown is null)
            return false;

        _activeDataValidationDropdown.Focus();
        _activeDataValidationDropdown.IsDropDownOpen = true;
        return true;
    }

    private void DataValidationDropdown_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: string selected })
            return;

        CommitDataValidationDropdownSelection(selected);
    }

    private void CommitDataValidationDropdownSelection(string selected)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var address = _session.ActiveCell;
        _session.BeginFormulaEdit(address);
        var result = _session.CommitCellText(selected);
        if (!result.Success)
        {
            _session.CancelFormulaEdit();
            _formulaBoxEditOriginalText = null;
            ShowEditIssue(result.ErrorMessage ?? "Data validation dropdown failed.");
            return;
        }

        RefreshShell($"Picked {selected} for {FormatCellReference(address)}");
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

        IStorageFile? storageFile = null;
        foreach (var file in storageFiles)
        {
            storageFile = file;
            break;
        }

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

            var fileAccessIdentity = await _workbookFileAccessService.CreateIdentityAsync(path, storageFile);
            await OpenWorkbookPathAsync(path, fileAccessIdentity);
        }
    }

    private async Task OpenWorkbookPathAsync(
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity = null)
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

        if (!_session.TryResolveOpenTarget(path, fileAccessIdentity, out var target, out var message))
        {
            ShowOpenIssue(message);
            return;
        }

        await OpenWorkbookFromTargetAsync(target!);
    }

    private bool TrySelectDroppedWorkbookPath(DragEventArgs e, out string? path, out string message) =>
        TrySelectDroppedWorkbookPath(e, out path, out _, out message);

    private bool TrySelectDroppedWorkbookPath(
        DragEventArgs e,
        out string? path,
        out IStorageItem? storageItem,
        out string message)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            path = null;
            storageItem = null;
            message = "Drop a supported local workbook file.";
            return false;
        }

        return TrySelectOpenableLocalWorkbookPath(files, out path, out storageItem, out message);
    }

    private bool TrySelectOpenableLocalWorkbookPath(IEnumerable<IStorageItem> files, out string? path, out string message) =>
        TrySelectOpenableLocalWorkbookPath(files, out path, out _, out message);

    private bool TrySelectOpenableLocalWorkbookPath(
        IEnumerable<IStorageItem> files,
        out string? path,
        out IStorageItem? storageItem,
        out string message)
    {
        path = null;
        storageItem = null;
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
            if (!LocalFilePath.TryNormalize(candidate, out var normalizedCandidate))
                continue;

            sawLocalPath = true;
            if (Directory.Exists(normalizedCandidate))
                continue;
            if (!File.Exists(normalizedCandidate))
                continue;

            sawFileCandidate = true;
            if (_session.TryResolveOpenTarget(normalizedCandidate, out var target, out unsupportedMessage))
            {
                path = target!.Path;
                storageItem = file;
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
        _isOpening = true;
        UpdateSaveButton();
        try
        {
            _statusText.Text = "Opening...";
            _statusText.Foreground = Brush(67, 113, 83);
            var progress = new Progress<WorkbookOpenProgressUpdate>(
                update =>
                {
                    _statusText.Text = FormatOpenStatus(update);
                    _statusText.Foreground = Brush(67, 113, 83);
                });

            using var fileAccess = await _workbookFileAccessService.BeginAccessAsync(
                StorageProvider,
                target.FileAccessIdentity);
            var result = await _openService.LoadAsync(
                target.Path,
                target.Adapter,
                target.Extension,
                target.Format,
                progress);
            var (viewportHeight, viewportWidth) = GetCurrentSheetViewportSize();
            _session = _sessionFactory.CreateOpened(target, result, viewportHeight, viewportWidth, includeObjects: true);
            RefreshViewportSizeForZoom();
            RecordRecentWorkbook(target.Path, target.FileAccessIdentity);
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

    private async Task ShareWorkbookAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        await ExecuteWorkbookShareActionPlanAsync(CreateWorkbookShareActionPlan());
    }

    private WorkbookShareActionPlan CreateWorkbookShareActionPlan() =>
        WorkbookShareActionPlanner.CreatePlan(
            _session.CurrentFilePath,
            CreateWorkbookShareActionSurface(),
            File.Exists);

    private WorkbookShareActionSurface CreateWorkbookShareActionSurface()
    {
        var capability = _workbookShareSheetService.Capability;
        return new(
            capability.ShareSheetLabel,
            CanShowShareSheet: capability.CanShowShareSheet,
            CanOpenContainingFolder: TopLevel.GetTopLevel(this)?.Launcher is not null,
            OpenContainingFolderLabel: GetWorkbookShareOpenContainingFolderLabel());
    }

    private static string GetWorkbookShareOpenContainingFolderLabel() =>
        OperatingSystem.IsMacOS()
            ? "Reveal in Finder"
            : "Open Containing Folder";

    private async Task ExecuteWorkbookShareActionPlanAsync(WorkbookShareActionPlan plan)
    {
        switch (plan.Kind)
        {
            case WorkbookShareActionPlanKind.SaveAsBeforeShare:
                ShowShareStatus(WorkbookShareActionPlanner.FormatStatus(plan), isWarning: true);
                await SaveWorkbookAsAsync();
                var nextPlan = CreateWorkbookShareActionPlan();
                if (nextPlan.Kind == WorkbookShareActionPlanKind.SaveAsBeforeShare)
                {
                    ShowShareStatus(WorkbookShareActionPlanner.FormatStatus(nextPlan), isWarning: true);
                    return;
                }

                await ExecuteWorkbookShareActionPlanAsync(nextPlan);
                break;

            case WorkbookShareActionPlanKind.OpenContainingFolder:
                if (!await TrySaveDirtyWorkbookForShareAsync())
                    return;

                var refreshedPlan = CreateWorkbookShareActionPlan();
                if (refreshedPlan.Kind != WorkbookShareActionPlanKind.OpenContainingFolder)
                {
                    await ExecuteWorkbookShareActionPlanAsync(refreshedPlan);
                    return;
                }

                await OpenWorkbookContainingFolderAsync(refreshedPlan);
                break;

            case WorkbookShareActionPlanKind.ShareSheet:
                await ShowWorkbookShareSheetAsync(plan);
                break;

            case WorkbookShareActionPlanKind.Deferred:
            default:
                ShowShareStatus(WorkbookShareActionPlanner.FormatStatus(plan), isWarning: true);
                break;
        }
    }

    private async Task<bool> TrySaveDirtyWorkbookForShareAsync()
    {
        if (!_session.IsDirty)
            return true;

        await SaveCurrentWorkbookAsync();
        return !_session.IsDirty;
    }

    private async Task ShowWorkbookShareSheetAsync(WorkbookShareActionPlan plan)
    {
        if (!await TrySaveDirtyWorkbookForShareAsync())
            return;

        var refreshedPlan = CreateWorkbookShareActionPlan();
        if (refreshedPlan.Kind != WorkbookShareActionPlanKind.ShareSheet)
        {
            await ExecuteWorkbookShareActionPlanAsync(refreshedPlan);
            return;
        }

        var filePath = refreshedPlan.Path;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            await ExecuteWorkbookShareActionPlanAsync(CreateWorkbookShareActionPlan());
            return;
        }

        ShowShareStatus(WorkbookShareActionPlanner.FormatStatus(refreshedPlan), isWarning: false);
        try
        {
            var result = await _workbookShareSheetService.ShowShareSheetAsync(this, filePath);
            if (result.WasShown)
            {
                ShowShareStatus(
                    $"{refreshedPlan.EffectiveSurface.ShareSheetLabel} opened for {Path.GetFileName(filePath)}.",
                    isWarning: false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
                ShowShareStatus(result.Message, isWarning: true);

            await FallbackToOpenContainingFolderAfterShareSheetFailureAsync(refreshedPlan);
        }
        catch (Exception ex)
        {
            ShowShareStatus($"{refreshedPlan.EffectiveSurface.ShareSheetLabel} could not open: {ex.Message}", isWarning: true);
            await FallbackToOpenContainingFolderAfterShareSheetFailureAsync(refreshedPlan);
        }
    }

    private async Task FallbackToOpenContainingFolderAfterShareSheetFailureAsync(WorkbookShareActionPlan shareSheetPlan)
    {
        var fallbackSurface = CreateWorkbookShareActionSurface() with { CanShowShareSheet = false };
        var fallbackPlan = WorkbookShareActionPlanner.CreatePlan(shareSheetPlan.Path, fallbackSurface, File.Exists);
        if (fallbackPlan.Kind == WorkbookShareActionPlanKind.OpenContainingFolder)
        {
            await OpenWorkbookContainingFolderAsync(fallbackPlan);
            return;
        }

        ShowShareStatus(WorkbookShareActionPlanner.FormatStatus(fallbackPlan), isWarning: true);
    }

    private async Task OpenWorkbookContainingFolderAsync(WorkbookShareActionPlan plan)
    {
        var folderPath = plan.ContainingFolderPath;
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            var unavailablePlan = new WorkbookShareActionPlan(
                WorkbookShareActionPlanKind.Deferred,
                plan.Path,
                UnavailableReason: WorkbookShareActionUnavailableReason.ContainingFolderUnavailable,
                Surface: plan.EffectiveSurface);
            ShowShareStatus(WorkbookShareActionPlanner.FormatStatus(unavailablePlan), isWarning: true);
            return;
        }

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            ShowShareStatus(WorkbookShareActionPlanner.FormatStatus(CreateWorkbookShareActionPlan()), isWarning: true);
            return;
        }

        ShowShareStatus(WorkbookShareActionPlanner.FormatStatus(plan), isWarning: true);
        try
        {
            if (!await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folderPath)))
            {
                ShowShareStatus($"{plan.EffectiveSurface.OpenContainingFolderLabel} could not open for {plan.Path}.", isWarning: true);
                return;
            }

            var workbookName = string.IsNullOrWhiteSpace(plan.Path)
                ? "the saved workbook"
                : Path.GetFileName(plan.Path);
            ShowShareStatus($"{plan.EffectiveSurface.OpenContainingFolderLabel} opened for {workbookName}.", isWarning: false);
        }
        catch (Exception ex)
        {
            ShowShareStatus($"{plan.EffectiveSurface.OpenContainingFolderLabel} could not open: {ex.Message}", isWarning: true);
        }
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

            var fileAccessIdentity = await _workbookFileAccessService.CreateIdentityAsync(path, storageFile);
            await SaveWorkbookToTargetAsync(target!, fileAccessIdentity);
        }
    }

    private async Task ExportActiveSheetPdfAsync()
    {
        if (_isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (!StorageProvider.CanSave)
        {
            ShowExportIssue("PDF export unavailable on this platform.");
            return;
        }

        var pdfFileType = new FilePickerFileType("PDF Document")
        {
            Patterns = ["*.pdf"],
        };
        var storageFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to PDF",
            SuggestedFileName = BuildSuggestedPdfExportFileName(),
            DefaultExtension = "pdf",
            FileTypeChoices = [pdfFileType],
            SuggestedFileType = pdfFileType,
            ShowOverwritePrompt = true,
        });

        if (storageFile is null)
            return;

        using (storageFile)
        {
            var path = storageFile.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowExportIssue("PDF export requires a local file path.");
                return;
            }

            var requestedPath = path;
            var exportPathPlan = ExportPathPlanner.Plan(requestedPath, ExportFileFormat.Pdf);
            if (ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, exportPathPlan, File.Exists) &&
                !await ConfirmNormalizedPdfOverwriteAsync(exportPathPlan.Path))
            {
                ShowExportIssue("PDF export canceled.");
                return;
            }

            path = exportPathPlan.Path;
            _isSaving = true;
            UpdateSaveButton();
            try
            {
                _statusText.Text = "Exporting PDF...";
                _statusText.Foreground = Brush(67, 113, 83);

                var exportPrintPlan = CreateActiveSheetPortablePdfPrintPlan();
                var exportPlan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);
                if (!exportPlan.IsReady)
                {
                    ShowExportIssue(exportPlan.StatusText);
                    return;
                }

                PortablePdfDocumentExportResult result;
                try
                {
                    // Skia shapes and automatically embeds/subsets fonts, so Unicode (non-WinAnsi)
                    // text exports correctly. Fall back to the dependency-free WinAnsi writer if Skia
                    // is unavailable so export still works for ASCII/WinAnsi content.
                    using var pdfBuffer = new MemoryStream();
                    result = Pdf.SkiaPdfDocumentExporter.Save(_session.Workbook, exportPlan, pdfBuffer);
                    await File.WriteAllBytesAsync(path, pdfBuffer.ToArray());
                }
                catch (Exception)
                {
                    result = PortablePdfDocumentExporter.Save(_session.Workbook, exportPlan, path);
                }

                RefreshShell($"{result.StatusText} {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                ShowExportIssue($"PDF export failed: {ex.Message}");
            }
            finally
            {
                _isSaving = false;
                UpdateSaveButton();
            }
        }
    }

    private async Task<bool> ConfirmNormalizedPdfOverwriteAsync(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = normalizedPath;

        var dialog = new Window
        {
            Title = "Replace PDF?",
            Width = 460,
            Height = 210,
            MinWidth = 420,
            MinHeight = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var titleText = new TextBlock
        {
            Text = $"{fileName} already exists.",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var detailText = new TextBlock
        {
            Text = "FreeX changed the selected file name to use the .pdf extension. Replace the existing PDF file?",
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        };

        var replaceButton = new Button
        {
            Content = "Replace",
            MinWidth = 92,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(replaceButton, "PdfExportOverwriteReplaceButton");
        AutomationProperties.SetName(replaceButton, "Replace");
        AutomationProperties.SetHelpText(replaceButton, "Replace the existing normalized PDF file.");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 92,
            Padding = new Thickness(10, 4),
            IsCancel = true,
        };
        AutomationProperties.SetAutomationId(cancelButton, "PdfExportOverwriteCancelButton");
        AutomationProperties.SetName(cancelButton, "Cancel");
        AutomationProperties.SetHelpText(cancelButton, "Return without exporting the PDF.");

        var shouldReplace = false;
        void Finish(bool value)
        {
            shouldReplace = value;
            dialog.Close();
        }

        replaceButton.Click += (_, _) => Finish(true);
        cancelButton.Click += (_, _) => Finish(false);
        dialog.Opened += (_, _) => cancelButton.Focus();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(false);
                e.Handled = true;
            }
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
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    Children =
                    {
                        cancelButton,
                        replaceButton,
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
        return shouldReplace;
    }

    private WorkbookExportPrintPlan CreateActiveSheetPortablePdfPrintPlan() =>
        WorkbookExportPrintPlanner.CreatePlan(
            _session.Workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: ResolveActiveSheetIndex()),
            new WorkbookExportPrintPageCapacity(PortablePdfRowsPerPage, PortablePdfColumnsPerPage),
            WorkbookExportPrintSurface.MacOs);

    private int ResolveActiveSheetIndex()
    {
        for (var index = 0; index < _session.Workbook.Sheets.Count; index++)
        {
            if (_session.Workbook.Sheets[index].Id == _session.ActiveSheet.Id)
                return index;
        }

        return _session.Workbook.ActiveSheetIndex ?? 0;
    }

    private string BuildSuggestedPdfExportFileName()
    {
        var workbookName = Path.GetFileNameWithoutExtension(_session.DisplayName);
        if (string.IsNullOrWhiteSpace(workbookName))
            workbookName = "FreeX";

        return workbookName + ".pdf";
    }

    private async Task SaveWorkbookToTargetAsync(
        FileSaveTarget target,
        WorkbookFileAccessIdentity? fileAccessIdentity = null)
    {
        // Capture the dirty generation before the first await so mid-save edits are detectable.
        var generationAtSaveStart = _session.DirtyGeneration;
        _isSaving = true;
        UpdateSaveButton();
        try
        {
            _statusText.Text = "Saving...";
            _statusText.Foreground = Brush(67, 113, 83);
            var progress = new Progress<WorkbookSaveProgressUpdate>(
                update =>
                {
                    _statusText.Text = FormatSaveStatus(update);
                    _statusText.Foreground = Brush(67, 113, 83);
                });

            fileAccessIdentity ??= await _workbookFileAccessService.CreateIdentityAsync(
                target.Path,
                existingIdentity: _session.CurrentFileAccessIdentity);
            using var fileAccess = await _workbookFileAccessService.BeginAccessAsync(StorageProvider, fileAccessIdentity);
            await _saveService.SaveAsync(target.Path, target.Adapter, _session.Workbook, progress);
            _session.TryMarkSavedIfNoEditsArrived(generationAtSaveStart, target.Path, fileAccessIdentity);
            RecordRecentWorkbook(target.Path, fileAccessIdentity);
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
        return FileDialogFilterBuilder
            .BuildSavePickerTypes(_session.SaveFormats, preferredFirstExtension: NativeWorkbookExtension)
            .Select(CreateFilePickerFileType)
            .ToList();
    }

    private IReadOnlyList<FilePickerFileType> BuildOpenFileTypes()
    {
        return FileDialogFilterBuilder
            .BuildOpenPickerTypes(_session.OpenFormats, allSupportedName: "All supported workbooks")
            .Select(CreateFilePickerFileType)
            .ToList();
    }

    private static FilePickerFileType CreateFilePickerFileType(FilePickerTypeDescriptor descriptor) =>
        new(descriptor.DisplayName)
        {
            Patterns = descriptor.Patterns.ToList(),
        };

    private void ShowSaveIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
    }

    private void ShowExportIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
        UpdateSaveButton();
    }

    private void ShowShareStatus(string message, bool isWarning)
    {
        _statusText.Text = message;
        _statusText.Foreground = isWarning
            ? Brush(143, 74, 18)
            : Brush(67, 113, 83);
        UpdateSaveButton();
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
        if (!IsHttpOrHttpsHelpUrl(url))
        {
            ShowHelpIssue($"{title} link is blocked.");
            return;
        }

        var result = await OpenExternalUriAsync(url);
        switch (result)
        {
            case ExternalUriLaunchResult.Launched:
                return;
            case ExternalUriLaunchResult.BlockedScheme:
                ShowHelpIssue($"{title} link is blocked.");
                return;
            case ExternalUriLaunchResult.LauncherUnavailable:
                ShowHelpIssue($"{title} link cannot be opened on this platform.");
                return;
            case ExternalUriLaunchResult.LaunchFailed:
            default:
                ShowHelpIssue($"{title} link could not be opened.");
                return;
        }
    }

    private static bool IsHttpOrHttpsHelpUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ExternalUriLaunchResult> OpenExternalUriAsync(string target)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        Func<Uri, Task<bool>>? launchAsync = launcher is null
            ? null
            : async uri => await launcher.LaunchUriAsync(uri);
        return await ExternalUriLauncher.OpenAsync(target, launchAsync);
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
        AutomationProperties.SetName(closeButton, "Close");
        AutomationProperties.SetHelpText(closeButton, $"Close {title}.");
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
        AutomationProperties.SetName(textBox, title);
        AutomationProperties.SetHelpText(textBox, $"Read-only {title} text.");

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

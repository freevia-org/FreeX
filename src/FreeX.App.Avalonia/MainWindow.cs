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
using FreeX.App.Services;
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
    private const string NativeWorkbookExtension = ".fxl";
    private const string PlatformAboutSummary = "Built with .NET 10, Avalonia, ClosedXML.";
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
    private readonly ContentControl _sheetGridHost = new();
    private readonly ContentControl _sheetTabsHost = new();
    private readonly ScrollViewer _sheetScrollViewer = new();
    private readonly ScrollBar _verticalWorksheetScrollBar = new();
    private readonly ScrollBar _horizontalWorksheetScrollBar = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _detailText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _selectionStatsText = new();
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
    private readonly Button _clearContentsButton = new();
    private readonly ToggleButton _boldButton = new();
    private readonly ToggleButton _italicButton = new();
    private readonly ToggleButton _underlineButton = new();
    private readonly ToggleButton _doubleUnderlineButton = new();
    private readonly ToggleButton _strikethroughButton = new();
    private readonly Button _increaseFontSizeButton = new();
    private readonly Button _decreaseFontSizeButton = new();
    private readonly DropDownButton _fillColorButton = new();
    private readonly DropDownButton _fontColorButton = new();
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
    private readonly Button _decreaseIndentButton = new();
    private readonly Button _increaseIndentButton = new();
    private readonly NativeMenuItem _openMenuItem = new();
    private readonly NativeMenuItem _saveMenuItem = new();
    private readonly NativeMenuItem _saveAsMenuItem = new();
    private readonly NativeMenuItem _newSheetMenuItem = new();
    private readonly NativeMenuItem _duplicateSheetMenuItem = new();
    private readonly NativeMenuItem _deleteSheetMenuItem = new();
    private readonly NativeMenuItem _undoMenuItem = new();
    private readonly NativeMenuItem _redoMenuItem = new();
    private readonly NativeMenuItem _cutMenuItem = new();
    private readonly NativeMenuItem _copyMenuItem = new();
    private readonly NativeMenuItem _pasteMenuItem = new();
    private readonly NativeMenuItem _pasteSpecialMenuItem = new();
    private readonly NativeMenuItem _clearContentsMenuItem = new();
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
    private readonly NativeMenuItem _decreaseIndentMenuItem = new();
    private readonly NativeMenuItem _increaseIndentMenuItem = new();
    private readonly NativeMenuItem _helpOnlineMenuItem = new();
    private readonly NativeMenuItem _sendFeedbackMenuItem = new();
    private readonly NativeMenuItem _checkForUpdatesMenuItem = new();
    private readonly NativeMenuItem _aboutMenuItem = new();
    private readonly NativeMenuItem _legalNoticesMenuItem = new();
    private readonly NativeMenuItem _quitMenuItem = new();
    private NativeMenu? _nativeMenu;
    private WorkbookSession _session;
    private string? _formulaBoxEditOriginalText;
    private bool _isOpening;
    private bool _isSaving;
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
        ConfigureWorkbookDropTarget();
        KeyDown += MainWindow_KeyDown;
        TextInput += MainWindow_TextInput;
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
        _openMenuItem.Header = "Open...";
        _openMenuItem.Gesture = new KeyGesture(Key.O, KeyModifiers.Meta);
        _openMenuItem.Click += async (_, _) => await OpenWorkbookAsync();

        _saveMenuItem.Header = "Save";
        _saveMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta);
        _saveMenuItem.Click += async (_, _) => await SaveCurrentWorkbookAsync();

        _saveAsMenuItem.Header = "Save As...";
        _saveAsMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift);
        _saveAsMenuItem.Click += async (_, _) => await SaveWorkbookAsAsync();

        _newSheetMenuItem.Header = "New Sheet";
        _newSheetMenuItem.Gesture = new KeyGesture(Key.F11, KeyModifiers.Shift);
        _newSheetMenuItem.Click += (_, _) => AddNewSheet();

        _duplicateSheetMenuItem.Header = "Duplicate Sheet";
        _duplicateSheetMenuItem.Click += (_, _) => DuplicateActiveSheet();

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

        _clearContentsMenuItem.Header = "Clear Contents";
        _clearContentsMenuItem.Gesture = new KeyGesture(Key.Delete);
        _clearContentsMenuItem.Click += (_, _) => ClearSelectedRangeContents();

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
        _quitMenuItem.Click += (_, _) => TryQuitApplication();

        var fileMenu = new NativeMenu();
        fileMenu.Items.Add(_openMenuItem);
        fileMenu.Items.Add(_saveMenuItem);
        fileMenu.Items.Add(_saveAsMenuItem);
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
        editMenu.Items.Add(new NativeMenuItemSeparator());
        editMenu.Items.Add(_clearContentsMenuItem);

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
        formatMenu.Items.Add(_decreaseIndentMenuItem);
        formatMenu.Items.Add(_increaseIndentMenuItem);
        formatMenu.Items.Add(_alignLeftMenuItem);
        formatMenu.Items.Add(_alignCenterMenuItem);
        formatMenu.Items.Add(_alignRightMenuItem);

        var sheetMenu = new NativeMenu();
        sheetMenu.Items.Add(_newSheetMenuItem);
        sheetMenu.Items.Add(_duplicateSheetMenuItem);
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

        _clearContentsButton.Content = "Clear";
        _clearContentsButton.Padding = new Thickness(10, 4);
        _clearContentsButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _clearContentsButton.Click += ClearContentsButton_Click;

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
                    _clearContentsButton,
                    _boldButton,
                    _italicButton,
                    _underlineButton,
                    _doubleUnderlineButton,
                    _strikethroughButton,
                    _increaseFontSizeButton,
                    _decreaseFontSizeButton,
                    _fillColorButton,
                    _fontColorButton,
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
                    _decreaseIndentButton,
                    _increaseIndentButton,
                    _alignLeftButton,
                    _alignCenterButton,
                    _alignRightButton,
                    _cellAddressText,
                    _formulaBox,
                    _statusText,
                    _selectionStatsText,
                },
            },
        };
    }

    private void RefreshShell(string status)
    {
        var preserveFormulaEdit = _formulaBox.IsFocused && _session.FormulaEditAddress is not null;
        var formulaText = _formulaBox.Text;
        var formulaCaretIndex = _formulaBox.CaretIndex;
        var formulaSelectionStart = _formulaBox.SelectionStart;
        var formulaSelectionEnd = _formulaBox.SelectionEnd;

        _sheetGridHost.Content = BuildSheetGrid();
        _sheetTabsHost.Content = BuildSheetTabs();
        _titleText.Text = _session.DisplayName;
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
        _statusText.Foreground = ShouldUseWarningStatusColor(status)
            ? Brush(143, 74, 18)
            : Brush(67, 113, 83);
        Title = $"FreeX - {_session.DisplayName}{(_session.IsDirty ? " *" : "")}";
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
        _clearContentsButton.IsEnabled = isIdle;
        _boldButton.IsEnabled = isIdle;
        _italicButton.IsEnabled = isIdle;
        _underlineButton.IsEnabled = isIdle;
        _doubleUnderlineButton.IsEnabled = isIdle;
        _strikethroughButton.IsEnabled = isIdle;
        _increaseFontSizeButton.IsEnabled = isIdle;
        _decreaseFontSizeButton.IsEnabled = isIdle;
        _fillColorButton.IsEnabled = isIdle;
        _fontColorButton.IsEnabled = isIdle;
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
        _decreaseIndentButton.IsEnabled = isIdle;
        _increaseIndentButton.IsEnabled = isIdle;

        _openMenuItem.IsEnabled = _openButton.IsEnabled;
        _saveMenuItem.IsEnabled = _saveButton.IsEnabled;
        _saveAsMenuItem.IsEnabled = _saveAsButton.IsEnabled;
        _newSheetMenuItem.IsEnabled = _newSheetButton.IsEnabled;
        _duplicateSheetMenuItem.IsEnabled = isIdle;
        _deleteSheetMenuItem.IsEnabled = isIdle;
        _undoMenuItem.IsEnabled = _undoButton.IsEnabled;
        _redoMenuItem.IsEnabled = _redoButton.IsEnabled;
        _cutMenuItem.IsEnabled = _cutButton.IsEnabled;
        _copyMenuItem.IsEnabled = _copyButton.IsEnabled;
        _pasteMenuItem.IsEnabled = _pasteButton.IsEnabled;
        _pasteSpecialMenuItem.IsEnabled = _pasteSpecialButton.IsEnabled;
        _clearContentsMenuItem.IsEnabled = _clearContentsButton.IsEnabled;
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
        _decreaseIndentMenuItem.IsEnabled = _decreaseIndentButton.IsEnabled;
        _increaseIndentMenuItem.IsEnabled = _increaseIndentButton.IsEnabled;
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
            var button = new Button
            {
                MinWidth = 72,
                MaxWidth = 180,
                MinHeight = 28,
                Padding = new Thickness(12, 4),
                Background = tab.IsActive ? SelectionHeaderBackground : Brushes.White,
                BorderBrush = tab.IsActive ? SelectionBorder : ToolbarBorder,
                BorderThickness = new Thickness(1),
                Content = new TextBlock
                {
                    Text = tab.Name,
                    FontSize = 12,
                    FontWeight = tab.IsActive ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = tab.IsActive ? SelectionHeaderForeground : HeaderForeground,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                },
            };
            button.Click += (_, _) => SelectSheet(tab.Id);
            panel.Children.Add(button);
        }

        return panel;
    }

    private Control BuildSheetGrid()
    {
        var viewport = _session.Viewport;
        var cellsByAddress = viewport.Cells.ToDictionary(cell => (cell.Row, cell.Col));
        var grid = new AvaloniaGrid
        {
            Background = Brushes.White,
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeaderColumnWidth) });
        foreach (var metric in viewport.ColMetrics)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GetDisplayedColumnWidth(metric)) });

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderRowHeight) });
        foreach (var metric in viewport.RowMetrics)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GetDisplayedRowHeight(metric)) });

        AddGridChild(grid, CreateHeaderCell(""), 0, 0);
        for (var colIndex = 0; colIndex < viewport.ColMetrics.Count; colIndex++)
        {
            var col = viewport.ColMetrics[colIndex].Col;
            var selected = IsSelectedColumn(col);
            AddGridChild(grid, CreateHeaderCell(CellAddress.NumberToColumnName(col), selected), 0, colIndex + 1);
        }

        for (var rowIndex = 0; rowIndex < viewport.RowMetrics.Count; rowIndex++)
        {
            var row = viewport.RowMetrics[rowIndex].Row;
            var selectedRow = IsSelectedRow(row);
            AddGridChild(grid, CreateHeaderCell(row.ToString(), selectedRow), rowIndex + 1, 0);

            for (var colIndex = 0; colIndex < viewport.ColMetrics.Count; colIndex++)
            {
                var col = viewport.ColMetrics[colIndex].Col;
                cellsByAddress.TryGetValue((row, col), out var cell);
                AddGridChild(grid, CreateCell(cell, row, col), rowIndex + 1, colIndex + 1);
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
        var overlay = new Canvas
        {
            Width = CalculateDisplayedGridWidth(viewport),
            Height = CalculateDisplayedGridHeight(viewport),
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
        out double left,
        out double top,
        out double width,
        out double height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;
        if (!TryGetDisplayedColumnLeft(viewport.ColMetrics, drawingObject.AnchorCol, out var columnLeft) ||
            !TryGetDisplayedRowTop(viewport.RowMetrics, drawingObject.AnchorRow, out var rowTop))
        {
            return false;
        }

        left = HeaderColumnWidth + columnLeft;
        top = HeaderRowHeight + rowTop;
        width = Math.Max(1, drawingObject.Width);
        height = Math.Max(1, drawingObject.Height);
        return true;
    }

    private static bool TryGetDisplayedColumnLeft(
        IReadOnlyList<ColMetric> columns,
        uint column,
        out double left)
    {
        left = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var metric = columns[i];
            if (metric.Col == column)
                return true;
            left += GetDisplayedColumnWidth(metric);
        }

        left = 0;
        return false;
    }

    private static bool TryGetDisplayedRowTop(
        IReadOnlyList<RowMetric> rows,
        uint row,
        out double top)
    {
        top = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var metric = rows[i];
            if (metric.Row == row)
                return true;
            top += GetDisplayedRowHeight(metric);
        }

        top = 0;
        return false;
    }

    private static double CalculateDisplayedGridWidth(ViewportModel viewport)
    {
        var width = HeaderColumnWidth;
        foreach (var metric in viewport.ColMetrics)
            width += GetDisplayedColumnWidth(metric);

        return width;
    }

    private static double CalculateDisplayedGridHeight(ViewportModel viewport)
    {
        var height = HeaderRowHeight;
        foreach (var metric in viewport.RowMetrics)
            height += GetDisplayedRowHeight(metric);

        return height;
    }

    private static double GetDisplayedColumnWidth(ColMetric metric) =>
        Math.Max(MinimumDisplayedColumnWidth, metric.Width);

    private static double GetDisplayedRowHeight(RowMetric metric) =>
        Math.Max(MinimumDisplayedRowHeight, metric.Height);

    private bool IsSelectedColumn(uint col) =>
        _session.SelectedRange.Start.Col <= col && col <= _session.SelectedRange.End.Col;

    private bool IsSelectedRow(uint row) =>
        _session.SelectedRange.Start.Row <= row && row <= _session.SelectedRange.End.Row;

    private Border CreateHeaderCell(string text, bool selected = false) =>
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
            selected: false);

    private Border CreateCell(DisplayCell cell, uint row, uint col)
    {
        var hasCell = cell.Row != 0 && cell.Col != 0;
        var address = new CellAddress(_session.ActiveSheet.Id, row, col);
        var selected = _session.SelectedRange.Contains(address);

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
                address);

        var style = cell.Style;
        var background = style?.ResolveFillColor(_session.Workbook.Theme) is { } fillColor
            ? Brush(fillColor)
            : Brushes.White;
        var foreground = style is null
            ? Brushes.Black
            : Brush(style.ResolveFontColor(_session.Workbook.Theme));
        var alignment = MapCellTextAlignment(
            style?.HorizontalAlignment ?? CellHAlign.General,
            cell.RawValue is NumberValue or DateTimeValue);
        var verticalAlignment = MapCellVerticalAlignment(style?.VerticalAlignment ?? CellVAlign.Bottom);
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
            style);
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
        CellStyle? style = null)
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
            style);
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
        CellStyle? style = null)
    {
        var effectiveText = FormatTextForRotation(text, textRotation);
        var effectiveTextWrapping = textRotation == 255 ? TextWrapping.NoWrap : textWrapping;
        var textBlock = new TextBlock
        {
            Text = effectiveText,
            FontSize = fontSize,
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
            Margin = new Thickness(8 + indentPadding, 0, 8, 0),
        };
        if (CreateTextRotationTransform(textRotation) is { } transform)
        {
            textBlock.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            textBlock.RenderTransform = transform;
        }

        var content = new AvaloniaGrid { ClipToBounds = true };
        content.Children.Add(textBlock);
        AddStyledCellBorderOverlay(content, style);

        return new Border
        {
            Background = background,
            BorderBrush = selected ? SelectionBorder : GridLine,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = content,
        };
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
        textRotation == 255 && text.Length > 1
            ? string.Join(Environment.NewLine, text.ToCharArray())
            : text;

    private static int NormalizeTextRotationForDisplay(int textRotation) =>
        textRotation is >= -90 and <= 90 ? textRotation : 0;

    private static RotateTransform? CreateTextRotationTransform(int textRotation)
    {
        var displayRotation = NormalizeTextRotationForDisplay(textRotation);
        return displayRotation == 0 ? null : new RotateTransform(-displayRotation);
    }

    private MenuFlyout CreatePasteSpecialFlyout() =>
        new()
        {
            ItemsSource = CreatePasteSpecialMenuItems().ToArray(),
        };

    private IEnumerable<MenuItem> CreatePasteSpecialMenuItems()
    {
        yield return CreatePasteSpecialMenuItem("Values", PasteCellsMode.Values, default);
        yield return CreatePasteSpecialMenuItem("Formulas", PasteCellsMode.Formulas, default);
        yield return CreatePasteSpecialMenuItem("Formats", PasteCellsMode.Formats, default);
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
        PasteSpecialOptions options)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteSpecialClipboardTextAsync(mode, options, header);
        return menuItem;
    }

    private NativeMenu CreateNativePasteSpecialMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Values", PasteCellsMode.Values, default));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Formulas", PasteCellsMode.Formulas, default));
        menu.Items.Add(CreateNativePasteSpecialMenuItem("Formats", PasteCellsMode.Formats, default));
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
        PasteSpecialOptions options)
    {
        var menuItem = new NativeMenuItem { Header = header };
        menuItem.Click += async (_, _) => await PasteSpecialClipboardTextAsync(mode, options, header);
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
        RefreshShell("Ready");
    }

    private void SelectRange(CellAddress address)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        _session.SelectRange(new GridRange(_session.ActiveCell, address));
        RefreshShell("Ready");
    }

    private void SelectSheet(SheetId sheetId)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        if (!_session.SelectSheet(sheetId))
            return;

        ClearSelectedDrawingObject();
        RefreshShell($"Selected {_session.ActiveSheet.Name}");
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

    private void ClearContentsButton_Click(object? sender, RoutedEventArgs e)
    {
        ClearSelectedRangeContents();
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
        var result = _session.PasteClipboardTextAtActiveCell(text);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste failed.");
            return;
        }

        RefreshShell($"Pasted at {FormatCellReference(destination)}");
    }

    private async Task PasteSpecialClipboardTextAsync(
        PasteCellsMode mode,
        PasteSpecialOptions options,
        string label)
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
        var result = _session.PasteSpecialClipboardAtActiveCell(text, mode, options);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste Special failed.");
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
        var nativeFillColorSwatchCount = CountNativeColorPaletteSwatches(_fillColorMenuItem.Menu);
        var nativeFontColorSwatchCount = CountNativeColorPaletteSwatches(_fontColorMenuItem.Menu);

        return new MacOsLaunchSmokeSnapshot(
            WindowShown: IsVisible,
            WindowTitle: Title ?? "",
            DisplayName: _session.DisplayName,
            ActiveSheetName: _session.ActiveSheet.Name,
            SheetTabCount: _session.SheetTabs.Count,
            ViewportRowCount: _session.Viewport.RowMetrics.Count,
            ViewportColumnCount: _session.Viewport.ColMetrics.Count,
            OpenedSourcePath: _session.CurrentFilePath,
            IsOpening: _isOpening,
            HasNewSheetButton: _newSheetButton.Content?.ToString() == "+",
            HasNativeFileMenu: hasNativeFileMenu,
            HasNativeEditMenu: hasNativeEditMenu,
            HasNativeFormatMenu: hasNativeFormatMenu,
            HasNativeSheetMenu: hasNativeSheetMenu,
            HasNativeHelpMenu: hasNativeHelpMenu,
            HasNativeOpenMenuItem: HasNativeMenuItem(_openMenuItem, "Open..."),
            HasNativeSaveMenuItem: HasNativeMenuItem(_saveMenuItem, "Save"),
            HasNativeSaveAsMenuItem: HasNativeMenuItem(_saveAsMenuItem, "Save As..."),
            HasNativeNewSheetMenuItem: HasNativeMenuItem(_newSheetMenuItem, "New Sheet"),
            HasNativeDuplicateSheetMenuItem: HasNativeMenuItem(_duplicateSheetMenuItem, "Duplicate Sheet", requireGesture: false),
            HasNativeDeleteSheetMenuItem: HasNativeMenuItem(_deleteSheetMenuItem, "Delete Sheet", requireGesture: false),
            HasNativeUndoMenuItem: HasNativeMenuItem(_undoMenuItem, "Undo"),
            HasNativeRedoMenuItem: HasNativeMenuItem(_redoMenuItem, "Redo"),
            HasNativeCutMenuItem: HasNativeMenuItem(_cutMenuItem, "Cut"),
            HasNativeCopyMenuItem: HasNativeMenuItem(_copyMenuItem, "Copy"),
            HasNativePasteMenuItem: HasNativeMenuItem(_pasteMenuItem, "Paste"),
            HasNativePasteSpecialMenuItem: HasNativeMenuItem(_pasteSpecialMenuItem, "Paste Special"),
            HasNativeClearContentsMenuItem: HasNativeMenuItem(_clearContentsMenuItem, "Clear Contents"),
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
            HasNativeDecreaseIndentMenuItem: HasNativeMenuItem(_decreaseIndentMenuItem, "Decrease Indent", requireGesture: false),
            HasNativeIncreaseIndentMenuItem: HasNativeMenuItem(_increaseIndentMenuItem, "Increase Indent", requireGesture: false),
            HasNativeAlignLeftMenuItem: HasNativeMenuItem(_alignLeftMenuItem, "Align Left", requireGesture: false),
            HasNativeAlignCenterMenuItem: HasNativeMenuItem(_alignCenterMenuItem, "Align Center", requireGesture: false),
            HasNativeAlignRightMenuItem: HasNativeMenuItem(_alignRightMenuItem, "Align Right", requireGesture: false),
            HasNativeHelpOnlineMenuItem: HasNativeMenuItem(_helpOnlineMenuItem, "Help Online"),
            HasNativeSendFeedbackMenuItem: HasNativeMenuItem(_sendFeedbackMenuItem, "Send Feedback", requireGesture: false),
            HasNativeCheckForUpdatesMenuItem: HasNativeMenuItem(_checkForUpdatesMenuItem, "Check for Updates", requireGesture: false),
            HasNativeAboutMenuItem: HasNativeMenuItem(_aboutMenuItem, "About FreeX", requireGesture: false),
            HasNativeLegalNoticesMenuItem: HasNativeMenuItem(_legalNoticesMenuItem, "Legal Notices", requireGesture: false),
            HasNativeQuitMenuItem: HasNativeMenuItem(_quitMenuItem, "Quit FreeX"));
    }

    private static bool HasNativeMenuItem(NativeMenuItem item, string expectedHeader, bool requireGesture = true) =>
        string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal) &&
        (!requireGesture || item.Gesture is not null);

    private static int CountNativeColorPaletteSwatches(NativeMenu? menu) =>
        menu?
            .Items
            .OfType<NativeMenuItem>()
            .Count(item => item.Header?.ToString()?.StartsWith("#", StringComparison.Ordinal) == true) ?? 0;

    private static bool HasOnlyCommandModifier(KeyModifiers modifiers)
    {
        const KeyModifiers commandModifiers = KeyModifiers.Control | KeyModifiers.Meta;
        return (modifiers & commandModifiers) != 0 &&
            (modifiers & ~commandModifiers) == 0;
    }

    private static bool HasOnlyControlModifier(KeyModifiers modifiers) =>
        modifiers == KeyModifiers.Control;

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
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

            NavigateActiveCell(e);
            return;
        }

        if (_formulaBox.IsFocused &&
            e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.B or Key.I or Key.U or Key.D4 or Key.NumPad4 or Key.D5 or Key.NumPad5)
        {
            return;
        }

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
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
        else if (e.Key == Key.O)
        {
            e.Handled = true;
            await OpenWorkbookAsync();
        }
    }

    private void TryQuitApplication()
    {
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
        var bounds = _sheetScrollViewer.Bounds;
        if (bounds.Height <= HeaderRowHeight || bounds.Width <= HeaderColumnWidth)
        {
            viewportHeight = 0;
            viewportWidth = 0;
            return false;
        }

        viewportHeight = bounds.Height - HeaderRowHeight;
        viewportWidth = bounds.Width - HeaderColumnWidth;
        return true;
    }

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

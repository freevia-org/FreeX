using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

internal sealed class ExportOptionsDialog : Window
{
    private readonly RadioButton _activeSheetButton = new() { Content = "Active _sheet(s)", IsChecked = true };
    private readonly RadioButton _selectionButton = new() { Content = "Selected _range" };
    private readonly RadioButton _entireWorkbookButton = new() { Content = "_Workbook" };
    private readonly CheckBox _documentPropertiesBox = new() { Content = "_Include document properties" };
    private readonly CheckBox _openAfterPublishBox = new() { Content = "_Open after publishing" };
    private readonly CheckBox _ignorePrintAreasBox = new() { Content = "_Ignore print areas" };
    private readonly CheckBox _bookmarksBox = new() { Content = "Create _PDF bookmarks using sheet names" };
    private readonly CheckBox _bitmapTextBox = new() { Content = "_Bitmap text when fonts may not be embedded" };
    private readonly ComboBox _bookmarkModeBox = new() { Width = 180, IsEnabled = false };
    private readonly ComboBox _initialViewBox = new() { Width = 180 };
    private readonly ComboBox _openModeBox = new() { Width = 180 };
    private readonly RadioButton _standardQualityButton = new() { Content = "_Standard", IsChecked = true };
    private readonly RadioButton _minimumSizeButton = new() { Content = "_Minimum size" };
    private readonly RadioButton _allPagesButton = new() { Content = "_All", GroupName = "PageRange", IsChecked = true };
    private readonly RadioButton _pagesRangeButton = new() { Content = "_Pages", GroupName = "PageRange" };
    private readonly TextBox _fromPageBox = new() { Width = 56 };
    private readonly TextBox _toPageBox = new() { Width = 56 };

    public ExportOptions Result { get; private set; } = ExportOptions.ExcelLikeDefault;

    public ExportOptionsDialog(bool hasSelection)
    {
        Title = "Export Options";
        Width = 430;
        Height = 376;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _selectionButton.IsEnabled = hasSelection;
        if (!hasSelection)
            _selectionButton.ToolTip = "Select a cell range before exporting the selection.";

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Publish what", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(_activeSheetButton);
        stack.Children.Add(_selectionButton);
        stack.Children.Add(_entireWorkbookButton);

        stack.Children.Add(new TextBlock { Text = "Page range", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        stack.Children.Add(_allPagesButton);
        var pageRangePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        pageRangePanel.Children.Add(_pagesRangeButton);
        pageRangePanel.Children.Add(new Label { Content = "_From", Target = _fromPageBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        pageRangePanel.Children.Add(_fromPageBox);
        pageRangePanel.Children.Add(new Label { Content = "t_o", Target = _toPageBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        pageRangePanel.Children.Add(_toPageBox);
        stack.Children.Add(pageRangePanel);
        _fromPageBox.IsEnabled = false;
        _toPageBox.IsEnabled = false;
        _allPagesButton.Checked += (_, _) => SetPageRangeFieldsEnabled(false);
        _pagesRangeButton.Checked += (_, _) => SetPageRangeFieldsEnabled(true);

        stack.Children.Add(new TextBlock { Text = "PDF/XPS options", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        stack.Children.Add(_documentPropertiesBox);
        stack.Children.Add(_ignorePrintAreasBox);
        stack.Children.Add(_bookmarksBox);
        _bookmarkModeBox.Items.Add("Sheet names");
        _bookmarkModeBox.Items.Add("Print titles");
        _bookmarkModeBox.Items.Add("Page numbers");
        _bookmarkModeBox.SelectedIndex = 0;
        _bookmarksBox.Checked += (_, _) => _bookmarkModeBox.IsEnabled = true;
        _bookmarksBox.Unchecked += (_, _) => _bookmarkModeBox.IsEnabled = false;
        var bookmarkModePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 2, 0, 0) };
        bookmarkModePanel.Children.Add(new Label { Content = "Bookmark _mode:", Target = _bookmarkModeBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        bookmarkModePanel.Children.Add(_bookmarkModeBox);
        stack.Children.Add(bookmarkModePanel);
        _initialViewBox.Items.Add("Single page");
        _initialViewBox.Items.Add("One continuous column");
        _initialViewBox.Items.Add("Two columns, odd pages left");
        _initialViewBox.Items.Add("Two columns, odd pages right");
        _initialViewBox.SelectedIndex = 0;
        _openModeBox.Items.Add("Normal");
        _openModeBox.Items.Add("Bookmarks visible");
        _openModeBox.Items.Add("Full screen");
        _openModeBox.SelectedIndex = 0;
        var initialViewPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        initialViewPanel.Children.Add(new Label { Content = "Initial _view:", Target = _initialViewBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        initialViewPanel.Children.Add(_initialViewBox);
        stack.Children.Add(initialViewPanel);
        var openModePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        openModePanel.Children.Add(new Label { Content = "Open _mode:", Target = _openModeBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        openModePanel.Children.Add(_openModeBox);
        stack.Children.Add(openModePanel);
        stack.Children.Add(_bitmapTextBox);
        stack.Children.Add(_standardQualityButton);
        stack.Children.Add(_minimumSizeButton);

        stack.Children.Add(_openAfterPublishBox);

        _openAfterPublishBox.Margin = new Thickness(0, 8, 0, 18);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "_OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "_Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            ExportPageRange? pageRange = null;
            if (_pagesRangeButton.IsChecked == true &&
                !ExportPlanner.TryCreatePageRange(_fromPageBox.Text, _toPageBox.Text, out pageRange, out var error))
            {
                MessageBox.Show(this, error, "Export Options", MessageBoxButton.OK, MessageBoxImage.Warning);
                FocusInvalidPageRangeInput();
                return;
            }

            Result = CreateResult(
                _entireWorkbookButton.IsChecked == true
                    ? ExportContentScope.EntireWorkbook
                    : _selectionButton.IsChecked == true
                        ? ExportContentScope.Selection
                        : ExportContentScope.ActiveSheet,
                _documentPropertiesBox.IsChecked == true,
                _openAfterPublishBox.IsChecked == true,
                _ignorePrintAreasBox.IsChecked == true,
                pageRange,
                _minimumSizeButton.IsChecked == true
                    ? ExportQuality.MinimumSize
                    : ExportQuality.Standard,
                _bookmarksBox.IsChecked == true,
                GetSelectedBookmarkMode(),
                GetSelectedInitialView(),
                GetSelectedOpenMode(),
                _bitmapTextBox.IsChecked == true);
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _activeSheetButton.Focus();
        Keyboard.Focus(_activeSheetButton);
    }

    private void SetPageRangeFieldsEnabled(bool enabled)
    {
        _fromPageBox.IsEnabled = enabled;
        _toPageBox.IsEnabled = enabled;
    }

    private void FocusInvalidPageRangeInput()
    {
        _pagesRangeButton.IsChecked = true;
        SetPageRangeFieldsEnabled(true);
        _fromPageBox.Focus();
        _fromPageBox.SelectAll();
        Keyboard.Focus(_fromPageBox);
    }

    public static ExportOptions CreateResult(
        ExportContentScope scope,
        bool includeDocumentProperties,
        bool openAfterPublish,
        bool ignorePrintAreas = false,
        ExportPageRange? pageRange = null,
        ExportQuality quality = ExportQuality.Standard,
        bool createBookmarks = false,
        PdfBookmarkMode bookmarkMode = PdfBookmarkMode.None,
        PdfInitialView initialView = PdfInitialView.SinglePage,
        PdfOpenMode openMode = PdfOpenMode.Normal,
        bool bitmapTextWhenFontsMayNotBeEmbedded = false) =>
        new(
            Enum.IsDefined(scope) ? scope : ExportContentScope.ActiveSheet,
            includeDocumentProperties,
            openAfterPublish,
            ignorePrintAreas,
            pageRange,
            Enum.IsDefined(quality) ? quality : ExportQuality.Standard,
            createBookmarks,
            Enum.IsDefined(bookmarkMode) && bookmarkMode != PdfBookmarkMode.None
                ? bookmarkMode
                : createBookmarks
                    ? PdfBookmarkMode.SheetNames
                    : PdfBookmarkMode.None,
            Enum.IsDefined(initialView) ? initialView : PdfInitialView.SinglePage,
            Enum.IsDefined(openMode) ? openMode : PdfOpenMode.Normal,
            bitmapTextWhenFontsMayNotBeEmbedded);

    private PdfBookmarkMode GetSelectedBookmarkMode() =>
        _bookmarkModeBox.SelectedIndex switch
        {
            1 => PdfBookmarkMode.PrintTitles,
            2 => PdfBookmarkMode.PageNumbers,
            _ => PdfBookmarkMode.SheetNames
        };

    private PdfInitialView GetSelectedInitialView() =>
        _initialViewBox.SelectedIndex switch
        {
            1 => PdfInitialView.OneColumn,
            2 => PdfInitialView.TwoColumnLeft,
            3 => PdfInitialView.TwoColumnRight,
            _ => PdfInitialView.SinglePage
        };

    private PdfOpenMode GetSelectedOpenMode() =>
        _openModeBox.SelectedIndex switch
        {
            1 => PdfOpenMode.Outlines,
            2 => PdfOpenMode.FullScreen,
            _ => PdfOpenMode.Normal
        };
}

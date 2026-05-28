using System.Windows;
using System.Windows.Automation;
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
    private readonly CheckBox _bookmarksBox = new() { Content = "Create _PDF bookmarks" };
    private readonly CheckBox _bitmapTextBox = new() { Content = "_Bitmap text when fonts may not be embedded" };
    private readonly CheckBox _pdfABox = new() { Content = "PDF/_A compliant (not supported)", IsEnabled = false };
    private readonly CheckBox _structureTagsBox = new() { Content = "Document structure _tags (not supported)", IsEnabled = false };
    private readonly ComboBox _bookmarkModeBox = new() { Width = 180, IsEnabled = false };
    private readonly ComboBox _initialViewBox = new() { Width = 180 };
    private readonly ComboBox _openModeBox = new() { Width = 180 };
    private readonly TextBox _pdfLanguageBox = new() { Width = 88, Text = ExportPlanner.DefaultPdfLanguage };
    private readonly RadioButton _standardQualityButton = new() { Content = "_Standard", IsChecked = true };
    private readonly RadioButton _minimumSizeButton = new() { Content = "_Minimum size" };
    private readonly RadioButton _allPagesButton = new() { Content = "_All", GroupName = "PageRange", IsChecked = true };
    private readonly RadioButton _pagesRangeButton = new() { Content = "_Pages", GroupName = "PageRange" };
    private readonly TextBox _fromPageBox = new() { Width = 56 };
    private readonly TextBox _toPageBox = new() { Width = 56 };

    public ExportOptions Result { get; private set; } = ExportOptions.ExcelLikeDefault;

    public ExportOptionsDialog(bool hasSelection, string? initialPdfLanguage = null)
    {
        Title = "Export Options";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _pdfLanguageBox.Text = ExportPlanner.NormalizePdfLanguage(initialPdfLanguage);

        _selectionButton.IsEnabled = hasSelection;
        if (!hasSelection)
        {
            _selectionButton.ToolTip = "Select a cell range before exporting the selection.";
            AutomationProperties.SetHelpText(_selectionButton, "Select a cell range before exporting the selection.");
        }

        AutomationProperties.SetName(_fromPageBox, "From page");
        AutomationProperties.SetName(_toPageBox, "To page");

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
        _pagesRangeButton.Checked += (_, _) =>
        {
            SetPageRangeFieldsEnabled(true);
            DialogFocus.FocusAndSelect(_fromPageBox);
        };

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
        var pdfLanguagePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        pdfLanguagePanel.Children.Add(new Label { Content = "PDF _language:", Target = _pdfLanguageBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        pdfLanguagePanel.Children.Add(_pdfLanguageBox);
        stack.Children.Add(pdfLanguagePanel);
        stack.Children.Add(_bitmapTextBox);
        _pdfABox.ToolTip = "Freexcel's current PDF exporter cannot write PDF/A conformance metadata.";
        _structureTagsBox.ToolTip = "Freexcel's current PDF exporter cannot write tagged PDF structure trees.";
        AutomationProperties.SetHelpText(_pdfABox, "Freexcel's current PDF exporter cannot write PDF/A conformance metadata.");
        AutomationProperties.SetHelpText(_structureTagsBox, "Freexcel's current PDF exporter cannot write tagged PDF structure trees.");
        stack.Children.Add(_pdfABox);
        stack.Children.Add(_structureTagsBox);
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
                DialogMessageHelper.ShowWarning(this, error, "Export Options");
                FocusInvalidPageRangeInput(error);
                return;
            }

            if (!ExportPlanner.TryNormalizePdfLanguage(_pdfLanguageBox.Text, out var pdfLanguage, out var pdfLanguageError))
            {
                DialogMessageHelper.ShowWarning(this, pdfLanguageError, "Export Options");
                FocusInvalidPdfLanguageInput();
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
                _bitmapTextBox.IsChecked == true,
                pdfLanguage);
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        Content = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
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

    private void FocusInvalidPageRangeInput(string? error)
    {
        _pagesRangeButton.IsChecked = true;
        SetPageRangeFieldsEnabled(true);
        var target = ResolveInvalidPageRangeInput(error);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    private TextBox ResolveInvalidPageRangeInput(string? error)
    {
        return ExportOptionsDialogPlanner.ResolveInvalidPageRangeFocusTarget(error, _fromPageBox.Text) == ExportOptionsFocusTarget.ToPage
            ? _toPageBox
            : _fromPageBox;
    }

    private void FocusInvalidPdfLanguageInput()
    {
        _pdfLanguageBox.Focus();
        _pdfLanguageBox.SelectAll();
        Keyboard.Focus(_pdfLanguageBox);
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
        bool bitmapTextWhenFontsMayNotBeEmbedded = false,
        string? pdfLanguage = ExportPlanner.DefaultPdfLanguage,
        PdfConformance pdfConformance = PdfConformance.Standard,
        bool includeDocumentStructureTags = false) =>
        ExportOptionsDialogPlanner.CreateResult(
            scope,
            includeDocumentProperties,
            openAfterPublish,
            ignorePrintAreas,
            pageRange,
            quality,
            createBookmarks,
            bookmarkMode,
            initialView,
            openMode,
            bitmapTextWhenFontsMayNotBeEmbedded,
            pdfLanguage,
            pdfConformance,
            includeDocumentStructureTags);

    private PdfBookmarkMode GetSelectedBookmarkMode() =>
        ExportOptionsDialogPlanner.BookmarkModeFromIndex(_bookmarkModeBox.SelectedIndex);

    private PdfInitialView GetSelectedInitialView() =>
        ExportOptionsDialogPlanner.InitialViewFromIndex(_initialViewBox.SelectedIndex);

    private PdfOpenMode GetSelectedOpenMode() =>
        ExportOptionsDialogPlanner.OpenModeFromIndex(_openModeBox.SelectedIndex);
}

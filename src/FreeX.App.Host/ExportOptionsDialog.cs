using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

internal sealed class ExportOptionsDialog : Window
{
    private readonly RadioButton _activeSheetButton = new() { Content = UiText.Get("ExportOptions_ActiveSheetS"), IsChecked = true };
    private readonly RadioButton _selectionButton = new() { Content = UiText.Get("ExportOptions_SelectedRange") };
    private readonly RadioButton _entireWorkbookButton = new() { Content = UiText.Get("ExportOptions_Workbook") };
    private readonly CheckBox _documentPropertiesBox = new() { Content = UiText.Get("ExportOptions_IncludeDocumentProperties") };
    private readonly CheckBox _openAfterPublishBox = new() { Content = UiText.Get("ExportOptions_OpenAfterPublishing") };
    private readonly CheckBox _ignorePrintAreasBox = new() { Content = UiText.Get("ExportOptions_IgnorePrintAreas") };
    private readonly CheckBox _bookmarksBox = new() { Content = UiText.Get("ExportOptions_CreatePdfBookmarks") };
    private readonly CheckBox _bitmapTextBox = new() { Content = UiText.Get("ExportOptions_BitmapTextWhenFontsMayNotBeEmbedded") };
    private readonly CheckBox _pdfABox = new() { Content = UiText.Get("ExportOptions_PdfACompliantNotSupported"), IsEnabled = false };
    private readonly CheckBox _structureTagsBox = new() { Content = UiText.Get("ExportOptions_DocumentStructureTagsNotSupported"), IsEnabled = false };
    private readonly ComboBox _bookmarkModeBox = new() { Width = 180, IsEnabled = false };
    private readonly ComboBox _initialViewBox = new() { Width = 180 };
    private readonly ComboBox _openModeBox = new() { Width = 180 };
    private readonly TextBox _pdfLanguageBox = new() { Width = 88, Text = ExportPlanner.DefaultPdfLanguage };
    private readonly RadioButton _standardQualityButton = new() { Content = UiText.Get("ExportOptions_Standard"), IsChecked = true };
    private readonly RadioButton _minimumSizeButton = new() { Content = UiText.Get("ExportOptions_MinimumSize") };
    private readonly RadioButton _allPagesButton = new() { Content = UiText.Get("ExportOptions_All"), GroupName = "PageRange", IsChecked = true };
    private readonly RadioButton _pagesRangeButton = new() { Content = UiText.Get("ExportOptions_Pages"), GroupName = "PageRange" };
    private readonly TextBox _fromPageBox = new() { Width = 56 };
    private readonly TextBox _toPageBox = new() { Width = 56 };

    public ExportOptions Result { get; private set; } = ExportOptions.ExcelLikeDefault;

    public ExportOptionsDialog(bool hasSelection, string? initialPdfLanguage = null, ExportFormat format = ExportFormat.Pdf)
    {
        Title = UiText.Get("ExportOptions_ExportOptions");
        Width = 430;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _pdfLanguageBox.Text = ExportPlanner.NormalizePdfLanguage(initialPdfLanguage);

        _selectionButton.IsEnabled = hasSelection;
        if (!hasSelection)
        {
            _selectionButton.ToolTip = UiText.Get("ExportOptions_SelectACellRangeBeforeExportingTheSelection");
            AutomationProperties.SetHelpText(_selectionButton, UiText.Get("ExportOptions_SelectACellRangeBeforeExportingTheSelection"));
        }

        AutomationProperties.SetName(_fromPageBox, UiText.Get("ExportOptions_FromPage"));
        AutomationProperties.SetName(_toPageBox, UiText.Get("ExportOptions_ToPage"));

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = UiText.Get("ExportOptions_PublishWhat"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(_activeSheetButton);
        stack.Children.Add(_selectionButton);
        stack.Children.Add(_entireWorkbookButton);

        stack.Children.Add(new TextBlock { Text = UiText.Get("ExportOptions_PageRange"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        stack.Children.Add(_allPagesButton);
        var pageRangePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        pageRangePanel.Children.Add(_pagesRangeButton);
        pageRangePanel.Children.Add(new Label { Content = UiText.Get("ExportOptions_From"), Target = _fromPageBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        pageRangePanel.Children.Add(_fromPageBox);
        pageRangePanel.Children.Add(new Label { Content = UiText.Get("ExportOptions_To"), Target = _toPageBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
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

        stack.Children.Add(new TextBlock { Text = UiText.Get("ExportOptions_PdfXpsOptions"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        stack.Children.Add(_documentPropertiesBox);
        stack.Children.Add(_ignorePrintAreasBox);
        stack.Children.Add(_bookmarksBox);
        _bookmarkModeBox.Items.Add(UiText.Get("ExportOptions_SheetNames"));
        _bookmarkModeBox.Items.Add(UiText.Get("ExportOptions_PrintTitles"));
        _bookmarkModeBox.Items.Add(UiText.Get("ExportOptions_PageNumbers"));
        _bookmarkModeBox.SelectedIndex = 0;
        _bookmarksBox.Checked += (_, _) => _bookmarkModeBox.IsEnabled = _bookmarksBox.IsEnabled;
        _bookmarksBox.Unchecked += (_, _) => _bookmarkModeBox.IsEnabled = false;
        var bookmarkModePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 2, 0, 0) };
        bookmarkModePanel.Children.Add(new Label { Content = UiText.Get("ExportOptions_BookmarkMode"), Target = _bookmarkModeBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        bookmarkModePanel.Children.Add(_bookmarkModeBox);
        stack.Children.Add(bookmarkModePanel);
        _initialViewBox.Items.Add(UiText.Get("ExportOptions_SinglePage"));
        _initialViewBox.Items.Add(UiText.Get("ExportOptions_OneContinuousColumn"));
        _initialViewBox.Items.Add(UiText.Get("ExportOptions_TwoColumnsOddPagesLeft"));
        _initialViewBox.Items.Add(UiText.Get("ExportOptions_TwoColumnsOddPagesRight"));
        _initialViewBox.SelectedIndex = 0;
        _openModeBox.Items.Add(UiText.Get("ExportOptions_Normal"));
        _openModeBox.Items.Add(UiText.Get("ExportOptions_BookmarksVisible"));
        _openModeBox.Items.Add(UiText.Get("ExportOptions_FullScreen"));
        _openModeBox.SelectedIndex = 0;
        var initialViewPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        initialViewPanel.Children.Add(new Label { Content = UiText.Get("ExportOptions_InitialView"), Target = _initialViewBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        initialViewPanel.Children.Add(_initialViewBox);
        stack.Children.Add(initialViewPanel);
        var openModePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        openModePanel.Children.Add(new Label { Content = UiText.Get("ExportOptions_OpenMode"), Target = _openModeBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        openModePanel.Children.Add(_openModeBox);
        stack.Children.Add(openModePanel);
        var pdfLanguagePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        pdfLanguagePanel.Children.Add(new Label { Content = UiText.Get("ExportOptions_PdfLanguage"), Target = _pdfLanguageBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        pdfLanguagePanel.Children.Add(_pdfLanguageBox);
        stack.Children.Add(pdfLanguagePanel);
        stack.Children.Add(_bitmapTextBox);
        _pdfABox.ToolTip = UiText.Get("ExportOptions_FreeXSCurrentPdfExporterCannotWritePdfAConformanceMetadata");
        _structureTagsBox.ToolTip = UiText.Get("ExportOptions_FreeXSCurrentPdfExporterCannotWriteTaggedPdfStructureTrees");
        AutomationProperties.SetHelpText(_pdfABox, UiText.Get("ExportOptions_FreeXSCurrentPdfExporterCannotWritePdfAConformanceMetadata"));
        AutomationProperties.SetHelpText(_structureTagsBox, UiText.Get("ExportOptions_FreeXSCurrentPdfExporterCannotWriteTaggedPdfStructureTrees"));
        stack.Children.Add(_pdfABox);
        stack.Children.Add(_structureTagsBox);
        stack.Children.Add(_standardQualityButton);
        stack.Children.Add(_minimumSizeButton);

        stack.Children.Add(_openAfterPublishBox);

        _openAfterPublishBox.Margin = new Thickness(0, 8, 0, 18);
        ApplyFormatAvailability(ExportOptionsDialogPlanner.CreateFormatAvailability(format));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = UiText.Ok, Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = UiText.Cancel, Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            ExportPageRange? pageRange = null;
            if (_pagesRangeButton.IsChecked == true &&
                !ExportPlanner.TryCreatePageRange(_fromPageBox.Text, _toPageBox.Text, out pageRange, out var error))
            {
                DialogMessageHelper.ShowWarning(this, error, UiText.Get("ExportOptions_ExportOptions"));
                FocusInvalidPageRangeInput(error);
                return;
            }

            if (!ExportPlanner.TryNormalizePdfLanguage(_pdfLanguageBox.Text, out var pdfLanguage, out var pdfLanguageError))
            {
                DialogMessageHelper.ShowWarning(this, pdfLanguageError, UiText.Get("ExportOptions_ExportOptions"));
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
                pdfLanguage,
                format: format);
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

    private void ApplyFormatAvailability(ExportOptionsFormatAvailability availability)
    {
        if (!availability.PdfBookmarksEnabled)
        {
            DisableOption(_bookmarksBox, UiText.Get("Export_BookmarksPdfOnly"));
            DisableOption(_bookmarkModeBox, UiText.Get("Export_BookmarksPdfOnly"));
        }

        if (!availability.PdfInitialViewEnabled)
            DisableOption(_initialViewBox, UiText.Get("Export_InitialViewPdfOnly"));

        if (!availability.PdfOpenModeEnabled)
            DisableOption(_openModeBox, UiText.Get("Export_OpenModePdfOnly"));

        if (!availability.PdfLanguageEnabled)
            DisableOption(_pdfLanguageBox, UiText.Get("Export_PdfLanguagePdfOnly"));

        if (!availability.PdfBitmapTextEnabled)
            DisableOption(_bitmapTextBox, UiText.Get("Export_BitmapTextPdfOnly"));

        if (!availability.MinimumSizeEnabled)
            DisableOption(_minimumSizeButton, UiText.Get("Export_QualityMinimumSizePdfOnly"));
    }

    private static void DisableOption(Control control, string helpText)
    {
        control.IsEnabled = false;
        control.ToolTip = helpText;
        AutomationProperties.SetHelpText(control, helpText);
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
        bool includeDocumentStructureTags = false,
        ExportFormat format = ExportFormat.Pdf) =>
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
            includeDocumentStructureTags,
            format);

    private PdfBookmarkMode GetSelectedBookmarkMode() =>
        ExportOptionsDialogPlanner.BookmarkModeFromIndex(_bookmarkModeBox.SelectedIndex);

    private PdfInitialView GetSelectedInitialView() =>
        ExportOptionsDialogPlanner.InitialViewFromIndex(_initialViewBox.SelectedIndex);

    private PdfOpenMode GetSelectedOpenMode() =>
        ExportOptionsDialogPlanner.OpenModeFromIndex(_openModeBox.SelectedIndex);
}

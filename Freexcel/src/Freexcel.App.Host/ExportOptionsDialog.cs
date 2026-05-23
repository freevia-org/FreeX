using System.Windows;
using System.Windows.Controls;

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
        Height = 354;
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
                _bookmarksBox.IsChecked == true);
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        Content = stack;
    }

    private void SetPageRangeFieldsEnabled(bool enabled)
    {
        _fromPageBox.IsEnabled = enabled;
        _toPageBox.IsEnabled = enabled;
    }

    public static ExportOptions CreateResult(
        ExportContentScope scope,
        bool includeDocumentProperties,
        bool openAfterPublish,
        bool ignorePrintAreas = false,
        ExportPageRange? pageRange = null,
        ExportQuality quality = ExportQuality.Standard,
        bool createBookmarks = false) =>
        new(
            Enum.IsDefined(scope) ? scope : ExportContentScope.ActiveSheet,
            includeDocumentProperties,
            openAfterPublish,
            ignorePrintAreas,
            pageRange,
            Enum.IsDefined(quality) ? quality : ExportQuality.Standard,
            createBookmarks);
}

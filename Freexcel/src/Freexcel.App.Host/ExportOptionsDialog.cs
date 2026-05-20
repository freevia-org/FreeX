using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

internal sealed class ExportOptionsDialog : Window
{
    private readonly RadioButton _activeSheetButton = new() { Content = "Active sheet", IsChecked = true };
    private readonly RadioButton _selectionButton = new() { Content = "Selection" };
    private readonly RadioButton _entireWorkbookButton = new() { Content = "Entire workbook" };
    private readonly CheckBox _documentPropertiesBox = new() { Content = "Include document properties" };
    private readonly CheckBox _openAfterPublishBox = new() { Content = "Open after publishing" };
    private readonly TextBox _fromPageBox = new() { Width = 56 };
    private readonly TextBox _toPageBox = new() { Width = 56 };

    public ExportOptions Result { get; private set; } = ExportOptions.ExcelLikeDefault;

    public ExportOptionsDialog(bool hasSelection)
    {
        Title = "Export Options";
        Width = 360;
        Height = 305;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _selectionButton.IsEnabled = hasSelection;
        if (!hasSelection)
            _selectionButton.ToolTip = "Select a cell range before exporting the selection.";

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Publish what", Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(_activeSheetButton);
        stack.Children.Add(_selectionButton);
        stack.Children.Add(_entireWorkbookButton);

        var pageRangePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        pageRangePanel.Children.Add(new TextBlock { Text = "Pages from", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        pageRangePanel.Children.Add(_fromPageBox);
        pageRangePanel.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        pageRangePanel.Children.Add(_toPageBox);
        stack.Children.Add(pageRangePanel);

        stack.Children.Add(_documentPropertiesBox);
        stack.Children.Add(_openAfterPublishBox);

        _documentPropertiesBox.Margin = new Thickness(0, 14, 0, 4);
        _openAfterPublishBox.Margin = new Thickness(0, 0, 0, 18);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (!ExportPlanner.TryCreatePageRange(_fromPageBox.Text, _toPageBox.Text, out var pageRange, out var error))
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
                pageRange);
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        Content = stack;
    }

    public static ExportOptions CreateResult(
        ExportContentScope scope,
        bool includeDocumentProperties,
        bool openAfterPublish,
        ExportPageRange? pageRange = null) =>
        new(
            Enum.IsDefined(scope) ? scope : ExportContentScope.ActiveSheet,
            includeDocumentProperties,
            openAfterPublish,
            pageRange);
}

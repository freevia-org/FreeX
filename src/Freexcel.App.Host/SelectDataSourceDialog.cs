using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed partial class SelectDataSourceDialog : Window
{
    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _firstColumnCategoriesBox = new() { Content = "First column contains _category labels" };
    private readonly CheckBox _switchRowColumnBox = new() { Content = "_Switch Row/Column" };
    private readonly ListBox _seriesList = new() { Height = 72 };
    private readonly ListBox _axisLabelsList = new() { Height = 72 };
    private readonly Action<SelectDataSourceRangeSelectionRequest>? _requestRangeSelection;

    public SelectDataSourceDialogResult Result { get; private set; }
    public SelectDataSourceRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public SelectDataSourceDialog(
        string sourceRangeText,
        bool firstColumnIsCategories = true,
        Action<SelectDataSourceRangeSelectionRequest>? requestRangeSelection = null)
    {
        _requestRangeSelection = requestRangeSelection;
        Result = CreateResult(sourceRangeText, firstColumnIsCategories);
        Title = "Select Data Source";
        Width = 620;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = "_Chart data range:", Target = _rangeBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _rangeBox.Text = Result.SourceRangeText;
        stack.Children.Add(CreateReferenceEditor(_rangeBox, "Select chart data range"));
        _switchRowColumnBox.Margin = new Thickness(0, 10, 0, 8);
        stack.Children.Add(_switchRowColumnBox);
        stack.Children.Add(CreateSourceListPanel(
            "Legend Entries (Series)",
            "Series list",
            "Name and values are inferred from the selected chart range.",
            _seriesList,
            (("_Add series", AddSeriesButton_Click), ("_Edit series", EditSeriesButton_Click), ("_Remove series", RemoveSeriesButton_Click))));
        stack.Children.Add(CreateSourceListPanel(
            "Horizontal (Category) Axis Labels",
            "Axis label list",
            "Axis labels are inferred from the first category column.",
            _axisLabelsList,
            (("_Edit Axis Labels", EditAxisLabelsButton_Click), null, null)));
        _firstColumnCategoriesBox.IsChecked = firstColumnIsCategories;
        _firstColumnCategoriesBox.Margin = new Thickness(0, 10, 0, 8);
        stack.Children.Add(_firstColumnCategoriesBox);
        _firstColumnCategoriesBox.Checked += (_, _) => RefreshPreviewLists();
        _firstColumnCategoriesBox.Unchecked += (_, _) => RefreshPreviewLists();
        _rangeBox.TextChanged += (_, _) => RefreshPreviewLists();
        var hiddenEmptyButton = new Button
        {
            Content = "_Hidden and Empty Cells",
            Width = 150,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        };
        hiddenEmptyButton.Click += HiddenEmptyCellsButton_Click;
        stack.Children.Add(hiddenEmptyButton);
        RefreshPreviewLists();
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            Result = CreateResult(
                _rangeBox.Text,
                _firstColumnCategoriesBox.IsChecked == true,
                _switchRowColumnBox.IsChecked == true);
            DialogResult = true;
        }));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _rangeBox.Focus();
        _rangeBox.SelectAll();
        Keyboard.Focus(_rangeBox);
    }

    private DockPanel CreateReferenceEditor(TextBox textBox, string automationName) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request =>
            {
                RangeSelectionRequest = CreateRangeSelectionRequest(request.CurrentText);
                _requestRangeSelection?.Invoke(RangeSelectionRequest);
            });

    private void RefreshPreviewLists()
    {
        if (_seriesList is null || _axisLabelsList is null)
            return;

        var preview = InferPreviewEntries(_rangeBox.Text, _firstColumnCategoriesBox.IsChecked == true);
        _seriesList.ItemsSource = preview.Series.Select(series => $"{series.Name}    {series.ValuesRangeText}").ToList();
        _axisLabelsList.ItemsSource = preview.Categories.Select(category => category.Label).ToList();
    }

    private void AddSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        var index = _seriesList.Items.Count + 1;
        _seriesList.ItemsSource = null;
        _seriesList.Items.Add($"Series {index}    <select range>");
        _seriesList.SelectedIndex = _seriesList.Items.Count - 1;
    }

    private void EditSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_seriesList.SelectedIndex < 0 && _seriesList.Items.Count > 0)
            _seriesList.SelectedIndex = 0;
    }

    private void RemoveSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_seriesList.SelectedIndex < 0)
            return;

        var items = _seriesList.Items.Cast<object>().Select(item => item.ToString() ?? "").ToList();
        items.RemoveAt(_seriesList.SelectedIndex);
        _seriesList.ItemsSource = items;
    }

    private void EditAxisLabelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_axisLabelsList.Items.Count > 0)
            _axisLabelsList.SelectedIndex = 0;
    }

    private static void HiddenEmptyCellsButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Hidden rows and columns are not plotted. Empty cells are shown as gaps.",
            "Hidden and Empty Cell Settings",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

}

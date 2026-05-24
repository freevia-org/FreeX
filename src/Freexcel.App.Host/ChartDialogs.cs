using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record ChartTitlesDialogResult(string ChartTitle, string XAxisTitle, string YAxisTitle)
{
    public ChartLayoutOptions ToOptions() => new(
        Title: ChartTitle,
        XAxisTitle: XAxisTitle,
        YAxisTitle: YAxisTitle);
}

public sealed class ChartTitlesDialog : Window
{
    private readonly TextBox _chartTitleBox = new();
    private readonly TextBox _xAxisTitleBox = new();
    private readonly TextBox _yAxisTitleBox = new();

    public ChartTitlesDialogResult Result { get; private set; }

    public ChartTitlesDialog(string? chartTitle, string? xAxisTitle, string? yAxisTitle)
    {
        Result = CreateResult(chartTitle, xAxisTitle, yAxisTitle);
        Title = "Chart Titles";
        Width = 380;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _chartTitleBox.Text = chartTitle ?? "";
        _xAxisTitleBox.Text = xAxisTitle ?? "";
        _yAxisTitleBox.Text = yAxisTitle ?? "";

        var stack = new StackPanel { Margin = new Thickness(16) };
        AddInput(stack, "_Chart title:", _chartTitleBox);
        AddInput(stack, "_Primary horizontal axis title:", _xAxisTitleBox);
        AddInput(stack, "Primary _vertical axis title:", _yAxisTitleBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            Result = CreateResult(_chartTitleBox.Text, _xAxisTitleBox.Text, _yAxisTitleBox.Text);
            DialogResult = true;
        }));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChartTitlesDialogResult CreateResult(string? chartTitle, string? xAxisTitle, string? yAxisTitle) =>
        new(
            (chartTitle ?? "").Trim(),
            (xAxisTitle ?? "").Trim(),
            (yAxisTitle ?? "").Trim());

    private static void AddInput(Panel stack, string label, TextBox box)
    {
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(box);
    }

    private void FocusInitialKeyboardTarget()
    {
        _chartTitleBox.Focus();
        _chartTitleBox.SelectAll();
        Keyboard.Focus(_chartTitleBox);
    }
}

public sealed record ChartStyleDialogResult(int? ChartStyleId);

public sealed class ChartStyleDialog : Window
{
    private readonly ListBox _styleGallery = new();

    public ChartStyleDialogResult Result { get; private set; }

    public ChartStyleDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Chart Styles";
        Width = 480;
        Height = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var options = GetStyleOptions();
        _styleGallery.ItemsSource = options;
        _styleGallery.ItemTemplate = CreateStyleGalleryTemplate();
        var itemsPanelFactory = new FrameworkElementFactory(typeof(UniformGrid), "ChartStyleGalleryPanel");
        itemsPanelFactory.SetValue(UniformGrid.ColumnsProperty, 4);
        _styleGallery.ItemsPanel = new ItemsPanelTemplate(itemsPanelFactory);
        _styleGallery.SelectedItem = options.FirstOrDefault(option => option.StyleId == Result.ChartStyleId) ?? options[0];
        _styleGallery.Margin = new Thickness(0, 0, 0, 16);
        _styleGallery.Height = 230;
        AutomationProperties.SetName(_styleGallery, "Chart style gallery");

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = "_Style", Target = _styleGallery, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(_styleGallery);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static ChartStyleDialogResult FromChart(ChartModel chart) =>
        new(NormalizeStyleId(chart.ChartStyleId));

    public static ChartStyleDialogResult CreateResult(int? chartStyleId) =>
        new(NormalizeStyleId(chartStyleId));

    public static IReadOnlyList<ChartStyleOption> GetStyleOptions() =>
        new[] { new ChartStyleOption(null, "Automatic", "Use current chart formatting") }
            .Concat(Enumerable.Range(1, 48).Select(index => new ChartStyleOption(index, $"Style {index}", $"Preview style {index}")))
            .ToList();

    private void Accept()
    {
        Result = _styleGallery.SelectedItem is ChartStyleOption option
            ? CreateResult(option.StyleId)
            : CreateResult(null);
        DialogResult = true;
    }

    private static DataTemplate CreateStyleGalleryTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.MarginProperty, new Thickness(4));
        root.SetValue(StackPanel.WidthProperty, 96.0);

        root.AppendChild(CreateStylePreviewSwatch());

        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChartStyleOption.DisplayName)));
        label.SetValue(TextBlock.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        label.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
        root.AppendChild(label);

        var previewLabel = new FrameworkElementFactory(typeof(TextBlock));
        previewLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChartStyleOption.PreviewLabel)));
        previewLabel.SetValue(TextBlock.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        previewLabel.SetValue(TextBlock.ForegroundProperty, SystemColors.GrayTextBrush);
        previewLabel.SetValue(TextBlock.FontSizeProperty, 10.0);
        previewLabel.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        root.AppendChild(previewLabel);

        return new DataTemplate { VisualTree = root };
    }

    private static FrameworkElementFactory CreateStylePreviewSwatch()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderBrushProperty, SystemColors.ControlDarkBrush);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.HeightProperty, 42.0);
        border.SetValue(Border.BackgroundProperty, Brushes.White);

        var bars = new FrameworkElementFactory(typeof(StackPanel));
        bars.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        bars.SetValue(StackPanel.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        bars.SetValue(StackPanel.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Bottom);
        bars.SetValue(StackPanel.MarginProperty, new Thickness(0, 0, 0, 5));
        foreach (var height in new[] { 18.0, 28.0, 22.0 })
        {
            var bar = new FrameworkElementFactory(typeof(Border));
            bar.SetValue(Border.WidthProperty, 10.0);
            bar.SetValue(Border.HeightProperty, height);
            bar.SetValue(Border.MarginProperty, new Thickness(3, 0, 3, 0));
            bar.SetValue(Border.BackgroundProperty, SystemColors.HighlightBrush);
            bars.AppendChild(bar);
        }

        border.AppendChild(bars);
        return border;
    }

    private static int? NormalizeStyleId(int? value)
    {
        if (value is null)
            return null;

        return Math.Clamp(value.Value, 1, 48);
    }
}

public sealed record ChartStyleOption(int? StyleId, string DisplayName, string PreviewLabel);

public enum MoveChartTargetKind
{
    ObjectInSheet,
    NewChartSheet
}

public sealed record MoveChartDialogResult(MoveChartTargetKind TargetKind, string TargetName);

public sealed class MoveChartDialog : Window
{
    private readonly RadioButton _objectInSheet = new() { Content = "_Object in sheet", IsChecked = true };
    private readonly TextBox _targetBox = new();

    public MoveChartDialogResult Result { get; private set; }

    public MoveChartDialog(string currentSheetName)
    {
        Result = CreateObjectResult(currentSheetName);
        Title = "Move Chart";
        Width = 340;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        _targetBox.Text = currentSheetName;
        stack.Children.Add(_objectInSheet);
        stack.Children.Add(new RadioButton { Content = "_New chart sheet", Margin = new Thickness(0, 4, 0, 8) });
        stack.Children.Add(_targetBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static MoveChartDialogResult CreateObjectResult(string? sheetName) =>
        new(MoveChartTargetKind.ObjectInSheet, RequireTargetName(sheetName));

    public static MoveChartDialogResult CreateNewSheetResult(string? sheetName) =>
        new(MoveChartTargetKind.NewChartSheet, RequireTargetName(sheetName));

    private void Accept()
    {
        Result = _objectInSheet.IsChecked == true
            ? CreateObjectResult(_targetBox.Text)
            : CreateNewSheetResult(_targetBox.Text);
        DialogResult = true;
    }

    private static string RequireTargetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Target name is required.", nameof(name));
        return name.Trim();
    }
}

public sealed record SelectDataSourceDialogResult(
    string SourceRangeText,
    bool FirstColumnIsCategories,
    bool SwitchRowColumn = false);

public sealed record SelectDataSourceRangeSelectionRequest(string CurrentText, bool CollapseDialog = true);

public sealed record SelectDataSourceSeriesPreview(string Name, string ValuesRangeText);

public sealed record SelectDataSourceCategoryPreview(string Label);

public sealed record SelectDataSourcePreview(
    IReadOnlyList<SelectDataSourceSeriesPreview> Series,
    IReadOnlyList<SelectDataSourceCategoryPreview> Categories,
    string CategoryRangeText);

public sealed class SelectDataSourceDialog : Window
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
    }

    public static SelectDataSourceDialogResult CreateResult(
        string sourceRangeText,
        bool firstColumnIsCategories,
        bool switchRowColumn = false) =>
        new(sourceRangeText.Trim(), firstColumnIsCategories, switchRowColumn);

    public static SelectDataSourcePreview InferPreviewEntries(string sourceRangeText, bool firstColumnIsCategories)
    {
        var parsed = TryParseRangeReference(sourceRangeText);
        if (parsed is null)
        {
            return new SelectDataSourcePreview(
                [new SelectDataSourceSeriesPreview("Series 1", sourceRangeText.Trim())],
                [new SelectDataSourceCategoryPreview("Category labels")],
                "");
        }

        var (sheetName, startCol, startRow, endCol, endRow) = parsed.Value;
        var firstSeriesColumn = firstColumnIsCategories && endCol > startCol ? startCol + 1 : startCol;
        var firstDataRow = firstColumnIsCategories && endRow > startRow ? startRow + 1 : startRow;
        var series = new List<SelectDataSourceSeriesPreview>();
        for (var col = firstSeriesColumn; col <= endCol; col++)
        {
            var seriesName = $"Series {series.Count + 1}";
            series.Add(new SelectDataSourceSeriesPreview(
                seriesName,
                FormatRangeReference(sheetName, col, firstDataRow, col, endRow)));
        }

        if (series.Count == 0)
            series.Add(new SelectDataSourceSeriesPreview("Series 1", sourceRangeText.Trim()));

        var categories = new List<SelectDataSourceCategoryPreview>();
        var categoryStartRow = firstColumnIsCategories && endRow > startRow ? startRow + 1 : startRow;
        for (var row = categoryStartRow; row <= endRow; row++)
            categories.Add(new SelectDataSourceCategoryPreview($"Category {categories.Count + 1}"));

        if (categories.Count == 0)
            categories.Add(new SelectDataSourceCategoryPreview("Category labels"));

        var categoryRange = firstColumnIsCategories
            ? FormatRangeReference(sheetName, startCol, categoryStartRow, startCol, endRow)
            : "";

        return new SelectDataSourcePreview(series, categories, categoryRange);
    }

    public static SelectDataSourceRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    private DockPanel CreateReferenceEditor(TextBox textBox, string automationName) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request =>
            {
                RangeSelectionRequest = CreateRangeSelectionRequest(request.CurrentText);
                _requestRangeSelection?.Invoke(RangeSelectionRequest);
            });

    private static Grid CreateSourceListPanel(
        string title,
        string automationName,
        string helpText,
        ListBox list,
        ((string Label, RoutedEventHandler Handler) Add, (string Label, RoutedEventHandler Handler)? Edit, (string Label, RoutedEventHandler Handler)? Remove) buttons)
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 2) });
        header.Children.Add(CreateInlineHelp(helpText));
        panel.Children.Add(header);
        AutomationProperties.SetName(list, automationName);
        AutomationProperties.SetHelpText(list, helpText);
        Grid.SetRow(list, 1);
        panel.Children.Add(list);

        var buttonPanel = AddEditRemoveButtons(buttons);
        Grid.SetColumn(buttonPanel, 1);
        Grid.SetRowSpan(buttonPanel, 2);
        panel.Children.Add(buttonPanel);
        return panel;
    }

    private static StackPanel AddEditRemoveButtons(
        ((string Label, RoutedEventHandler Handler) Add, (string Label, RoutedEventHandler Handler)? Edit, (string Label, RoutedEventHandler Handler)? Remove) labels)
    {
        var stack = new StackPanel { Margin = new Thickness(8, 20, 0, 0) };
        stack.Children.Add(CreateSeriesButton(labels.Add.Label, labels.Add.Handler, new Thickness(0, 0, 0, 4)));
        if (labels.Edit is not null)
            stack.Children.Add(CreateSeriesButton(labels.Edit.Value.Label, labels.Edit.Value.Handler, new Thickness(0, 0, 0, 4)));
        if (labels.Remove is not null)
            stack.Children.Add(CreateSeriesButton(labels.Remove.Value.Label, labels.Remove.Value.Handler, new Thickness()));
        return stack;
    }

    private static Button CreateSeriesButton(string content, RoutedEventHandler handler, Thickness margin)
    {
        var button = new Button
        {
            Content = content,
            Width = 92,
            Margin = margin
        };
        button.Click += handler;
        return button;
    }

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

    private static (string? SheetName, uint StartCol, uint StartRow, uint EndCol, uint EndRow)? TryParseRangeReference(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return null;

        string? sheetName = null;
        var bangIndex = trimmed.LastIndexOf('!');
        if (bangIndex >= 0)
        {
            sheetName = trimmed[..bangIndex].Trim('\'');
            trimmed = trimmed[(bangIndex + 1)..];
        }

        var parts = trimmed.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            parts = [parts[0], parts[0]];
        if (parts.Length != 2)
            return null;

        if (!TryParseCellReference(parts[0], out var startCol, out var startRow)
            || !TryParseCellReference(parts[1], out var endCol, out var endRow))
            return null;

        return (
            sheetName,
            Math.Min(startCol, endCol),
            Math.Min(startRow, endRow),
            Math.Max(startCol, endCol),
            Math.Max(startRow, endRow));
    }

    private static bool TryParseCellReference(string text, out uint col, out uint row)
    {
        var normalized = text.Replace("$", "", StringComparison.Ordinal).Trim();
        var letterCount = normalized.TakeWhile(char.IsLetter).Count();
        col = 0;
        row = 0;
        if (letterCount == 0 || letterCount == normalized.Length)
            return false;

        col = CellAddress.ColumnNameToNumber(normalized[..letterCount]);
        return col > 0 && uint.TryParse(normalized[letterCount..], out row) && row > 0;
    }

    private static string FormatRangeReference(string? sheetName, uint startCol, uint startRow, uint endCol, uint endRow)
    {
        var prefix = string.IsNullOrWhiteSpace(sheetName) ? "" : $"{sheetName}!";
        var start = "$" + CellAddress.NumberToColumnName(startCol) + "$" + startRow;
        var end = "$" + CellAddress.NumberToColumnName(endCol) + "$" + endRow;
        return $"{prefix}{start}:{end}";
    }
}

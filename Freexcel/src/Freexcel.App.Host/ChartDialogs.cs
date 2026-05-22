using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
        AddInput(stack, "Chart title", _chartTitleBox);
        AddInput(stack, "Horizontal axis title", _xAxisTitleBox);
        AddInput(stack, "Vertical axis title", _yAxisTitleBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            Result = CreateResult(_chartTitleBox.Text, _xAxisTitleBox.Text, _yAxisTitleBox.Text);
            DialogResult = true;
        }));
        Content = stack;
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

public sealed class SelectDataSourceDialog : Window
{
    private const string DeferredSeriesEditingHelpText =
        "Edit the chart data range to change inferred series and category labels.";

    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _firstColumnCategoriesBox = new() { Content = "First column contains _category labels" };
    private readonly CheckBox _switchRowColumnBox = new() { Content = "_Switch Row/Column" };
    private readonly ListBox _seriesList = new() { Height = 72 };
    private readonly ListBox _axisLabelsList = new() { Height = 72 };

    public SelectDataSourceDialogResult Result { get; private set; }

    public SelectDataSourceDialog(string sourceRangeText, bool firstColumnIsCategories = true)
    {
        Result = CreateResult(sourceRangeText, firstColumnIsCategories);
        Title = "Select Data Source";
        Width = 520;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = "_Chart data range:", Target = _rangeBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _rangeBox.Text = Result.SourceRangeText;
        stack.Children.Add(CreateReferenceEditor(_rangeBox, "Select chart data range"));
        _switchRowColumnBox.Margin = new Thickness(0, 10, 0, 8);
        stack.Children.Add(_switchRowColumnBox);
        _seriesList.Items.Add("Series 1");
        _axisLabelsList.Items.Add("Category labels");
        stack.Children.Add(CreateSourceListPanel(
            "Legend Entries (Series)",
            "Series list",
            "Name and values are inferred from the selected chart range.",
            _seriesList,
            ("_Add series", "_Edit series", "_Remove series")));
        stack.Children.Add(CreateSourceListPanel(
            "Horizontal (Category) Axis Labels",
            "Axis label list",
            "Axis labels are inferred from the first category column.",
            _axisLabelsList,
            ("_Edit Axis Labels", null, null)));
        _firstColumnCategoriesBox.IsChecked = firstColumnIsCategories;
        _firstColumnCategoriesBox.Margin = new Thickness(0, 10, 0, 16);
        stack.Children.Add(_firstColumnCategoriesBox);
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

    private static DockPanel CreateReferenceEditor(TextBox textBox, string automationName)
    {
        var panel = new DockPanel();
        var pickerButton = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = textBox
        };
        AutomationProperties.SetName(pickerButton, automationName);
        pickerButton.Click += ReferencePickerButton_Click;
        panel.Children.Add(pickerButton);
        panel.Children.Add(textBox);
        return panel;
    }

    private static void ReferencePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TextBox textBox })
            return;

        textBox.Focus();
        textBox.SelectAll();
    }

    private static Grid CreateSourceListPanel(
        string title,
        string automationName,
        string helpText,
        ListBox list,
        (string Add, string? Edit, string? Remove) buttons)
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

    private static StackPanel AddEditRemoveButtons((string Add, string? Edit, string? Remove) labels)
    {
        var stack = new StackPanel { Margin = new Thickness(8, 20, 0, 0) };
        stack.Children.Add(CreateDeferredSeriesButton(labels.Add, new Thickness(0, 0, 0, 4)));
        if (labels.Edit is not null)
            stack.Children.Add(CreateDeferredSeriesButton(labels.Edit, new Thickness(0, 0, 0, 4)));
        if (labels.Remove is not null)
            stack.Children.Add(CreateDeferredSeriesButton(labels.Remove, new Thickness()));
        return stack;
    }

    private static Button CreateDeferredSeriesButton(string content, Thickness margin)
    {
        var button = new Button
        {
            Content = content,
            Width = 92,
            Margin = margin,
            IsEnabled = false,
            ToolTip = DeferredSeriesEditingHelpText
        };
        AutomationProperties.SetHelpText(button, DeferredSeriesEditingHelpText);
        return button;
    }
}

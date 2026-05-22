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

public sealed record ChartAreaLegendDialogResult(
    CellColor? ChartAreaFillColor,
    CellColor? PlotAreaFillColor,
    CellColor? PlotAreaBorderColor,
    double PlotAreaBorderThickness,
    bool ShowLegend,
    ChartLegendPosition LegendPosition,
    bool LegendOverlay,
    CellColor? LegendTextColor,
    CellColor? LegendFillColor,
    CellColor? LegendBorderColor,
    double LegendBorderThickness,
    double LegendFontSize)
{
    public ChartLayoutOptions ToOptions() => new(
        ChartAreaFillColor: ChartAreaFillColor,
        PlotAreaFillColor: PlotAreaFillColor,
        PlotAreaBorderColor: PlotAreaBorderColor,
        PlotAreaBorderThickness: PlotAreaBorderThickness,
        ShowLegend: ShowLegend,
        LegendPosition: LegendPosition,
        LegendOverlay: LegendOverlay,
        LegendTextColor: LegendTextColor,
        LegendFillColor: LegendFillColor,
        LegendBorderColor: LegendBorderColor,
        LegendBorderThickness: LegendBorderThickness,
        LegendFontSize: LegendFontSize);
}

public sealed class ChartAreaLegendDialog : Window
{
    private readonly TextBox _chartAreaFillBox = new();
    private readonly TextBox _plotAreaFillBox = new();
    private readonly TextBox _plotAreaBorderBox = new();
    private readonly TextBox _plotAreaBorderThicknessBox = new();
    private readonly CheckBox _showLegendBox = new() { Content = "_Show legend" };
    private readonly ComboBox _legendPositionBox = new();
    private readonly CheckBox _legendOverlayBox = new() { Content = "O_verlay legend on chart" };
    private readonly TextBox _legendTextBox = new();
    private readonly TextBox _legendFillBox = new();
    private readonly TextBox _legendBorderBox = new();
    private readonly TextBox _legendBorderThicknessBox = new();
    private readonly TextBox _legendFontSizeBox = new();

    public ChartAreaLegendDialogResult Result { get; private set; }

    public ChartAreaLegendDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Chart Area";
        Width = 420;
        Height = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartAreaLegendDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ChartAreaFillColor,
        chart.PlotAreaFillColor,
        chart.PlotAreaBorderColor,
        chart.PlotAreaBorderThickness,
        chart.ShowLegend,
        chart.LegendPosition,
        chart.LegendOverlay,
        chart.LegendTextColor,
        chart.LegendFillColor,
        chart.LegendBorderColor,
        chart.LegendBorderThickness,
        chart.LegendFontSize);

    public static ChartAreaLegendDialogResult CreateResult(
        CellColor? chartAreaFillColor,
        CellColor? plotAreaFillColor,
        CellColor? plotAreaBorderColor,
        double plotAreaBorderThickness,
        bool showLegend,
        ChartLegendPosition legendPosition,
        bool legendOverlay,
        CellColor? legendTextColor,
        CellColor? legendFillColor,
        CellColor? legendBorderColor,
        double legendBorderThickness,
        double legendFontSize) =>
        new(
            chartAreaFillColor,
            plotAreaFillColor,
            plotAreaBorderColor,
            Math.Clamp(FiniteOrDefault(plotAreaBorderThickness, 1), 0, 10),
            showLegend,
            Enum.IsDefined(legendPosition) ? legendPosition : ChartLegendPosition.Right,
            legendOverlay,
            legendTextColor,
            legendFillColor,
            legendBorderColor,
            Math.Clamp(FiniteOrDefault(legendBorderThickness, 0), 0, 10),
            Math.Clamp(FiniteOrDefault(legendFontSize, 12), 6, 72));

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            stack.Children.Add(CreateInlineHelp("Set the chart and plot area fills, borders, and line weights."));
            ChartDialogHelpers.AddColorText(stack, "Chart area fill color", _chartAreaFillBox);
            ChartDialogHelpers.AddColorText(stack, "Plot area fill color", _plotAreaFillBox);
            ChartDialogHelpers.AddColorText(stack, "Plot area border color", _plotAreaBorderBox);
            ChartDialogHelpers.AddNumericText(stack, "Plot area border width", _plotAreaBorderThicknessBox, "Enter a line width from 0 to 10 points.");
            root.Children.Add(CreateGroupBox("Fill & Line", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showLegendBox);
            ChartDialogHelpers.AddCombo(stack, "Legend position", _legendPositionBox, Enum.GetValues<ChartLegendPosition>());
            ChartDialogHelpers.AddCheck(stack, _legendOverlayBox);
            ChartDialogHelpers.AddColorText(stack, "Legend text color", _legendTextBox);
            ChartDialogHelpers.AddColorText(stack, "Legend fill color", _legendFillBox);
            ChartDialogHelpers.AddColorText(stack, "Legend border color", _legendBorderBox);
            ChartDialogHelpers.AddNumericText(stack, "Legend border width", _legendBorderThicknessBox, "Enter a line width from 0 to 10 points.");
            ChartDialogHelpers.AddNumericText(stack, "Legend font size", _legendFontSizeBox, "Enter a font size from 6 to 72 points.");
            root.Children.Add(CreateGroupBox("Legend", stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartAreaLegendDialogResult result)
    {
        _chartAreaFillBox.Text = ChartDialogHelpers.FormatColor(result.ChartAreaFillColor);
        _plotAreaFillBox.Text = ChartDialogHelpers.FormatColor(result.PlotAreaFillColor);
        _plotAreaBorderBox.Text = ChartDialogHelpers.FormatColor(result.PlotAreaBorderColor);
        _plotAreaBorderThicknessBox.Text = result.PlotAreaBorderThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _showLegendBox.IsChecked = result.ShowLegend;
        _legendPositionBox.SelectedItem = result.LegendPosition;
        _legendOverlayBox.IsChecked = result.LegendOverlay;
        _legendTextBox.Text = ChartDialogHelpers.FormatColor(result.LegendTextColor);
        _legendFillBox.Text = ChartDialogHelpers.FormatColor(result.LegendFillColor);
        _legendBorderBox.Text = ChartDialogHelpers.FormatColor(result.LegendBorderColor);
        _legendBorderThicknessBox.Text = result.LegendBorderThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _legendFontSizeBox.Text = result.LegendFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Accept()
    {
        Result = CreateResult(
            ChartDialogHelpers.ParseColor(_chartAreaFillBox.Text),
            ChartDialogHelpers.ParseColor(_plotAreaFillBox.Text),
            ChartDialogHelpers.ParseColor(_plotAreaBorderBox.Text),
            ChartDialogHelpers.ParseDouble(_plotAreaBorderThicknessBox.Text, 1),
            _showLegendBox.IsChecked == true,
            ChartDialogHelpers.Selected(_legendPositionBox, ChartLegendPosition.Right),
            _legendOverlayBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_legendTextBox.Text),
            ChartDialogHelpers.ParseColor(_legendFillBox.Text),
            ChartDialogHelpers.ParseColor(_legendBorderBox.Text),
            ChartDialogHelpers.ParseDouble(_legendBorderThicknessBox.Text, 0),
            ChartDialogHelpers.ParseDouble(_legendFontSizeBox.Text, 12));
        DialogResult = true;
    }

    private static double FiniteOrDefault(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;
}

public sealed record ChartDataLabelsDialogResult(
    bool ShowDataLabels,
    ChartDataLabelPosition Position,
    bool ShowCategoryName,
    bool ShowSeriesName,
    bool ShowPercentage,
    ChartDataLabelSeparator Separator,
    ChartDataLabelNumberFormat NumberFormat,
    bool ShowCallouts,
    CellColor? FillColor,
    CellColor? BorderColor,
    CellColor? TextColor,
    double BorderThickness,
    double FontSize,
    double Angle)
{
    public ChartLayoutOptions ToOptions() => new(
        ShowDataLabels: ShowDataLabels,
        DataLabelPosition: Position,
        ShowDataLabelCategoryName: ShowCategoryName,
        ShowDataLabelSeriesName: ShowSeriesName,
        ShowDataLabelPercentage: ShowPercentage,
        DataLabelSeparator: Separator,
        DataLabelNumberFormat: NumberFormat,
        ShowDataLabelCallouts: ShowCallouts,
        DataLabelFillColor: FillColor,
        DataLabelBorderColor: BorderColor,
        DataLabelTextColor: TextColor,
        DataLabelBorderThickness: BorderThickness,
        DataLabelFontSize: FontSize,
        DataLabelAngle: Angle);
}

public sealed class ChartDataLabelsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = "_Show data labels" };
    private readonly CheckBox _categoryBox = new() { Content = "_Category name" };
    private readonly CheckBox _seriesBox = new() { Content = "_Series name" };
    private readonly CheckBox _percentageBox = new() { Content = "_Percentage" };
    private readonly CheckBox _calloutsBox = new() { Content = "Data label _callouts" };
    private readonly ComboBox _positionBox = new();
    private readonly ComboBox _separatorBox = new();
    private readonly ComboBox _numberFormatBox = new();
    private readonly TextBox _fillBox = new();
    private readonly TextBox _borderBox = new();
    private readonly TextBox _textBox = new();
    private readonly TextBox _borderThicknessBox = new();
    private readonly TextBox _fontSizeBox = new();
    private readonly TextBox _angleBox = new();

    public ChartDataLabelsDialogResult Result { get; private set; }

    public ChartDataLabelsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Data Labels";
        Width = 420;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartDataLabelsDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ShowDataLabels,
        chart.DataLabelPosition,
        chart.ShowDataLabelCategoryName,
        chart.ShowDataLabelSeriesName,
        chart.ShowDataLabelPercentage,
        chart.DataLabelSeparator,
        chart.DataLabelNumberFormat,
        chart.ShowDataLabelCallouts,
        chart.DataLabelFillColor,
        chart.DataLabelBorderColor,
        chart.DataLabelTextColor,
        chart.DataLabelBorderThickness,
        chart.DataLabelFontSize,
        chart.DataLabelAngle);

    public static ChartDataLabelsDialogResult CreateResult(
        bool showDataLabels,
        ChartDataLabelPosition position,
        bool showCategoryName,
        bool showSeriesName,
        bool showPercentage,
        ChartDataLabelSeparator separator,
        ChartDataLabelNumberFormat numberFormat,
        bool showCallouts,
        CellColor? fillColor,
        CellColor? borderColor,
        CellColor? textColor,
        double borderThickness,
        double fontSize,
        double angle) =>
        new(showDataLabels, position, showCategoryName, showSeriesName, showPercentage, separator, numberFormat,
            showCallouts, fillColor, borderColor, textColor, borderThickness, fontSize, angle);

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showBox);
            ChartDialogHelpers.AddCombo(stack, "Position", _positionBox, Enum.GetValues<ChartDataLabelPosition>());
            ChartDialogHelpers.AddCheck(stack, _categoryBox);
            ChartDialogHelpers.AddCheck(stack, _seriesBox);
            ChartDialogHelpers.AddCheck(stack, _percentageBox);
            ChartDialogHelpers.AddCombo(stack, "Separator", _separatorBox, Enum.GetValues<ChartDataLabelSeparator>());
            ChartDialogHelpers.AddCombo(stack, "Number format", _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
            ChartDialogHelpers.AddCheck(stack, _calloutsBox);
            root.Children.Add(CreateGroupBox("Label Options", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, "Fill color", _fillBox);
            ChartDialogHelpers.AddColorText(stack, "Border color", _borderBox);
            ChartDialogHelpers.AddColorText(stack, "Text color", _textBox);
            ChartDialogHelpers.AddNumericText(stack, "Border thickness", _borderThicknessBox, "Enter a border width in points.");
            ChartDialogHelpers.AddNumericText(stack, "Font size", _fontSizeBox, "Enter a font size in points.");
            ChartDialogHelpers.AddNumericText(stack, "Text angle", _angleBox, "Enter degrees from -90 to 90.");
            root.Children.Add(CreateGroupBox("Fill & Line", stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartDataLabelsDialogResult result)
    {
        _showBox.IsChecked = result.ShowDataLabels;
        _positionBox.SelectedItem = result.Position;
        _categoryBox.IsChecked = result.ShowCategoryName;
        _seriesBox.IsChecked = result.ShowSeriesName;
        _percentageBox.IsChecked = result.ShowPercentage;
        _separatorBox.SelectedItem = result.Separator;
        _numberFormatBox.SelectedItem = result.NumberFormat;
        _calloutsBox.IsChecked = result.ShowCallouts;
        _fillBox.Text = ChartDialogHelpers.FormatColor(result.FillColor);
        _borderBox.Text = ChartDialogHelpers.FormatColor(result.BorderColor);
        _textBox.Text = ChartDialogHelpers.FormatColor(result.TextColor);
        _borderThicknessBox.Text = result.BorderThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _fontSizeBox.Text = result.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _angleBox.Text = result.Angle.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Accept()
    {
        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_positionBox, ChartDataLabelPosition.BestFit),
            _categoryBox.IsChecked == true,
            _seriesBox.IsChecked == true,
            _percentageBox.IsChecked == true,
            ChartDialogHelpers.Selected(_separatorBox, ChartDataLabelSeparator.Comma),
            ChartDialogHelpers.Selected(_numberFormatBox, ChartDataLabelNumberFormat.General),
            _calloutsBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_fillBox.Text),
            ChartDialogHelpers.ParseColor(_borderBox.Text),
            ChartDialogHelpers.ParseColor(_textBox.Text),
            ChartDialogHelpers.ParseDouble(_borderThicknessBox.Text, 0),
            ChartDialogHelpers.ParseDouble(_fontSizeBox.Text, 11),
            ChartDialogHelpers.ParseDouble(_angleBox.Text, 0));
        DialogResult = true;
    }
}

public sealed record ChartTrendlineOptionsDialogResult(
    bool ShowTrendline,
    ChartTrendlineType Type,
    int Period,
    int Order,
    bool ShowEquation,
    bool ShowRSquared,
    CellColor? Color,
    double Thickness,
    ChartLineDashStyle DashStyle)
{
    public ChartLayoutOptions ToOptions() => new(
        ShowLinearTrendline: ShowTrendline,
        TrendlineType: Type,
        TrendlinePeriod: Period,
        TrendlineOrder: Order,
        ShowTrendlineEquation: ShowEquation,
        ShowTrendlineRSquared: ShowRSquared,
        TrendlineColor: Color,
        TrendlineThickness: Thickness,
        TrendlineDashStyle: DashStyle);
}

public sealed class ChartTrendlineOptionsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = "_Show trendline" };
    private readonly CheckBox _equationBox = new() { Content = "Display _equation" };
    private readonly CheckBox _rSquaredBox = new() { Content = "Display _R-squared value" };
    private readonly ComboBox _typeBox = new();
    private readonly ComboBox _dashBox = new();
    private readonly TextBox _periodBox = new();
    private readonly TextBox _orderBox = new();
    private readonly TextBox _colorBox = new();
    private readonly TextBox _thicknessBox = new();

    public ChartTrendlineOptionsDialogResult Result { get; private set; }

    public ChartTrendlineOptionsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Trendline";
        Width = 380;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartTrendlineOptionsDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ShowLinearTrendline,
        chart.TrendlineType,
        chart.TrendlinePeriod,
        chart.TrendlineOrder,
        chart.ShowTrendlineEquation,
        chart.ShowTrendlineRSquared,
        chart.TrendlineColor,
        chart.TrendlineThickness,
        chart.TrendlineDashStyle);

    public static ChartTrendlineOptionsDialogResult CreateResult(
        bool showTrendline,
        ChartTrendlineType type,
        int period,
        int order,
        bool showEquation,
        bool showRSquared,
        CellColor? color,
        double thickness,
        ChartLineDashStyle dashStyle) =>
        new(showTrendline, type, Math.Clamp(period, 2, 255), Math.Clamp(order, 2, 6), showEquation, showRSquared, color, thickness, dashStyle);

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _showBox);
            ChartDialogHelpers.AddCombo(stack, "Type", _typeBox, Enum.GetValues<ChartTrendlineType>());
            ChartDialogHelpers.AddNumericText(stack, "Moving average period", _periodBox, "Enter a period from 2 to 255.");
            ChartDialogHelpers.AddNumericText(stack, "Polynomial order", _orderBox, "Enter an order from 2 to 6.");
            ChartDialogHelpers.AddCheck(stack, _equationBox);
            ChartDialogHelpers.AddCheck(stack, _rSquaredBox);
            root.Children.Add(CreateGroupBox("Trendline Options", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, "Line color", _colorBox);
            ChartDialogHelpers.AddNumericText(stack, "Line width", _thicknessBox, "Enter a line width in points.");
            ChartDialogHelpers.AddCombo(stack, "Dash style", _dashBox, Enum.GetValues<ChartLineDashStyle>());
            root.Children.Add(CreateGroupBox("Fill & Line", stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartTrendlineOptionsDialogResult result)
    {
        _showBox.IsChecked = result.ShowTrendline;
        _typeBox.SelectedItem = result.Type;
        _periodBox.Text = result.Period.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _orderBox.Text = result.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _equationBox.IsChecked = result.ShowEquation;
        _rSquaredBox.IsChecked = result.ShowRSquared;
        _colorBox.Text = ChartDialogHelpers.FormatColor(result.Color);
        _thicknessBox.Text = result.Thickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _dashBox.SelectedItem = result.DashStyle;
    }

    private void Accept()
    {
        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_typeBox, ChartTrendlineType.Linear),
            (int)ChartDialogHelpers.ParseDouble(_periodBox.Text, 2),
            (int)ChartDialogHelpers.ParseDouble(_orderBox.Text, 2),
            _equationBox.IsChecked == true,
            _rSquaredBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_colorBox.Text),
            ChartDialogHelpers.ParseDouble(_thicknessBox.Text, 1.5),
            ChartDialogHelpers.Selected(_dashBox, ChartLineDashStyle.Solid));
        DialogResult = true;
    }
}

public sealed record ChartErrorBarsDialogResult(
    bool ShowErrorBars,
    ChartErrorBarKind Kind,
    ChartErrorBarDirection Direction,
    double Value,
    bool EndCaps)
{
    public ChartLayoutOptions ToOptions() => new(
        ShowErrorBars: ShowErrorBars,
        ErrorBarKind: Kind,
        ErrorBarDirection: Direction,
        ErrorBarValue: Value,
        ErrorBarEndCaps: EndCaps);
}

public sealed class ChartErrorBarsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = "_Show error bars" };
    private readonly CheckBox _endCapsBox = new() { Content = "_End caps" };
    private readonly ComboBox _kindBox = new();
    private readonly ComboBox _directionBox = new();
    private readonly TextBox _valueBox = new();

    public ChartErrorBarsDialogResult Result { get; private set; }

    public ChartErrorBarsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Error Bars";
        Width = 360;
        Height = 290;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartErrorBarsDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ShowErrorBars,
        chart.ErrorBarKind,
        chart.ErrorBarDirection,
        chart.ErrorBarValue,
        chart.ErrorBarEndCaps);

    public static ChartErrorBarsDialogResult CreateResult(
        bool showErrorBars,
        ChartErrorBarKind kind,
        ChartErrorBarDirection direction,
        double value,
        bool endCaps) =>
        new(
            showErrorBars,
            Enum.IsDefined(kind) ? kind : ChartErrorBarKind.StandardError,
            Enum.IsDefined(direction) ? direction : ChartErrorBarDirection.Both,
            Math.Clamp(double.IsFinite(value) ? value : 5, 0, 1000),
            endCaps);

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        var stack = new StackPanel();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, "Type", _kindBox, Enum.GetValues<ChartErrorBarKind>());
        ChartDialogHelpers.AddCombo(stack, "Direction", _directionBox, Enum.GetValues<ChartErrorBarDirection>());
        ChartDialogHelpers.AddNumericText(stack, "Value", _valueBox, "Enter the error amount or percentage.");
        ChartDialogHelpers.AddCheck(stack, _endCapsBox);
        root.Children.Add(CreateGroupBox("Error Amount", stack));
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartErrorBarsDialogResult result)
    {
        _showBox.IsChecked = result.ShowErrorBars;
        _kindBox.SelectedItem = result.Kind;
        _directionBox.SelectedItem = result.Direction;
        _valueBox.Text = result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _endCapsBox.IsChecked = result.EndCaps;
    }

    private void Accept()
    {
        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_kindBox, ChartErrorBarKind.StandardError),
            ChartDialogHelpers.Selected(_directionBox, ChartErrorBarDirection.Both),
            ChartDialogHelpers.ParseDouble(_valueBox.Text, 5),
            _endCapsBox.IsChecked == true);
        DialogResult = true;
    }
}

public sealed record ChartAxisFormatDialogResult(
    bool UseXAxis,
    double? Minimum,
    double? Maximum,
    double? MajorUnit,
    double? MinorUnit,
    bool LogScale,
    ChartDataLabelNumberFormat NumberFormat,
    bool ShowMajorGridlines,
    bool ShowMinorGridlines,
    CellColor? MajorGridlineColor,
    CellColor? MinorGridlineColor,
    double GridlineThickness,
    ChartAxisTickStyle MajorTickStyle,
    ChartAxisTickStyle MinorTickStyle,
    bool ShowLabels,
    CellColor? LabelTextColor,
    double LabelFontSize,
    double LabelAngle,
    CellColor? LineColor,
    double LineThickness)
{
    public ChartLayoutOptions ToOptions() => UseXAxis
        ? new ChartLayoutOptions(
            XAxisMinimum: Minimum,
            XAxisMaximum: Maximum,
            XAxisMajorUnit: MajorUnit,
            XAxisMinorUnit: MinorUnit,
            XAxisLogScale: LogScale,
            XAxisNumberFormat: NumberFormat,
            ShowXAxisMajorGridlines: ShowMajorGridlines,
            ShowXAxisMinorGridlines: ShowMinorGridlines,
            XAxisMajorGridlineColor: MajorGridlineColor,
            XAxisMinorGridlineColor: MinorGridlineColor,
            XAxisGridlineThickness: GridlineThickness,
            XAxisMajorTickStyle: MajorTickStyle,
            XAxisMinorTickStyle: MinorTickStyle,
            ShowXAxisLabels: ShowLabels,
            XAxisLabelTextColor: LabelTextColor,
            XAxisLabelFontSize: LabelFontSize,
            XAxisLabelAngle: LabelAngle,
            XAxisLineColor: LineColor,
            XAxisLineThickness: LineThickness,
            ClearXAxisBounds: Minimum is null && Maximum is null)
        : new ChartLayoutOptions(
            YAxisMinimum: Minimum,
            YAxisMaximum: Maximum,
            YAxisMajorUnit: MajorUnit,
            YAxisMinorUnit: MinorUnit,
            YAxisLogScale: LogScale,
            YAxisNumberFormat: NumberFormat,
            ShowYAxisMajorGridlines: ShowMajorGridlines,
            ShowYAxisMinorGridlines: ShowMinorGridlines,
            YAxisMajorGridlineColor: MajorGridlineColor,
            YAxisMinorGridlineColor: MinorGridlineColor,
            YAxisGridlineThickness: GridlineThickness,
            YAxisMajorTickStyle: MajorTickStyle,
            YAxisMinorTickStyle: MinorTickStyle,
            ShowYAxisLabels: ShowLabels,
            YAxisLabelTextColor: LabelTextColor,
            YAxisLabelFontSize: LabelFontSize,
            YAxisLabelAngle: LabelAngle,
            YAxisLineColor: LineColor,
            YAxisLineThickness: LineThickness,
            ClearYAxisBounds: Minimum is null && Maximum is null);
}

public sealed class ChartAxisFormatDialog : Window
{
    private readonly bool _useXAxis;
    private readonly TextBox _minimumBox = new();
    private readonly TextBox _maximumBox = new();
    private readonly TextBox _majorUnitBox = new();
    private readonly TextBox _minorUnitBox = new();
    private readonly CheckBox _logBox = new() { Content = "_Logarithmic scale" };
    private readonly ComboBox _numberFormatBox = new();
    private readonly CheckBox _majorGridBox = new() { Content = "_Major gridlines" };
    private readonly CheckBox _minorGridBox = new() { Content = "M_inor gridlines" };
    private readonly TextBox _majorGridColorBox = new();
    private readonly TextBox _minorGridColorBox = new();
    private readonly TextBox _gridlineThicknessBox = new();
    private readonly ComboBox _majorTickBox = new();
    private readonly ComboBox _minorTickBox = new();
    private readonly CheckBox _labelsBox = new() { Content = "Show _labels" };
    private readonly TextBox _labelColorBox = new();
    private readonly TextBox _labelFontSizeBox = new();
    private readonly TextBox _labelAngleBox = new();
    private readonly TextBox _lineColorBox = new();
    private readonly TextBox _lineThicknessBox = new();

    public ChartAxisFormatDialogResult Result { get; private set; }

    public ChartAxisFormatDialog(ChartModel chart, bool useXAxis)
    {
        _useXAxis = useXAxis;
        Result = FromChart(chart, useXAxis);
        Title = useXAxis ? "Format X Axis" : "Format Y Axis";
        Width = 430;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartAxisFormatDialogResult FromChart(ChartModel chart, bool useXAxis) => useXAxis
        ? CreateResult(true, chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisMajorUnit, chart.XAxisMinorUnit,
            chart.XAxisLogScale, chart.XAxisNumberFormat, chart.ShowXAxisMajorGridlines, chart.ShowXAxisMinorGridlines,
            chart.XAxisMajorGridlineColor, chart.XAxisMinorGridlineColor, chart.XAxisGridlineThickness,
            chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle, chart.ShowXAxisLabels, chart.XAxisLabelTextColor,
            chart.XAxisLabelFontSize, chart.XAxisLabelAngle, chart.XAxisLineColor, chart.XAxisLineThickness)
        : CreateResult(false, chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit, chart.YAxisMinorUnit,
            chart.YAxisLogScale, chart.YAxisNumberFormat, chart.ShowYAxisMajorGridlines, chart.ShowYAxisMinorGridlines,
            chart.YAxisMajorGridlineColor, chart.YAxisMinorGridlineColor, chart.YAxisGridlineThickness,
            chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle, chart.ShowYAxisLabels, chart.YAxisLabelTextColor,
            chart.YAxisLabelFontSize, chart.YAxisLabelAngle, chart.YAxisLineColor, chart.YAxisLineThickness);

    public static ChartAxisFormatDialogResult CreateResult(
        bool useXAxis,
        double? minimum,
        double? maximum,
        double? majorUnit,
        double? minorUnit,
        bool logScale,
        ChartDataLabelNumberFormat numberFormat,
        bool showMajorGridlines,
        bool showMinorGridlines,
        CellColor? majorGridlineColor,
        CellColor? minorGridlineColor,
        double gridlineThickness,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        bool showLabels,
        CellColor? labelTextColor,
        double labelFontSize,
        double labelAngle,
        CellColor? lineColor,
        double lineThickness) =>
        new(useXAxis, minimum, maximum, majorUnit, minorUnit, logScale, numberFormat, showMajorGridlines,
            showMinorGridlines, majorGridlineColor, minorGridlineColor, gridlineThickness, majorTickStyle,
            minorTickStyle, showLabels, labelTextColor, labelFontSize, labelAngle, lineColor, lineThickness);

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            stack.Children.Add(CreateInlineHelp("Leave bounds blank for Auto."));
            ChartDialogHelpers.AddNumericText(stack, "Minimum (blank for Auto)", _minimumBox, "Blank or Auto keeps the automatic minimum.");
            ChartDialogHelpers.AddNumericText(stack, "Maximum (blank for Auto)", _maximumBox, "Blank or Auto keeps the automatic maximum.");
            ChartDialogHelpers.AddNumericText(stack, "Major unit", _majorUnitBox, "Blank keeps the automatic major unit.");
            ChartDialogHelpers.AddNumericText(stack, "Minor unit", _minorUnitBox, "Blank keeps the automatic minor unit.");
            ChartDialogHelpers.AddCheck(stack, _logBox);
            ChartDialogHelpers.AddCombo(stack, "Number format", _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
            root.Children.Add(CreateGroupBox("Axis Options", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCheck(stack, _majorGridBox);
            ChartDialogHelpers.AddCheck(stack, _minorGridBox);
            ChartDialogHelpers.AddColorText(stack, "Major gridline color", _majorGridColorBox);
            ChartDialogHelpers.AddColorText(stack, "Minor gridline color", _minorGridColorBox);
            ChartDialogHelpers.AddNumericText(stack, "Gridline width", _gridlineThicknessBox, "Enter a gridline width in points.");
            root.Children.Add(CreateGroupBox("Gridlines", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCombo(stack, "Major tick marks", _majorTickBox, Enum.GetValues<ChartAxisTickStyle>());
            ChartDialogHelpers.AddCombo(stack, "Minor tick marks", _minorTickBox, Enum.GetValues<ChartAxisTickStyle>());
            ChartDialogHelpers.AddCheck(stack, _labelsBox);
            ChartDialogHelpers.AddColorText(stack, "Label color", _labelColorBox);
            ChartDialogHelpers.AddNumericText(stack, "Label font size", _labelFontSizeBox, "Enter a font size in points.");
            ChartDialogHelpers.AddNumericText(stack, "Label angle", _labelAngleBox, "Enter label rotation in degrees.");
            ChartDialogHelpers.AddColorText(stack, "Axis line color", _lineColorBox);
            ChartDialogHelpers.AddNumericText(stack, "Axis line width", _lineThicknessBox, "Enter an axis line width in points.");
            root.Children.Add(CreateGroupBox("Tick Marks", stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartAxisFormatDialogResult result)
    {
        _minimumBox.Text = ChartDialogHelpers.FormatNullable(result.Minimum);
        _maximumBox.Text = ChartDialogHelpers.FormatNullable(result.Maximum);
        _majorUnitBox.Text = ChartDialogHelpers.FormatNullable(result.MajorUnit);
        _minorUnitBox.Text = ChartDialogHelpers.FormatNullable(result.MinorUnit);
        _logBox.IsChecked = result.LogScale;
        _numberFormatBox.SelectedItem = result.NumberFormat;
        _majorGridBox.IsChecked = result.ShowMajorGridlines;
        _minorGridBox.IsChecked = result.ShowMinorGridlines;
        _majorGridColorBox.Text = ChartDialogHelpers.FormatColor(result.MajorGridlineColor);
        _minorGridColorBox.Text = ChartDialogHelpers.FormatColor(result.MinorGridlineColor);
        _gridlineThicknessBox.Text = result.GridlineThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _majorTickBox.SelectedItem = result.MajorTickStyle;
        _minorTickBox.SelectedItem = result.MinorTickStyle;
        _labelsBox.IsChecked = result.ShowLabels;
        _labelColorBox.Text = ChartDialogHelpers.FormatColor(result.LabelTextColor);
        _labelFontSizeBox.Text = result.LabelFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _labelAngleBox.Text = result.LabelAngle.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _lineColorBox.Text = ChartDialogHelpers.FormatColor(result.LineColor);
        _lineThicknessBox.Text = result.LineThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Accept()
    {
        Result = CreateResult(
            _useXAxis,
            ChartDialogHelpers.ParseNullableDouble(_minimumBox.Text),
            ChartDialogHelpers.ParseNullableDouble(_maximumBox.Text),
            ChartDialogHelpers.ParseNullableDouble(_majorUnitBox.Text),
            ChartDialogHelpers.ParseNullableDouble(_minorUnitBox.Text),
            _logBox.IsChecked == true,
            ChartDialogHelpers.Selected(_numberFormatBox, ChartDataLabelNumberFormat.General),
            _majorGridBox.IsChecked == true,
            _minorGridBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_majorGridColorBox.Text),
            ChartDialogHelpers.ParseColor(_minorGridColorBox.Text),
            ChartDialogHelpers.ParseDouble(_gridlineThicknessBox.Text, 1),
            ChartDialogHelpers.Selected(_majorTickBox, ChartAxisTickStyle.Outside),
            ChartDialogHelpers.Selected(_minorTickBox, ChartAxisTickStyle.None),
            _labelsBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_labelColorBox.Text),
            ChartDialogHelpers.ParseDouble(_labelFontSizeBox.Text, 11),
            ChartDialogHelpers.ParseDouble(_labelAngleBox.Text, 0),
            ChartDialogHelpers.ParseColor(_lineColorBox.Text),
            ChartDialogHelpers.ParseDouble(_lineThicknessBox.Text, 1));
        DialogResult = true;
    }
}

public sealed record ChartSeriesFormatDialogResult(
    int SeriesIndex,
    CellColor? FillColor,
    CellColor? StrokeColor,
    double? StrokeThickness,
    ChartLineDashStyle? DashStyle,
    ChartMarkerStyle? MarkerStyle,
    double? MarkerSize)
{
    public ChartLayoutOptions ToOptions(IReadOnlyList<ChartSeriesFormat> currentFormats)
    {
        var formats = currentFormats.ToList();
        var replacement = new ChartSeriesFormat(
            SeriesIndex,
            FillColor,
            StrokeColor,
            StrokeThickness,
            DashStyle,
            MarkerStyle,
            MarkerSize);
        var existingIndex = formats.FindIndex(format => format.SeriesIndex == SeriesIndex);
        if (existingIndex >= 0)
            formats[existingIndex] = replacement;
        else
            formats.Add(replacement);
        return new ChartLayoutOptions(SeriesFormats: formats);
    }
}

public sealed class ChartSeriesFormatDialog : Window
{
    private readonly ComboBox _seriesBox = new();
    private readonly ComboBox _dashBox = new();
    private readonly ComboBox _markerBox = new();
    private readonly TextBox _fillBox = new();
    private readonly TextBox _strokeBox = new();
    private readonly TextBox _strokeThicknessBox = new();
    private readonly TextBox _markerSizeBox = new();

    public ChartSeriesFormatDialogResult Result { get; private set; }

    public ChartSeriesFormatDialog(ChartModel chart, int seriesCount)
    {
        Result = FromChart(chart, seriesCount);
        Title = "Format Data Series";
        Width = 380;
        Height = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent(seriesCount);
        Load(Result);
    }

    public static ChartSeriesFormatDialogResult FromChart(ChartModel chart, int seriesCount)
    {
        var seriesIndex = Math.Clamp(chart.SeriesFormats.FirstOrDefault()?.SeriesIndex ?? 0, 0, Math.Max(0, seriesCount - 1));
        var format = chart.SeriesFormats.FirstOrDefault(item => item.SeriesIndex == seriesIndex) ?? new ChartSeriesFormat(seriesIndex);
        return CreateResult(seriesIndex, format.FillColor, format.StrokeColor, format.StrokeThickness, format.DashStyle, format.MarkerStyle, format.MarkerSize);
    }

    public static ChartSeriesFormatDialogResult CreateResult(
        int seriesIndex,
        CellColor? fillColor,
        CellColor? strokeColor,
        double? strokeThickness,
        ChartLineDashStyle? dashStyle,
        ChartMarkerStyle? markerStyle,
        double? markerSize) =>
        new(Math.Max(0, seriesIndex), fillColor, strokeColor, strokeThickness, dashStyle, markerStyle, markerSize);

    private StackPanel CreateContent(int seriesCount)
    {
        var root = ChartDialogHelpers.DialogStack();
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddCombo(stack, "Series", _seriesBox, Enumerable.Range(0, Math.Max(1, seriesCount)).Select(index => $"Series {index + 1}").ToArray());
            stack.Children.Add(CreateInlineHelp("Choose the series to format without changing the chart data."));
            root.Children.Add(CreateGroupBox("Series Options", stack));
        }
        {
            var stack = new StackPanel();
            ChartDialogHelpers.AddColorText(stack, "Fill color", _fillBox);
            ChartDialogHelpers.AddColorText(stack, "Line color", _strokeBox);
            ChartDialogHelpers.AddNumericText(stack, "Line width", _strokeThicknessBox, "Blank keeps the automatic line width.");
            ChartDialogHelpers.AddCombo(stack, "Dash style", _dashBox, Enum.GetValues<ChartLineDashStyle>().Cast<object>().Prepend("(none)").ToArray());
            ChartDialogHelpers.AddCombo(stack, "Marker", _markerBox, Enum.GetValues<ChartMarkerStyle>().Cast<object>().Prepend("(none)").ToArray());
            ChartDialogHelpers.AddNumericText(stack, "Marker size", _markerSizeBox, "Blank keeps the automatic marker size.");
            root.Children.Add(CreateGroupBox("Fill & Line", stack));
        }
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartSeriesFormatDialogResult result)
    {
        _seriesBox.SelectedIndex = Math.Min(result.SeriesIndex, Math.Max(0, _seriesBox.Items.Count - 1));
        _fillBox.Text = ChartDialogHelpers.FormatColor(result.FillColor);
        _strokeBox.Text = ChartDialogHelpers.FormatColor(result.StrokeColor);
        _strokeThicknessBox.Text = ChartDialogHelpers.FormatNullable(result.StrokeThickness);
        _dashBox.SelectedItem = result.DashStyle is null ? "(none)" : result.DashStyle.Value;
        _markerBox.SelectedItem = result.MarkerStyle is null ? "(none)" : result.MarkerStyle.Value;
        _markerSizeBox.Text = ChartDialogHelpers.FormatNullable(result.MarkerSize);
    }

    private void Accept()
    {
        Result = CreateResult(
            _seriesBox.SelectedIndex < 0 ? 0 : _seriesBox.SelectedIndex,
            ChartDialogHelpers.ParseColor(_fillBox.Text),
            ChartDialogHelpers.ParseColor(_strokeBox.Text),
            ChartDialogHelpers.ParseNullableDouble(_strokeThicknessBox.Text),
            _dashBox.SelectedItem is ChartLineDashStyle dash ? dash : null,
            _markerBox.SelectedItem is ChartMarkerStyle marker ? marker : null,
            ChartDialogHelpers.ParseNullableDouble(_markerSizeBox.Text));
        DialogResult = true;
    }
}

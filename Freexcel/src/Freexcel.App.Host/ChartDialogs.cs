using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record ChartTypePickerOption(ChartType Type, string DisplayName, bool IsRecommended = false);

public sealed record ChartTypePickerCategory(string Name, IReadOnlyList<ChartTypePickerOption> Options);

public sealed record ChartTypeGalleryChoice(
    ChartType Type,
    string CategoryName,
    string SubtypeName,
    string PreviewText,
    bool IsRecommended = false);

public static class ChartTypePickerPlanner
{
    private static readonly ChartTypePickerOption[] Options =
    [
        new(ChartType.Column, "Clustered Column", true),
        new(ChartType.StackedColumn, "Stacked Column"),
        new(ChartType.PercentStackedColumn, "100% Stacked Column"),
        new(ChartType.Line, "Line", true),
        new(ChartType.Pie, "Pie", true),
        new(ChartType.Doughnut, "Doughnut"),
        new(ChartType.Bar, "Clustered Bar", true),
        new(ChartType.StackedBar, "Stacked Bar"),
        new(ChartType.PercentStackedBar, "100% Stacked Bar"),
        new(ChartType.Scatter, "Scatter", true),
        new(ChartType.Bubble, "Bubble"),
        new(ChartType.Area, "Area"),
        new(ChartType.Radar, "Radar"),
        new(ChartType.Stock, "Stock")
    ];

    public static IReadOnlyList<ChartTypePickerOption> GetSupportedOptions() =>
        Options.Where(option => ChartTypeSupport.IsRenderable(option.Type)).ToList();

    public static IReadOnlyList<ChartTypePickerOption> GetRecommendedOptions() =>
        new[]
        {
            ChartType.Column,
            ChartType.Line,
            ChartType.Bar,
            ChartType.Pie,
            ChartType.Scatter
        }
        .Select(type => Options.Single(option => option.Type == type))
        .Where(option => option.IsRecommended && ChartTypeSupport.IsRenderable(option.Type))
        .ToList();

    public static IReadOnlyList<ChartTypePickerCategory> GetCategories()
    {
        var supported = GetSupportedOptions();
        return new (string Name, ChartType[] Types)[]
            {
                ("Column", [ChartType.Column, ChartType.StackedColumn, ChartType.PercentStackedColumn]),
                ("Line", [ChartType.Line]),
                ("Pie", [ChartType.Pie, ChartType.Doughnut]),
                ("Bar", [ChartType.Bar, ChartType.StackedBar, ChartType.PercentStackedBar]),
                ("Area", [ChartType.Area]),
                ("X Y (Scatter)", [ChartType.Scatter, ChartType.Bubble]),
                ("Stock", [ChartType.Stock]),
                ("Radar", [ChartType.Radar])
            }
            .Select(category => new ChartTypePickerCategory(
                category.Name,
                category.Types
                    .Select(type => supported.FirstOrDefault(option => option.Type == type))
                    .OfType<ChartTypePickerOption>()
                    .ToList()))
            .Where(category => category.Options.Count > 0)
            .ToList();
    }

    public static IReadOnlyList<ChartTypeGalleryChoice> GetGalleryChoices(string categoryName) =>
        GetCategories()
            .Where(category => category.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(category => category.Options.Select(option => new ChartTypeGalleryChoice(
                option.Type,
                category.Name,
                option.DisplayName,
                $"Preview: {option.DisplayName}",
                option.IsRecommended)))
            .ToList();

    public static IReadOnlyList<ChartTypeGalleryChoice> GetRecommendedGalleryChoices() =>
        GetRecommendedOptions()
            .Select(option => new ChartTypeGalleryChoice(
                option.Type,
                "Recommended Charts",
                option.DisplayName,
                $"Preview: {option.DisplayName}",
                IsRecommended: true))
            .ToList();
}

public sealed record InsertChartDialogResult(ChartType ChartType, bool UseRecommendedLayout);

public sealed class InsertChartDialog : Window
{
    private readonly ListBox _recommendedGallery = new();
    private readonly ListBox _categoryList = new();
    private readonly ListBox _subtypeGallery = new();
    private readonly CheckBox _recommendedBox = new() { Content = "Use recommended layout" };

    public InsertChartDialogResult Result { get; private set; } = CreateRecommendedResult();

    public InsertChartDialog()
    {
        Title = "Insert Chart";
        Width = 660;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = false };
        var tabs = new TabControl { Height = 310, Margin = new Thickness(0, 0, 0, 12) };
        _recommendedGallery.ItemsSource = ChartTypePickerPlanner.GetRecommendedGalleryChoices();
        _recommendedGallery.DisplayMemberPath = nameof(ChartTypeGalleryChoice.SubtypeName);
        _recommendedGallery.SelectedIndex = 0;
        tabs.Items.Add(new TabItem
        {
            Header = "Recommended Charts",
            Content = CreateRecommendedChartsPanel(_recommendedGallery)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "All Charts",
            Content = CreateAllChartsPanel(_categoryList, _subtypeGallery)
        });
        DockPanel.SetDock(tabs, Dock.Top);
        root.Children.Add(tabs);
        _recommendedBox.IsChecked = true;
        _recommendedBox.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(_recommendedBox, Dock.Top);
        root.Children.Add(_recommendedBox);
        var buttons = CreateButtonRow(Accept);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        Content = root;
    }

    public static InsertChartDialogResult CreateResult(ChartType chartType) =>
        new(chartType, UseRecommendedLayout: false);

    public static InsertChartDialogResult CreateRecommendedResult() =>
        new(ChartTypePickerPlanner.GetRecommendedOptions().First().Type, UseRecommendedLayout: true);

    private void Accept()
    {
        var selected = SelectedGalleryChoice(_recommendedGallery, _subtypeGallery)?.Type ?? ChartType.Column;
        Result = new InsertChartDialogResult(selected, _recommendedBox.IsChecked == true);
        DialogResult = true;
    }

    private static ChartTypeGalleryChoice? SelectedGalleryChoice(ListBox recommendedGallery, ListBox subtypeGallery) =>
        subtypeGallery.SelectedItem as ChartTypeGalleryChoice
        ?? recommendedGallery.SelectedItem as ChartTypeGalleryChoice;

    private static Grid CreateRecommendedChartsPanel(ListBox gallery)
    {
        var grid = CreatePickerGrid();
        grid.Children.Add(new TextBlock
        {
            Text = "Choose a chart type",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        gallery.Margin = new Thickness(0, 24, 12, 0);
        AutomationProperties.SetName(gallery, "Chart subtype gallery");
        Grid.SetRow(gallery, 1);
        grid.Children.Add(gallery);
        var preview = CreatePreviewPanel("Preview", "Recommended chart preview");
        Grid.SetColumn(preview, 1);
        Grid.SetRowSpan(preview, 2);
        grid.Children.Add(preview);
        return grid;
    }

    internal static Grid CreateAllChartsPanel(
        ListBox categoryList,
        ListBox subtypeGallery,
        ChartType? selectedType = null)
    {
        var categories = ChartTypePickerPlanner.GetCategories();
        var grid = CreatePickerGrid();
        categoryList.ItemsSource = categories;
        categoryList.DisplayMemberPath = nameof(ChartTypePickerCategory.Name);
        categoryList.Width = 150;
        categoryList.Margin = new Thickness(0, 24, 12, 0);
        AutomationProperties.SetName(categoryList, "Chart categories");
        subtypeGallery.DisplayMemberPath = nameof(ChartTypeGalleryChoice.SubtypeName);
        subtypeGallery.Margin = new Thickness(0, 24, 12, 0);
        AutomationProperties.SetName(subtypeGallery, "Chart subtype gallery");
        categoryList.SelectionChanged += (_, _) =>
        {
            if (categoryList.SelectedItem is not ChartTypePickerCategory category)
                return;

            subtypeGallery.ItemsSource = ChartTypePickerPlanner.GetGalleryChoices(category.Name);
            subtypeGallery.SelectedIndex = 0;
        };

        var selectedCategory = categories.FirstOrDefault(category =>
            selectedType is not null && category.Options.Any(option => option.Type == selectedType.Value))
            ?? categories.FirstOrDefault();
        categoryList.SelectedItem = selectedCategory;
        if (selectedType is not null && subtypeGallery.ItemsSource is IEnumerable<ChartTypeGalleryChoice> choices)
        {
            subtypeGallery.SelectedItem = choices.FirstOrDefault(choice => choice.Type == selectedType.Value)
                ?? choices.FirstOrDefault();
        }

        grid.Children.Add(new TextBlock
        {
            Text = "All Charts",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        Grid.SetRow(categoryList, 1);
        grid.Children.Add(categoryList);
        Grid.SetColumn(subtypeGallery, 1);
        Grid.SetRow(subtypeGallery, 1);
        grid.Children.Add(subtypeGallery);
        var preview = CreatePreviewPanel("Preview", "Chart preview");
        Grid.SetColumn(preview, 2);
        Grid.SetRowSpan(preview, 2);
        grid.Children.Add(preview);
        return grid;
    }

    private static Grid CreatePickerGrid()
    {
        var grid = new Grid { Margin = new Thickness(12), MinHeight = 250 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static Border CreatePreviewPanel(string title, string body) =>
        new()
        {
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 24, 0, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 12)
                    },
                    new TextBlock
                    {
                        Text = body,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };

    internal static StackPanel CreateButtonRow(Action accept) =>
        DialogButtonRowFactory.Create(accept, buttonWidth: 76);
}

public sealed record ChangeChartTypeDialogResult(ChartType ChartType);

public sealed class ChangeChartTypeDialog : Window
{
    private readonly ListBox _categoryList = new();
    private readonly ListBox _subtypeGallery = new();

    public ChartType SelectedChartType { get; private set; }
    public ChangeChartTypeDialogResult Result { get; private set; }

    public ChangeChartTypeDialog(ChartType currentType)
    {
        SelectedChartType = currentType;
        Result = CreateResult(currentType);
        Title = "Change Chart Type";
        Width = 640;
        Height = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = false };
        var heading = new TextBlock
        {
            Text = "Choose a chart type",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var panel = InsertChartDialog.CreateAllChartsPanel(_categoryList, _subtypeGallery, currentType);
        panel.Height = 290;
        DockPanel.SetDock(panel, Dock.Top);
        root.Children.Add(panel);
        var buttons = CreateButtonRow();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        Content = root;
    }

    public static ChangeChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);

    private StackPanel CreateButtonRow() => InsertChartDialog.CreateButtonRow(() =>
    {
        if (_subtypeGallery.SelectedItem is ChartTypeGalleryChoice option)
            SelectedChartType = option.Type;
        Result = CreateResult(SelectedChartType);
        DialogResult = true;
    });
}

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
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(box);
    }
}

public sealed record ChartStyleDialogResult(int? ChartStyleId);

public sealed class ChartStyleDialog : Window
{
    private readonly ComboBox _styleBox = new();

    public ChartStyleDialogResult Result { get; private set; }

    public ChartStyleDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Chart Styles";
        Width = 340;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var options = GetStyleOptions();
        _styleBox.ItemsSource = options;
        _styleBox.DisplayMemberPath = nameof(ChartStyleOption.DisplayName);
        _styleBox.SelectedItem = options.FirstOrDefault(option => option.StyleId == Result.ChartStyleId) ?? options[0];
        _styleBox.Margin = new Thickness(0, 0, 0, 16);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Style", Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(_styleBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static ChartStyleDialogResult FromChart(ChartModel chart) =>
        new(NormalizeStyleId(chart.ChartStyleId));

    public static ChartStyleDialogResult CreateResult(int? chartStyleId) =>
        new(NormalizeStyleId(chartStyleId));

    public static IReadOnlyList<ChartStyleOption> GetStyleOptions() =>
        new[] { new ChartStyleOption(null, "Automatic") }
            .Concat(Enumerable.Range(1, 48).Select(index => new ChartStyleOption(index, $"Style {index}")))
            .ToList();

    private void Accept()
    {
        Result = _styleBox.SelectedItem is ChartStyleOption option
            ? CreateResult(option.StyleId)
            : CreateResult(null);
        DialogResult = true;
    }

    private static int? NormalizeStyleId(int? value)
    {
        if (value is null)
            return null;

        return Math.Clamp(value.Value, 1, 48);
    }
}

public sealed record ChartStyleOption(int? StyleId, string DisplayName);

public enum MoveChartTargetKind
{
    ObjectInSheet,
    NewChartSheet
}

public sealed record MoveChartDialogResult(MoveChartTargetKind TargetKind, string TargetName);

public sealed class MoveChartDialog : Window
{
    private readonly RadioButton _objectInSheet = new() { Content = "Object in sheet", IsChecked = true };
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
        stack.Children.Add(new RadioButton { Content = "New chart sheet", Margin = new Thickness(0, 4, 0, 8) });
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
    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _firstColumnCategoriesBox = new() { Content = "First column contains category labels" };
    private readonly CheckBox _switchRowColumnBox = new() { Content = "Switch Row/Column" };
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
        stack.Children.Add(new TextBlock { Text = "Chart data range", Margin = new Thickness(0, 0, 0, 4) });
        _rangeBox.Text = Result.SourceRangeText;
        stack.Children.Add(CreateReferenceEditor(_rangeBox, "Select chart data range"));
        _switchRowColumnBox.Margin = new Thickness(0, 10, 0, 8);
        stack.Children.Add(_switchRowColumnBox);
        stack.Children.Add(CreateSourceListPanel("Legend Entries (Series)", _seriesList));
        stack.Children.Add(CreateSourceListPanel("Horizontal (Category) Axis Labels", _axisLabelsList));
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

    private static Grid CreateSourceListPanel(string title, ListBox list)
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 4) });
        Grid.SetRow(list, 1);
        panel.Children.Add(list);

        var buttons = AddEditRemoveButtons();
        Grid.SetColumn(buttons, 1);
        Grid.SetRowSpan(buttons, 2);
        panel.Children.Add(buttons);
        return panel;
    }

    private static StackPanel AddEditRemoveButtons()
    {
        var stack = new StackPanel { Margin = new Thickness(8, 20, 0, 0) };
        stack.Children.Add(new Button { Content = "Add", Width = 74, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new Button { Content = "Edit", Width = 74, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new Button { Content = "Remove", Width = 74 });
        return stack;
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
    private readonly CheckBox _showLegendBox = new() { Content = "Show legend" };
    private readonly ComboBox _legendPositionBox = new();
    private readonly CheckBox _legendOverlayBox = new() { Content = "Overlay legend on chart" };
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
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddColorText(stack, "Chart area fill color", _chartAreaFillBox);
        ChartDialogHelpers.AddColorText(stack, "Plot area fill color", _plotAreaFillBox);
        ChartDialogHelpers.AddColorText(stack, "Plot area border color", _plotAreaBorderBox);
        ChartDialogHelpers.AddText(stack, "Plot area border width", _plotAreaBorderThicknessBox);
        ChartDialogHelpers.AddCheck(stack, _showLegendBox);
        ChartDialogHelpers.AddCombo(stack, "Legend position", _legendPositionBox, Enum.GetValues<ChartLegendPosition>());
        ChartDialogHelpers.AddCheck(stack, _legendOverlayBox);
        ChartDialogHelpers.AddColorText(stack, "Legend text color", _legendTextBox);
        ChartDialogHelpers.AddColorText(stack, "Legend fill color", _legendFillBox);
        ChartDialogHelpers.AddColorText(stack, "Legend border color", _legendBorderBox);
        ChartDialogHelpers.AddText(stack, "Legend border width", _legendBorderThicknessBox);
        ChartDialogHelpers.AddText(stack, "Legend font size", _legendFontSizeBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
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
    private readonly CheckBox _showBox = new() { Content = "Show data labels" };
    private readonly CheckBox _categoryBox = new() { Content = "Category name" };
    private readonly CheckBox _seriesBox = new() { Content = "Series name" };
    private readonly CheckBox _percentageBox = new() { Content = "Percentage" };
    private readonly CheckBox _calloutsBox = new() { Content = "Data label callouts" };
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
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, "Position", _positionBox, Enum.GetValues<ChartDataLabelPosition>());
        ChartDialogHelpers.AddCheck(stack, _categoryBox);
        ChartDialogHelpers.AddCheck(stack, _seriesBox);
        ChartDialogHelpers.AddCheck(stack, _percentageBox);
        ChartDialogHelpers.AddCombo(stack, "Separator", _separatorBox, Enum.GetValues<ChartDataLabelSeparator>());
        ChartDialogHelpers.AddCombo(stack, "Number format", _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
        ChartDialogHelpers.AddCheck(stack, _calloutsBox);
        ChartDialogHelpers.AddColorText(stack, "Fill color", _fillBox);
        ChartDialogHelpers.AddColorText(stack, "Border color", _borderBox);
        ChartDialogHelpers.AddColorText(stack, "Text color", _textBox);
        ChartDialogHelpers.AddText(stack, "Border thickness", _borderThicknessBox);
        ChartDialogHelpers.AddText(stack, "Font size", _fontSizeBox);
        ChartDialogHelpers.AddText(stack, "Text angle", _angleBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
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
    private readonly CheckBox _showBox = new() { Content = "Show trendline" };
    private readonly CheckBox _equationBox = new() { Content = "Display equation" };
    private readonly CheckBox _rSquaredBox = new() { Content = "Display R-squared value" };
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
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, "Type", _typeBox, Enum.GetValues<ChartTrendlineType>());
        ChartDialogHelpers.AddText(stack, "Moving average period", _periodBox);
        ChartDialogHelpers.AddText(stack, "Polynomial order", _orderBox);
        ChartDialogHelpers.AddCheck(stack, _equationBox);
        ChartDialogHelpers.AddCheck(stack, _rSquaredBox);
        ChartDialogHelpers.AddColorText(stack, "Line color", _colorBox);
        ChartDialogHelpers.AddText(stack, "Line width", _thicknessBox);
        ChartDialogHelpers.AddCombo(stack, "Dash style", _dashBox, Enum.GetValues<ChartLineDashStyle>());
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
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
    private readonly CheckBox _showBox = new() { Content = "Show error bars" };
    private readonly CheckBox _endCapsBox = new() { Content = "End caps" };
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
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, "Type", _kindBox, Enum.GetValues<ChartErrorBarKind>());
        ChartDialogHelpers.AddCombo(stack, "Direction", _directionBox, Enum.GetValues<ChartErrorBarDirection>());
        ChartDialogHelpers.AddText(stack, "Value", _valueBox);
        ChartDialogHelpers.AddCheck(stack, _endCapsBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
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
    private readonly CheckBox _logBox = new() { Content = "Logarithmic scale" };
    private readonly ComboBox _numberFormatBox = new();
    private readonly CheckBox _majorGridBox = new() { Content = "Major gridlines" };
    private readonly CheckBox _minorGridBox = new() { Content = "Minor gridlines" };
    private readonly TextBox _majorGridColorBox = new();
    private readonly TextBox _minorGridColorBox = new();
    private readonly TextBox _gridlineThicknessBox = new();
    private readonly ComboBox _majorTickBox = new();
    private readonly ComboBox _minorTickBox = new();
    private readonly CheckBox _labelsBox = new() { Content = "Show labels" };
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
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddText(stack, "Minimum (blank for Auto)", _minimumBox);
        ChartDialogHelpers.AddText(stack, "Maximum (blank for Auto)", _maximumBox);
        ChartDialogHelpers.AddText(stack, "Major unit", _majorUnitBox);
        ChartDialogHelpers.AddText(stack, "Minor unit", _minorUnitBox);
        ChartDialogHelpers.AddCheck(stack, _logBox);
        ChartDialogHelpers.AddCombo(stack, "Number format", _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
        ChartDialogHelpers.AddCheck(stack, _majorGridBox);
        ChartDialogHelpers.AddCheck(stack, _minorGridBox);
        ChartDialogHelpers.AddColorText(stack, "Major gridline color", _majorGridColorBox);
        ChartDialogHelpers.AddColorText(stack, "Minor gridline color", _minorGridColorBox);
        ChartDialogHelpers.AddText(stack, "Gridline width", _gridlineThicknessBox);
        ChartDialogHelpers.AddCombo(stack, "Major tick marks", _majorTickBox, Enum.GetValues<ChartAxisTickStyle>());
        ChartDialogHelpers.AddCombo(stack, "Minor tick marks", _minorTickBox, Enum.GetValues<ChartAxisTickStyle>());
        ChartDialogHelpers.AddCheck(stack, _labelsBox);
        ChartDialogHelpers.AddColorText(stack, "Label color", _labelColorBox);
        ChartDialogHelpers.AddText(stack, "Label font size", _labelFontSizeBox);
        ChartDialogHelpers.AddText(stack, "Label angle", _labelAngleBox);
        ChartDialogHelpers.AddColorText(stack, "Axis line color", _lineColorBox);
        ChartDialogHelpers.AddText(stack, "Axis line width", _lineThicknessBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
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
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddCombo(stack, "Series", _seriesBox, Enumerable.Range(0, Math.Max(1, seriesCount)).Select(index => $"Series {index + 1}").ToArray());
        ChartDialogHelpers.AddColorText(stack, "Fill color", _fillBox);
        ChartDialogHelpers.AddColorText(stack, "Line color", _strokeBox);
        ChartDialogHelpers.AddText(stack, "Line width", _strokeThicknessBox);
        ChartDialogHelpers.AddCombo(stack, "Dash style", _dashBox, Enum.GetValues<ChartLineDashStyle>().Cast<object>().Prepend("(none)").ToArray());
        ChartDialogHelpers.AddCombo(stack, "Marker", _markerBox, Enum.GetValues<ChartMarkerStyle>().Cast<object>().Prepend("(none)").ToArray());
        ChartDialogHelpers.AddText(stack, "Marker size", _markerSizeBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
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

internal static class ChartDialogHelpers
{
public static StackPanel DialogStack() => new() { Margin = new Thickness(16) };

public static void AddCheck(Panel stack, CheckBox checkBox)
{
    checkBox.Margin = new Thickness(0, 0, 0, 6);
    stack.Children.Add(checkBox);
}

public static void AddCombo<T>(Panel stack, string label, ComboBox comboBox, IEnumerable<T> items)
{
    stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) });
    comboBox.ItemsSource = items;
    comboBox.Margin = new Thickness(0, 0, 0, 8);
    stack.Children.Add(comboBox);
}

public static void AddText(Panel stack, string label, TextBox textBox)
{
    stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) });
    textBox.Margin = new Thickness(0, 0, 0, 8);
    stack.Children.Add(textBox);
}

public static void AddColorText(Panel stack, string label, TextBox textBox)
{
    stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) });
    var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
    var pickerButton = new Button
    {
        Content = "...",
        Width = 28,
        Margin = new Thickness(0, 0, 6, 0),
        Tag = textBox
    };
    AutomationProperties.SetName(pickerButton, $"Pick {label}");
    pickerButton.Click += ColorPickerButton_Click;
    panel.Children.Add(pickerButton);
    panel.Children.Add(textBox);
    stack.Children.Add(panel);
}

private static void ColorPickerButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement { Tag: TextBox textBox })
        return;

    var initialColor = ParseColor(textBox.Text);
    var dialog = new ColorPickerDialog(initialColor, allowNoColor: true)
    {
        Owner = Window.GetWindow(textBox)
    };
    if (dialog.ShowDialog() != true)
        return;

    textBox.Text = dialog.SelectedColor is { } color
        ? FormatColor(color)
        : "none";
}

public static T Selected<T>(ComboBox comboBox, T fallback) =>
    comboBox.SelectedItem is T value ? value : fallback;

public static CellColor? ParseColor(string text) =>
    ColorInputParser.TryParseOptionalHexColor(text, out var color) ? color : null;

public static string FormatColor(CellColor? color) =>
    color is null ? "none" : ColorInputParser.FormatHexColor(color.Value);

public static double ParseDouble(string text, double fallback) =>
    double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
        ? value
        : fallback;

public static double? ParseNullableDouble(string text) =>
    string.IsNullOrWhiteSpace(text) || text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)
        ? null
        : ChartDialogHelpers.ParseDouble(text, 0);

public static string FormatNullable(double? value) =>
    value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
}

using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ChartTypePickerOption(ChartType Type, string DisplayName, bool IsRecommended = false);

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
}

public sealed record InsertChartDialogResult(ChartType ChartType, bool UseRecommendedLayout);

public sealed class InsertChartDialog : Window
{
    private readonly ComboBox _chartTypeBox = new();
    private readonly CheckBox _recommendedBox = new() { Content = "Use recommended layout" };

    public InsertChartDialogResult Result { get; private set; } = CreateRecommendedResult();

    public InsertChartDialog()
    {
        Title = "Insert Chart";
        Width = 360;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Chart type", Margin = new Thickness(0, 0, 0, 4) });
        _chartTypeBox.ItemsSource = ChartTypePickerPlanner.GetSupportedOptions();
        _chartTypeBox.DisplayMemberPath = nameof(ChartTypePickerOption.DisplayName);
        _chartTypeBox.SelectedIndex = 0;
        _chartTypeBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_chartTypeBox);

        _recommendedBox.IsChecked = true;
        _recommendedBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_recommendedBox);
        stack.Children.Add(CreateButtonRow(Accept));
        Content = stack;
    }

    public static InsertChartDialogResult CreateResult(ChartType chartType) =>
        new(chartType, UseRecommendedLayout: false);

    public static InsertChartDialogResult CreateRecommendedResult() =>
        new(ChartTypePickerPlanner.GetRecommendedOptions().First().Type, UseRecommendedLayout: true);

    private void Accept()
    {
        var selected = _chartTypeBox.SelectedItem is ChartTypePickerOption option
            ? option.Type
            : ChartType.Column;
        Result = new InsertChartDialogResult(selected, _recommendedBox.IsChecked == true);
        DialogResult = true;
    }

    internal static StackPanel CreateButtonRow(Action accept)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = global::System.Windows.HorizontalAlignment.Right
        };
        var ok = new Button { Content = "OK", Width = 76, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        row.Children.Add(new Button { Content = "Cancel", Width = 76, IsCancel = true });
        return row;
    }
}

public sealed record ChangeChartTypeDialogResult(ChartType ChartType);

public sealed class ChangeChartTypeDialog : Window
{
    private readonly ComboBox _chartTypeBox = new();

    public ChartType SelectedChartType { get; private set; }
    public ChangeChartTypeDialogResult Result { get; private set; }

    public ChangeChartTypeDialog(ChartType currentType)
    {
        SelectedChartType = currentType;
        Result = CreateResult(currentType);
        Title = "Change Chart Type";
        Width = 340;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        _chartTypeBox.ItemsSource = ChartTypePickerPlanner.GetSupportedOptions();
        _chartTypeBox.DisplayMemberPath = nameof(ChartTypePickerOption.DisplayName);
        _chartTypeBox.SelectedItem = ChartTypePickerPlanner.GetSupportedOptions()
            .FirstOrDefault(option => option.Type == currentType);
        stack.Children.Add(_chartTypeBox);
        stack.Children.Add(CreateButtonRow());
        Content = stack;
    }

    public static ChangeChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);

    private StackPanel CreateButtonRow() => InsertChartDialog.CreateButtonRow(() =>
    {
        if (_chartTypeBox.SelectedItem is ChartTypePickerOption option)
            SelectedChartType = option.Type;
        Result = CreateResult(SelectedChartType);
        DialogResult = true;
    });
}

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

public sealed record SelectDataSourceDialogResult(string SourceRangeText, bool SwitchRowColumn);

public sealed class SelectDataSourceDialog : Window
{
    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _switchBox = new() { Content = "Switch row/column" };

    public SelectDataSourceDialogResult Result { get; private set; }

    public SelectDataSourceDialog(string sourceRangeText, bool switchRowColumn = false)
    {
        Result = CreateResult(sourceRangeText, switchRowColumn);
        Title = "Select Data Source";
        Width = 420;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Chart data range", Margin = new Thickness(0, 0, 0, 4) });
        _rangeBox.Text = Result.SourceRangeText;
        stack.Children.Add(_rangeBox);
        _switchBox.IsChecked = switchRowColumn;
        _switchBox.Margin = new Thickness(0, 10, 0, 16);
        stack.Children.Add(_switchBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            Result = CreateResult(_rangeBox.Text, _switchBox.IsChecked == true);
            DialogResult = true;
        }));
        Content = stack;
    }

    public static SelectDataSourceDialogResult CreateResult(string sourceRangeText, bool switchRowColumn) =>
        new(sourceRangeText.Trim(), switchRowColumn);
}

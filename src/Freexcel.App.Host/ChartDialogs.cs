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
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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

    private void FocusInitialKeyboardTarget()
    {
        _styleGallery.Focus();
        Keyboard.Focus(_styleGallery);
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
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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

    private void FocusInitialKeyboardTarget()
    {
        _objectInSheet.Focus();
        Keyboard.Focus(_objectInSheet);
    }

    private static string RequireTargetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Target name is required.", nameof(name));
        return name.Trim();
    }
}


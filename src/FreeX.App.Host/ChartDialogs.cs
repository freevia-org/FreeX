using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;
using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed class ChartTitlesDialog : Window
{
    private readonly TextBox _chartTitleBox = new();
    private readonly TextBox _xAxisTitleBox = new();
    private readonly TextBox _yAxisTitleBox = new();

    public ChartTitlesInput Result { get; private set; }

    public ChartTitlesDialog(string? chartTitle, string? xAxisTitle, string? yAxisTitle)
    {
        Result = ChartTitlesPlanner.Normalize(new ChartTitlesInput(
            chartTitle ?? string.Empty,
            xAxisTitle ?? string.Empty,
            yAxisTitle ?? string.Empty));
        Title = UiText.Get("ChartTitles_Title");
        Width = 380;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _chartTitleBox.Text = chartTitle ?? "";
        AutomationProperties.SetName(_chartTitleBox, UiText.Get("ChartTitles_ChartTitleAutomationName"));
        _xAxisTitleBox.Text = xAxisTitle ?? "";
        AutomationProperties.SetName(_xAxisTitleBox, UiText.Get("ChartTitles_XAxisTitleAutomationName"));
        _yAxisTitleBox.Text = yAxisTitle ?? "";
        AutomationProperties.SetName(_yAxisTitleBox, UiText.Get("ChartTitles_YAxisTitleAutomationName"));

        var stack = new StackPanel { Margin = new Thickness(16) };
        AddInput(stack, UiText.Get("ChartTitles_ChartTitleLabel"), _chartTitleBox);
        AddInput(stack, UiText.Get("ChartTitles_XAxisTitleLabel"), _xAxisTitleBox);
        AddInput(stack, UiText.Get("ChartTitles_YAxisTitleLabel"), _yAxisTitleBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            Result = ChartTitlesPlanner.Normalize(new ChartTitlesInput(
                _chartTitleBox.Text,
                _xAxisTitleBox.Text,
                _yAxisTitleBox.Text));
            DialogResult = true;
        }));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

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

public sealed class ChartStyleDialog : Window
{
    private readonly ListBox _styleGallery = new();

    public ChartStyleInput Result { get; private set; }

    public ChartStyleDialog(ChartModel chart)
    {
        Result = ChartStylePlanner.Read(chart);
        Title = UiText.Get("ChartStyle_Title");
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
        _styleGallery.SelectedIndex = ChartStylePlanner.FindStyleOptionIndex(Result.StyleId);
        _styleGallery.Margin = new Thickness(0, 0, 0, 16);
        _styleGallery.Height = 230;
        AutomationProperties.SetName(_styleGallery, UiText.Get("ChartStyle_GalleryAutomationName"));

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = UiText.Get("ChartStyle_StyleLabel"), Target = _styleGallery, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(_styleGallery);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static IReadOnlyList<ChartStyleOption> GetStyleOptions() =>
        ChartStylePlanner.GetStyleOptions()
            .Select(CreateStyleOption)
            .ToList();

    private void Accept()
    {
        Result = _styleGallery.SelectedItem is ChartStyleOption option
            ? ChartStylePlanner.CreateResult(option.StyleId)
            : ChartStylePlanner.CreateResult(null);
        DialogResult = true;
    }

    private static ChartStyleOption CreateStyleOption(ChartStyleGalleryOptionDescriptor descriptor)
    {
        var displayName = descriptor.ResourceValue is { } displayValue
            ? UiText.Format(descriptor.DisplayNameResourceKey, displayValue)
            : UiText.Get(descriptor.DisplayNameResourceKey);
        var previewLabel = descriptor.ResourceValue is { } previewValue
            ? UiText.Format(descriptor.PreviewLabelResourceKey, previewValue)
            : UiText.Get(descriptor.PreviewLabelResourceKey);
        return new ChartStyleOption(descriptor.StyleId, displayName, previewLabel);
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
}

public sealed record ChartStyleOption(int? StyleId, string DisplayName, string PreviewLabel);

public sealed class MoveChartDialog : Window
{
    private readonly RadioButton _objectInSheet = new() { Content = MoveTargetLabel(ChartMoveTargetKind.ObjectInSheet), IsChecked = true };
    private readonly RadioButton _newChartSheet = new() { Content = MoveTargetLabel(ChartMoveTargetKind.NewSheet), Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBox _targetBox = new();

    public ChartMoveInput Result { get; private set; }

    public MoveChartDialog(string currentSheetName)
    {
        Result = CreateObjectResult(currentSheetName);
        Title = UiText.Get("MoveChart_Title");
        Width = 340;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        _targetBox.Text = currentSheetName;
        var targetField = ChartMovePlanner.GetTargetNameField();
        AutomationProperties.SetName(_targetBox, UiText.Get(targetField.AutomationNameResourceKey!));
        AutomationProperties.SetAutomationId(_targetBox, targetField.AutomationId);
        AutomationProperties.SetHelpText(_targetBox, UiText.Get(targetField.HelpResourceKey!));
        ApplyTargetAutomation(_objectInSheet, ChartMoveTargetKind.ObjectInSheet);
        ApplyTargetAutomation(_newChartSheet, ChartMoveTargetKind.NewSheet);
        stack.Children.Add(_objectInSheet);
        stack.Children.Add(_newChartSheet);
        stack.Children.Add(new Label { Content = UiText.Get(targetField.LabelResourceKey), Target = _targetBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(_targetBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChartMoveInput CreateObjectResult(string? sheetName) =>
        CreateResult(ChartMoveTargetKind.ObjectInSheet, sheetName);

    public static ChartMoveInput CreateNewSheetResult(string? sheetName) =>
        CreateResult(ChartMoveTargetKind.NewSheet, sheetName);

    private void Accept()
    {
        try
        {
            Result = _objectInSheet.IsChecked == true
                ? CreateObjectResult(_targetBox.Text)
                : CreateNewSheetResult(_targetBox.Text);
        }
        catch (ArgumentException ex)
        {
            DialogMessageHelper.ShowWarning(this, ex.Message, Title);
            FocusInvalidTargetName();
            return;
        }

        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _objectInSheet.Focus();
        Keyboard.Focus(_objectInSheet);
    }

    private void FocusInvalidTargetName()
    {
        _targetBox.Focus();
        _targetBox.SelectAll();
        Keyboard.Focus(_targetBox);
    }

    private static ChartMoveInput CreateResult(ChartMoveTargetKind kind, string? name)
    {
        var plan = ChartMovePlanner.Plan(new ChartMoveInput(kind, name ?? string.Empty), _ => true);
        if (!plan.IsValid)
            throw new ArgumentException(UiText.Get("MoveChart_TargetNameRequiredMessage"), nameof(name));

        return new ChartMoveInput(plan.TargetKind, plan.TargetName);
    }

    private static ChartMoveDialogTargetDescriptor MoveTargetDescriptor(ChartMoveTargetKind kind) =>
        ChartMovePlanner.GetTargetChoices().Single(choice => choice.TargetKind == kind);

    private static string MoveTargetLabel(ChartMoveTargetKind kind) =>
        UiText.Get(MoveTargetDescriptor(kind).LabelResourceKey);

    private static void ApplyTargetAutomation(RadioButton radio, ChartMoveTargetKind kind)
    {
        var descriptor = MoveTargetDescriptor(kind);
        radio.GroupName = ChartMovePlanner.TargetGroupName;
        AutomationProperties.SetAutomationId(radio, descriptor.AutomationId);
    }
}


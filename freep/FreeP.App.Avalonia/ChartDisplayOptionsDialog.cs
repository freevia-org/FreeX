using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartDisplayOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartDisplayOptionsPlanner _planner;
    private readonly TextBox _titleBox;
    private readonly ComboBox _legendCombo;
    private readonly CheckBox _valueLabelsCheck;
    private readonly ComboBox _labelPositionCombo;
    private readonly CheckBox _categoryGridlinesCheck;
    private readonly CheckBox _valueGridlinesCheck;

    internal ChartDisplayOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartDisplayOptionsPlanner.FromChart(chart);
        var surface = ChartDisplayOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDisplayOptionsPlanner.DefaultDialogWidth;
        Height = ChartDisplayOptionsPlanner.DefaultDialogHeight;
        MinWidth = 360;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _titleBox = new TextBox { Text = _planner.Title, MinWidth = 230 };
        _legendCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LegendOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindLegendIndex(_planner.Legend),
            MinWidth = 150,
        };
        _valueLabelsCheck = new CheckBox
        {
            Content = surface.ValueLabelsLabel,
            IsChecked = _planner.ShowValueLabels,
        };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LabelPositionOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindLabelPositionIndex(_planner.LabelPosition),
            MinWidth = 150,
        };
        _categoryGridlinesCheck = new CheckBox
        {
            Content = surface.CategoryGridlinesLabel,
            IsChecked = _planner.CategoryGridlines,
        };
        _valueGridlinesCheck = new CheckBox
        {
            Content = surface.ValueGridlinesLabel,
            IsChecked = _planner.ValueGridlines,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                MakeButton(surface.OkLabel, true, OnOk),
                MakeButton(surface.CancelLabel, false, () => Close(false)),
            },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                MakeRow(surface.ChartTitleLabel, _titleBox),
                MakeRow(surface.LegendLabel, _legendCombo),
                MakeRow(surface.LabelPositionLabel, _labelPositionCombo),
                _valueLabelsCheck,
                _categoryGridlinesCheck,
                _valueGridlinesCheck,
                buttons,
            },
        };
    }

    internal ChartDisplayOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(
        string title,
        LegendPosition? legend,
        bool showValueLabels,
        DataLabelPosition labelPosition,
        bool categoryGridlines,
        bool valueGridlines)
    {
        _titleBox.Text = title;
        _legendCombo.SelectedIndex = FindLegendIndex(legend);
        _valueLabelsCheck.IsChecked = showValueLabels;
        _labelPositionCombo.SelectedIndex = FindLabelPositionIndex(labelPosition);
        _categoryGridlinesCheck.IsChecked = categoryGridlines;
        _valueGridlinesCheck.IsChecked = valueGridlines;
    }

    private void OnOk()
    {
        _editor.ApplyChartDisplayOptions(BuildCommitPlanForTests());
        Close(true);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetTitle(_titleBox.Text);
        var legendIndex = _legendCombo.SelectedIndex;
        _planner.SetLegend(legendIndex >= 0 && legendIndex < ChartDisplayOptionsPlanner.LegendOptions.Count
            ? ChartDisplayOptionsPlanner.LegendOptions[legendIndex].Value
            : null);
        _planner.SetShowValueLabels(_valueLabelsCheck.IsChecked == true);
        var labelIndex = _labelPositionCombo.SelectedIndex;
        if (labelIndex >= 0 && labelIndex < ChartDisplayOptionsPlanner.LabelPositionOptions.Count)
            _planner.SetLabelPosition(ChartDisplayOptionsPlanner.LabelPositionOptions[labelIndex].Value);
        _planner.SetCategoryGridlines(_categoryGridlinesCheck.IsChecked == true);
        _planner.SetValueGridlines(_valueGridlinesCheck.IsChecked == true);
    }

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150, *"),
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }

    private static int FindLegendIndex(LegendPosition? position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.LegendOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.LabelPositionOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);
}

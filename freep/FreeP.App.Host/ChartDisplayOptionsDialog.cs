using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small PowerPoint-style chart display/options dialog.</summary>
public sealed class ChartDisplayOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartDisplayOptionsPlanner _planner;
    private readonly TextBox _titleBox;
    private readonly ComboBox _legendCombo;
    private readonly CheckBox _valueLabelsCheck;
    private readonly ComboBox _labelPositionCombo;
    private readonly CheckBox _categoryGridlinesCheck;
    private readonly CheckBox _valueGridlinesCheck;

    public ChartDisplayOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");

        _planner = ChartDisplayOptionsPlanner.FromChart(chart);
        var surface = ChartDisplayOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDisplayOptionsPlanner.DefaultDialogWidth;
        Height = ChartDisplayOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _titleBox = new TextBox { Text = _planner.Title, MinWidth = 240 };
        _legendCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LegendOptions,
            DisplayMemberPath = nameof(ChartDisplayLegendOption.Label),
            MinWidth = 160,
            SelectedIndex = FindLegendIndex(_planner.Legend),
        };
        _valueLabelsCheck = new CheckBox
        {
            Content = surface.ValueLabelsLabel,
            IsChecked = _planner.ShowValueLabels,
        };
        _labelPositionCombo = new ComboBox
        {
            ItemsSource = ChartDisplayOptionsPlanner.LabelPositionOptions,
            DisplayMemberPath = nameof(ChartDisplayLabelPositionOption.Label),
            MinWidth = 160,
            SelectedIndex = FindLabelPositionIndex(_planner.LabelPosition),
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
            Margin = new Thickness(8, 14, 8, 8),
        };
        var ok = new Button { Content = surface.OkLabel, IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        var cancel = new Button { Content = surface.CancelLabel, IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(MakeRow(surface.ChartTitleLabel, _titleBox));
        content.Children.Add(MakeRow(surface.LegendLabel, _legendCombo));
        content.Children.Add(MakeRow(surface.LabelPositionLabel, _labelPositionCombo));
        content.Children.Add(_valueLabelsCheck);
        content.Children.Add(_categoryGridlinesCheck);
        content.Children.Add(_valueGridlinesCheck);
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartDisplayOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    private void OnOk()
    {
        _editor.ApplyChartDisplayOptions(BuildCommitPlanForTests());
        DialogResult = true;
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetTitle(_titleBox.Text);
        _planner.SetLegend(_legendCombo.SelectedItem is ChartDisplayLegendOption legend ? legend.Value : null);
        _planner.SetShowValueLabels(_valueLabelsCheck.IsChecked == true);
        if (_labelPositionCombo.SelectedItem is ChartDisplayLabelPositionOption position)
            _planner.SetLabelPosition(position.Value);
        _planner.SetCategoryGridlines(_categoryGridlinesCheck.IsChecked == true);
        _planner.SetValueGridlines(_valueGridlinesCheck.IsChecked == true);
    }

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        row.Children.Add(new Label { Content = label, Width = 150, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    private static int FindLegendIndex(LegendPosition? position) =>
        ChartDisplayOptionsPlanner.LegendOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index;

    private static int FindLabelPositionIndex(DataLabelPosition position) =>
        Math.Max(0, ChartDisplayOptionsPlanner.LabelPositionOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == position).index);
}

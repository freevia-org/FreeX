using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style Scatter/Radar plot-style dialog.</summary>
public sealed class ChartPlotStyleOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartPlotStyleOptionsPlanner _planner;
    private readonly ComboBox _scatterCombo;
    private readonly ComboBox _radarCombo;

    public ChartPlotStyleOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        if (chart.ChartType is not (ChartType.Scatter or ChartType.Radar))
            throw new InvalidOperationException("Select a Scatter or Radar chart before editing plot style.");

        _planner = ChartPlotStyleOptionsPlanner.FromChart(chart);
        var surface = ChartPlotStyleOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = ChartPlotStyleOptionsPlanner.DefaultDialogWidth;
        Height = ChartPlotStyleOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _scatterCombo = new ComboBox
        {
            ItemsSource = ChartPlotStyleOptionsPlanner.ScatterStyleOptions,
            DisplayMemberPath = nameof(ChartScatterStyleOption.Label),
            SelectedIndex = FindScatterIndex(_planner.ScatterStyle),
            IsEnabled = chart.ChartType == ChartType.Scatter,
            MinWidth = 190,
        };
        _radarCombo = new ComboBox
        {
            ItemsSource = ChartPlotStyleOptionsPlanner.RadarStyleOptions,
            DisplayMemberPath = nameof(ChartRadarStyleOption.Label),
            SelectedIndex = FindRadarIndex(_planner.RadarStyle),
            IsEnabled = chart.ChartType == ChartType.Radar,
            MinWidth = 190,
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
        content.Children.Add(MakeRow(surface.ScatterStyleLabel, _scatterCombo));
        content.Children.Add(MakeRow(surface.RadarStyleLabel, _radarCombo));
        content.Children.Add(new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartPlotStyleOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(ScatterStyle scatterStyle, RadarStyle radarStyle)
    {
        _scatterCombo.SelectedIndex = FindScatterIndex(scatterStyle);
        _radarCombo.SelectedIndex = FindRadarIndex(radarStyle);
    }

    private void OnOk()
    {
        _editor.ApplyChartPlotStyleOptions(BuildCommitPlanForTests());
        DialogResult = true;
    }

    private void UpdatePlannerFromControls()
    {
        if (_scatterCombo.SelectedItem is ChartScatterStyleOption scatter)
            _planner.SetScatterStyle(scatter.Value);
        if (_radarCombo.SelectedItem is ChartRadarStyleOption radar)
            _planner.SetRadarStyle(radar.Value);
    }

    private static int FindScatterIndex(ScatterStyle value) =>
        Math.Max(0, ChartPlotStyleOptionsPlanner.ScatterStyleOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static int FindRadarIndex(RadarStyle value) =>
        Math.Max(0, ChartPlotStyleOptionsPlanner.RadarStyleOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new Label { Content = label, Width = 190, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}

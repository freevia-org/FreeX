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

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.ScatterStyleLabel, _scatterCombo, 190));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.RadarStyleLabel, _radarCombo, 190));
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
        _planner.SetScatterStyle(ChartDialogOptionProjection.ValueAtOrDefault(
            ChartPlotStyleOptionsPlanner.ScatterStyleOptions,
            _scatterCombo.SelectedIndex,
            option => option.Value,
            _planner.ScatterStyle));
        _planner.SetRadarStyle(ChartDialogOptionProjection.ValueAtOrDefault(
            ChartPlotStyleOptionsPlanner.RadarStyleOptions,
            _radarCombo.SelectedIndex,
            option => option.Value,
            _planner.RadarStyle));
    }

    private static int FindScatterIndex(ScatterStyle value) =>
        ChartDialogOptionProjection.FindIndex(
            ChartPlotStyleOptionsPlanner.ScatterStyleOptions,
            value,
            option => option.Value);

    private static int FindRadarIndex(RadarStyle value) =>
        ChartDialogOptionProjection.FindIndex(
            ChartPlotStyleOptionsPlanner.RadarStyleOptions,
            value,
            option => option.Value);
}

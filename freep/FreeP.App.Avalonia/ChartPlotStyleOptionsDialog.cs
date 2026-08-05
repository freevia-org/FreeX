using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartPlotStyleOptionsDialog : Window
{
    private readonly EditingSession _editor;
    private readonly ChartPlotStyleOptionsPlanner _planner;
    private readonly ComboBox _scatterCombo;
    private readonly ComboBox _radarCombo;

    internal ChartPlotStyleOptionsDialog(EditingSession editor)
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
        MinWidth = 380;
        MinHeight = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _scatterCombo = new ComboBox
        {
            ItemsSource = ChartPlotStyleOptionsPlanner.ScatterStyleOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindScatterIndex(_planner.ScatterStyle),
            IsEnabled = chart.ChartType == ChartType.Scatter,
            MinWidth = 190,
        };
        _radarCombo = new ComboBox
        {
            ItemsSource = ChartPlotStyleOptionsPlanner.RadarStyleOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindRadarIndex(_planner.RadarStyle),
            IsEnabled = chart.ChartType == ChartType.Radar,
            MinWidth = 190,
        };

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            () => Close(false));

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.ScatterStyleLabel, _scatterCombo, 190),
                ChartOptionsDialogChrome.CreateRow(surface.RadarStyleLabel, _radarCombo, 190),
                new TextBlock { Text = surface.Hint, Opacity = 0.7, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                buttons,
            },
        };
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
        Close(true);
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

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
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
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
                MakeRow(surface.ScatterStyleLabel, _scatterCombo),
                MakeRow(surface.RadarStyleLabel, _radarCombo),
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
        if (_scatterCombo.SelectedIndex >= 0 && _scatterCombo.SelectedIndex < ChartPlotStyleOptionsPlanner.ScatterStyleOptions.Count)
            _planner.SetScatterStyle(ChartPlotStyleOptionsPlanner.ScatterStyleOptions[_scatterCombo.SelectedIndex].Value);
        if (_radarCombo.SelectedIndex >= 0 && _radarCombo.SelectedIndex < ChartPlotStyleOptionsPlanner.RadarStyleOptions.Count)
            _planner.SetRadarStyle(ChartPlotStyleOptionsPlanner.RadarStyleOptions[_radarCombo.SelectedIndex].Value);
    }

    private static int FindScatterIndex(ScatterStyle value) =>
        Math.Max(0, ChartPlotStyleOptionsPlanner.ScatterStyleOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static int FindRadarIndex(RadarStyle value) =>
        Math.Max(0, ChartPlotStyleOptionsPlanner.RadarStyleOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("190, *") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
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
}

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
    private readonly ChartPlotStyleOptionsDialogSession _session;
    private readonly ComboBox _scatterCombo;
    private readonly ComboBox _radarCombo;

    internal ChartPlotStyleOptionsDialog(EditingSession editor)
    {
        _session = new ChartPlotStyleOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;
        Title = surface.Title;
        Width = ChartPlotStyleOptionsPlanner.DefaultDialogWidth;
        Height = ChartPlotStyleOptionsPlanner.DefaultDialogHeight;
        MinWidth = 380;
        MinHeight = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _scatterCombo = new ComboBox
        {
            ItemsSource = _session.ScatterStyleOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = state.ScatterStyleIndex,
            IsEnabled = state.IsScatterEnabled,
            MinWidth = 190,
        };
        _radarCombo = new ComboBox
        {
            ItemsSource = _session.RadarStyleOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = state.RadarStyleIndex,
            IsEnabled = state.IsRadarEnabled,
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

    internal ChartPlotStyleOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(ScatterStyle scatterStyle, RadarStyle radarStyle)
    {
        _scatterCombo.SelectedIndex = _session.FindScatterIndex(scatterStyle);
        _radarCombo.SelectedIndex = _session.FindRadarIndex(radarStyle);
    }

    private void OnOk()
    {
        _session.Submit(ReadInput());
        Close(true);
    }

    private ChartPlotStyleOptionsDialogInput ReadInput() => new(
        _scatterCombo.SelectedIndex,
        _radarCombo.SelectedIndex);
}

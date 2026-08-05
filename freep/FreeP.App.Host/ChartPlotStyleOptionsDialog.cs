using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style Scatter/Radar plot-style dialog.</summary>
public sealed class ChartPlotStyleOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartPlotStyleOptionsDialogSession _session;
    private readonly ComboBox _scatterCombo;
    private readonly ComboBox _radarCombo;

    public ChartPlotStyleOptionsDialog(EditingSession editor)
    {
        _session = new ChartPlotStyleOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;
        Title = surface.Title;
        Width = ChartPlotStyleOptionsPlanner.DefaultDialogWidth;
        Height = ChartPlotStyleOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _scatterCombo = new ComboBox
        {
            ItemsSource = _session.ScatterStyleOptions,
            DisplayMemberPath = nameof(ChartScatterStyleOption.Label),
            SelectedIndex = state.ScatterStyleIndex,
            IsEnabled = state.IsScatterEnabled,
            MinWidth = 190,
        };
        _radarCombo = new ComboBox
        {
            ItemsSource = _session.RadarStyleOptions,
            DisplayMemberPath = nameof(ChartRadarStyleOption.Label),
            SelectedIndex = state.RadarStyleIndex,
            IsEnabled = state.IsRadarEnabled,
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
        DialogResult = true;
    }

    private ChartPlotStyleOptionsDialogInput ReadInput() => new(
        _scatterCombo.SelectedIndex,
        _radarCombo.SelectedIndex);
}

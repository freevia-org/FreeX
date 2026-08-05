using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartAreaOptionsDialog : Window
{
    private readonly ChartAreaOptionsDialogSession _session;
    private readonly ComboBox _targetCombo;
    private readonly TextBox _fillBox;
    private readonly TextBox _fillTransparencyBox;
    private readonly CheckBox _noFillCheck;
    private readonly TextBox _outlineBox;
    private readonly CheckBox _noOutlineCheck;
    private readonly TextBox _widthBox;

    internal ChartAreaOptionsDialog(EditingSession editor, ChartAreaFormattingTarget? initialTarget = null)
    {
        _session = new ChartAreaOptionsDialogSession(editor, initialTarget);
        var state = _session.State;
        var surface = ChartAreaOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = 400;
        Height = 340;
        MinWidth = 400;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _targetCombo = new ComboBox { ItemsSource = ChartAreaOptionsPlanner.TargetOptions.Select(x => x.Label).ToArray(), SelectedIndex = state.TargetIndex, MinWidth = 190 };
        _targetCombo.SelectionChanged += (_, _) => LoadControls(_session.SelectTarget(_targetCombo.SelectedIndex));
        _fillBox = new TextBox { MinWidth = 190 };
        _fillTransparencyBox = new TextBox { MinWidth = 120 };
        _noFillCheck = new CheckBox { Content = surface.NoFillLabel };
        _outlineBox = new TextBox { MinWidth = 190 };
        _noOutlineCheck = new CheckBox { Content = surface.NoOutlineLabel };
        _widthBox = new TextBox { MinWidth = 120 };
        LoadControls(state);

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            () => Close(false));
        Content = new StackPanel
        {
            Margin = new Thickness(14), Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.TargetLabel, _targetCombo, 170),
                ChartOptionsDialogChrome.CreateRow(surface.FillLabel, _fillBox, 170),
                ChartOptionsDialogChrome.CreateRow(surface.FillTransparencyLabel, _fillTransparencyBox, 170),
                _noFillCheck,
                ChartOptionsDialogChrome.CreateRow(surface.OutlineLabel, _outlineBox, 170),
                _noOutlineCheck,
                ChartOptionsDialogChrome.CreateRow(surface.WidthLabel, _widthBox, 170),
                new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartAreaOptions BuildCommitPlanForTests()
        => _session.BuildCommitPlan(ReadInput(), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(ChartAreaFormattingTarget target, string? fill, string? outline, double? width, bool noFill = false, bool noOutline = false, double? fillTransparency = null)
    {
        _targetCombo.SelectedIndex = target == ChartAreaFormattingTarget.PlotArea ? 1 : 0;
        _fillBox.Text = fill ?? string.Empty;
        _fillTransparencyBox.Text = Format(fillTransparency);
        _noFillCheck.IsChecked = noFill;
        _outlineBox.Text = outline ?? string.Empty;
        _noOutlineCheck.IsChecked = noOutline;
        _widthBox.Text = Format(width);
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
            Close(true);
        else
            Close(false);
    }

    private void LoadControls(ChartAreaOptionsDialogState state)
    {
        _fillBox.Text = state.FillColor;
        _fillTransparencyBox.Text = Format(state.FillTransparencyPercent);
        _noFillCheck.IsChecked = state.NoFill;
        _outlineBox.Text = state.OutlineColor;
        _noOutlineCheck.IsChecked = state.NoOutline;
        _widthBox.Text = Format(state.OutlineWidthPt);
    }

    private ChartAreaOptionsDialogInput ReadInput() => new(
        _targetCombo.SelectedIndex,
        _fillBox.Text,
        _fillTransparencyBox.Text,
        _noFillCheck.IsChecked == true,
        _outlineBox.Text,
        _noOutlineCheck.IsChecked == true,
        _widthBox.Text);

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}

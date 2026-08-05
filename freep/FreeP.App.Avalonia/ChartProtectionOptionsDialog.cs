using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartProtectionOptionsDialog : Window
{
    private readonly ChartProtectionOptionsDialogSession _session;
    private readonly ComboBox _chartObjectCombo;
    private readonly ComboBox _dataCombo;
    private readonly ComboBox _formattingCombo;
    private readonly ComboBox _selectionCombo;

    internal ChartProtectionOptionsDialog(EditingSession editor)
    {
        _session = new ChartProtectionOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartProtectionOptionsPlanner.DefaultDialogWidth;
        Height = ChartProtectionOptionsPlanner.DefaultDialogHeight;
        MinWidth = 380;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _chartObjectCombo = BuildBooleanCombo(state.ChartObjectIndex);
        _dataCombo = BuildBooleanCombo(state.DataIndex);
        _formattingCombo = BuildBooleanCombo(state.FormattingIndex);
        _selectionCombo = BuildBooleanCombo(state.SelectionIndex);

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
                ChartOptionsDialogChrome.CreateRow(surface.ChartObjectLabel, _chartObjectCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.DataLabel, _dataCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FormattingLabel, _formattingCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.SelectionLabel, _selectionCombo, 180),
                new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartProtectionOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(bool? chartObject, bool? data, bool? formatting, bool? selection)
    {
        _chartObjectCombo.SelectedIndex = _session.FindBooleanIndex(chartObject);
        _dataCombo.SelectedIndex = _session.FindBooleanIndex(data);
        _formattingCombo.SelectedIndex = _session.FindBooleanIndex(formatting);
        _selectionCombo.SelectedIndex = _session.FindBooleanIndex(selection);
    }

    private void OnOk()
    {
        _session.Submit(ReadInput());
        Close(true);
    }

    private ComboBox BuildBooleanCombo(int selectedIndex) => new()
    {
        ItemsSource = _session.BooleanOptions.Select(option => option.Label).ToArray(),
        SelectedIndex = selectedIndex,
        MinWidth = 180,
    };

    private ChartProtectionOptionsDialogInput ReadInput() => new(
        _chartObjectCombo.SelectedIndex,
        _dataCombo.SelectedIndex,
        _formattingCombo.SelectedIndex,
        _selectionCombo.SelectedIndex);
}

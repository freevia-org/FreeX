using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart object/data/formatting/selection protection dialog.</summary>
public sealed class ChartProtectionOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartProtectionOptionsDialogSession _session;
    private readonly ComboBox _chartObjectCombo;
    private readonly ComboBox _dataCombo;
    private readonly ComboBox _formattingCombo;
    private readonly ComboBox _selectionCombo;

    public ChartProtectionOptionsDialog(EditingSession editor)
    {
        _session = new ChartProtectionOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartProtectionOptionsPlanner.DefaultDialogWidth;
        Height = ChartProtectionOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _chartObjectCombo = BuildBooleanCombo(state.ChartObjectIndex);
        _dataCombo = BuildBooleanCombo(state.DataIndex);
        _formattingCombo = BuildBooleanCombo(state.FormattingIndex);
        _selectionCombo = BuildBooleanCombo(state.SelectionIndex);

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.ChartObjectLabel, _chartObjectCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.DataLabel, _dataCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FormattingLabel, _formattingCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.SelectionLabel, _selectionCombo, 180));
        content.Children.Add(new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
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
        DialogResult = true;
    }

    private ComboBox BuildBooleanCombo(int selectedIndex) => new()
    {
        ItemsSource = _session.BooleanOptions,
        DisplayMemberPath = nameof(ChartProtectionBooleanOption.Label),
        SelectedIndex = selectedIndex,
        MinWidth = 180,
    };

    private ChartProtectionOptionsDialogInput ReadInput() => new(
        _chartObjectCombo.SelectedIndex,
        _dataCombo.SelectedIndex,
        _formattingCombo.SelectedIndex,
        _selectionCombo.SelectedIndex);
}

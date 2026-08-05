using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartBubbleOptionsDialog : Window
{
    private readonly ChartBubbleOptionsDialogSession _session;
    private readonly TextBox _scaleBox;
    private readonly ComboBox _sizeRepresentsCombo;
    private readonly CheckBox _negativeBubblesCheck;

    internal ChartBubbleOptionsDialog(EditingSession editor)
    {
        _session = new ChartBubbleOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartBubbleOptionsPlanner.DefaultDialogWidth;
        Height = ChartBubbleOptionsPlanner.DefaultDialogHeight;
        MinWidth = 360;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _scaleBox = new TextBox { Text = state.BubbleScaleText, MinWidth = 150 };
        _sizeRepresentsCombo = new ComboBox
        {
            ItemsSource = _session.SizeRepresentsOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = state.SizeRepresentsIndex,
            MinWidth = 150,
        };
        _negativeBubblesCheck = new CheckBox { Content = surface.ShowNegativeBubblesLabel, IsChecked = state.ShowNegativeBubbles };

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
                ChartOptionsDialogChrome.CreateRow(surface.BubbleScaleLabel, _scaleBox, 190),
                ChartOptionsDialogChrome.CreateRow(surface.SizeRepresentsLabel, _sizeRepresentsCombo, 190),
                _negativeBubblesCheck,
                new TextBlock { Text = surface.Hint, Opacity = 0.7, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                buttons,
            },
        };
    }

    internal ChartBubbleOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(int bubbleScalePercent, BubbleSizeRepresentation sizeRepresents, bool showNegativeBubbles)
    {
        _scaleBox.Text = _session.Format(bubbleScalePercent);
        _sizeRepresentsCombo.SelectedIndex = _session.FindSizeRepresentsIndex(sizeRepresents);
        _negativeBubblesCheck.IsChecked = showNegativeBubbles;
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
            Close(true);
    }

    private ChartBubbleOptionsDialogInput ReadInput() => new(
        _scaleBox.Text,
        _sizeRepresentsCombo.SelectedIndex,
        _negativeBubblesCheck.IsChecked == true);
}

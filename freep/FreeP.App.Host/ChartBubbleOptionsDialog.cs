using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style bubble chart sizing options dialog.</summary>
public sealed class ChartBubbleOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartBubbleOptionsDialogSession _session;
    private readonly TextBox _scaleBox;
    private readonly ComboBox _sizeRepresentsCombo;
    private readonly CheckBox _negativeBubblesCheck;

    public ChartBubbleOptionsDialog(EditingSession editor)
    {
        _session = new ChartBubbleOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartBubbleOptionsPlanner.DefaultDialogWidth;
        Height = ChartBubbleOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _scaleBox = new TextBox { Text = state.BubbleScaleText, MinWidth = 150 };
        _sizeRepresentsCombo = new ComboBox
        {
            ItemsSource = _session.SizeRepresentsOptions,
            DisplayMemberPath = nameof(ChartBubbleSizeRepresentationOption.Label),
            SelectedIndex = state.SizeRepresentsIndex,
            MinWidth = 150,
        };
        _negativeBubblesCheck = new CheckBox
        {
            Content = surface.ShowNegativeBubblesLabel,
            IsChecked = state.ShowNegativeBubbles,
        };

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.BubbleScaleLabel, _scaleBox, 190));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.SizeRepresentsLabel, _sizeRepresentsCombo, 190));
        content.Children.Add(_negativeBubblesCheck);
        content.Children.Add(new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
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
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(
            this,
            result.ValidationMessage,
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private ChartBubbleOptionsDialogInput ReadInput() => new(
        _scaleBox.Text,
        _sizeRepresentsCombo.SelectedIndex,
        _negativeBubblesCheck.IsChecked == true);
}

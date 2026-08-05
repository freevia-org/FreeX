using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart-wide default text formatting dialog.</summary>
public sealed class ChartTextOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartTextOptionsDialogSession _session;
    private readonly TextBox _fontFamilyBox;
    private readonly TextBox _fontSizeBox;
    private readonly ComboBox _boldCombo;
    private readonly ComboBox _italicCombo;
    private readonly TextBox _colorBox;

    public ChartTextOptionsDialog(EditingSession editor, ChartTextTarget target = ChartTextTarget.Chart)
    {
        _session = new ChartTextOptionsDialogSession(editor, target);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartTextOptionsPlanner.DefaultDialogWidth;
        Height = ChartTextOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _fontFamilyBox = new TextBox { Text = state.FontFamilyText, MinWidth = 180 };
        _fontSizeBox = new TextBox { Text = state.FontSizeText, MinWidth = 180 };
        _boldCombo = BuildBooleanCombo(state.BoldIndex);
        _italicCombo = BuildBooleanCombo(state.ItalicIndex);
        _colorBox = new TextBox { Text = state.ColorText, MinWidth = 180 };

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FontFamilyLabel, _fontFamilyBox, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FontSizeLabel, _fontSizeBox, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.BoldLabel, _boldCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.ItalicLabel, _italicCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.ColorLabel, _colorBox, 180));
        content.Children.Add(new TextBlock { Text = surface.AutoHint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartTextOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, result.ValidationMessage, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private ComboBox BuildBooleanCombo(int selectedIndex) => new()
    {
        ItemsSource = _session.BooleanOptions,
        DisplayMemberPath = nameof(ChartTextBooleanOption.Label),
        SelectedIndex = selectedIndex,
        MinWidth = 180,
    };

    private ChartTextOptionsDialogInput ReadInput() => new(
        _fontFamilyBox.Text,
        _fontSizeBox.Text,
        _boldCombo.SelectedIndex,
        _italicCombo.SelectedIndex,
        _colorBox.Text);
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartTextOptionsDialog : Window
{
    private readonly ChartTextOptionsDialogSession _session;
    private readonly TextBox _fontFamilyBox;
    private readonly TextBox _fontSizeBox;
    private readonly ComboBox _boldCombo;
    private readonly ComboBox _italicCombo;
    private readonly TextBox _colorBox;

    internal ChartTextOptionsDialog(EditingSession editor, ChartTextTarget target = ChartTextTarget.Chart)
    {
        _session = new ChartTextOptionsDialogSession(editor, target);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartTextOptionsPlanner.DefaultDialogWidth;
        Height = ChartTextOptionsPlanner.DefaultDialogHeight;
        MinWidth = 380;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _fontFamilyBox = new TextBox { Text = state.FontFamilyText, MinWidth = 180 };
        _fontSizeBox = new TextBox { Text = state.FontSizeText, MinWidth = 180 };
        _boldCombo = BuildBooleanCombo(state.BoldIndex);
        _italicCombo = BuildBooleanCombo(state.ItalicIndex);
        _colorBox = new TextBox { Text = state.ColorText, MinWidth = 180 };

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
                ChartOptionsDialogChrome.CreateRow(surface.FontFamilyLabel, _fontFamilyBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FontSizeLabel, _fontSizeBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.BoldLabel, _boldCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.ItalicLabel, _italicCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.ColorLabel, _colorBox, 180),
                new TextBlock { Text = surface.AutoHint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartTextOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(string? fontFamily, double? fontSizePt, bool? bold, bool? italic, string? color)
    {
        _fontFamilyBox.Text = fontFamily ?? string.Empty;
        _fontSizeBox.Text = _session.FormatFontSize(fontSizePt);
        _boldCombo.SelectedIndex = _session.FindBooleanIndex(bold);
        _italicCombo.SelectedIndex = _session.FindBooleanIndex(italic);
        _colorBox.Text = color ?? string.Empty;
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
        {
            Close(true);
            return;
        }

        Close(false);
    }

    private ComboBox BuildBooleanCombo(int selectedIndex) => new()
    {
        ItemsSource = _session.BooleanOptions.Select(option => option.Label).ToArray(),
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

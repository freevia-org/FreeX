using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class MultilevelListDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = new(AvaloniaCompactDialogChrome.WindowsUiFontFamily)
    {
        ControlHeight = 20,
        TextBoxHeight = 18,
        ComboBoxHeight = 22,
        ButtonHeight = 20,
        ComboBoxBackgroundBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new global::Avalonia.Media.GradientStop(Color.FromRgb(240, 240, 240), 0),
                new global::Avalonia.Media.GradientStop(Color.FromRgb(229, 229, 229), 1),
            ],
        },
    };
    private static readonly IBrush ComboBorderBrush = new SolidColorBrush(Color.FromRgb(172, 172, 172));
    private readonly ComboBox _levels;
    private readonly TextBox _level0Start;
    private readonly TextBox _level1Start;
    private readonly ComboBox _level0Format;
    private readonly ComboBox _level1Format;
    private readonly ComboBox _level2Format;

    internal MultilevelListDialog(IReadOnlyList<ListNumberFormat> currentFormats)
    {
        var state = MultilevelListDialogPlanner.BuildInitialState(currentFormats, CultureInfo.CurrentCulture);
        Title = MultilevelListDialogPlanner.Title;
        // Match the WPF prompt's outer width. The visual harness subtracts the native
        // frame when arranging Avalonia, so this remains the same content contract in
        // both the desktop app and paired evidence captures.
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _levels = Combo(Enumerable.Range(1, 9).Select(value => value.ToString(CultureInfo.CurrentCulture)), state.LevelsIndex, 80);
        _level0Start = TextBox(state.Level0StartAtText, 60);
        _level1Start = TextBox(state.Level1StartAtText, 60);
        var labels = MultilevelListDialogPlanner.NumberFormatChoices.Select(choice => choice.Label).ToArray();
        _level0Format = Combo(labels, state.Level0FormatIndex, 130);
        _level1Format = Combo(labels, state.Level1FormatIndex, 130);
        _level2Format = Combo(labels, state.Level2FormatIndex, 130);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Configure multilevel list levels.",
            Foreground = Brushes.Black,
            FontFamily = Chrome.FontFamily,
            FontSize = Chrome.FontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        AddField(panel, "Number of levels (1-9):", _levels);
        AddField(panel, "Level 1 start at:", _level0Start);
        AddField(panel, "Level 2 start at:", _level1Start);
        AddField(panel, "Level 1 number style:", _level0Format);
        AddField(panel, "Level 2 number style:", _level1Format);
        AddField(panel, "Level 3 number style:", _level2Format);
        var actionRow = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            Accept,
            () => Close(null),
            buttonWidth: 72,
            margin: new Thickness(0, 11, 0, 0),
            style: Chrome);
        panel.Children.Add(actionRow);
        Content = panel;
        Opened += (_, _) =>
        {
            // FreeWDialogWindow installs the shared default chrome before this route can
            // provide its WPF-sized control metrics. Reapply the route style after that
            // inherited hook so the rendered templates keep the authority dimensions.
            AvaloniaCompactDialogChrome.ApplyDescendantChrome(this, Chrome);
            // TextBox templates can be attached after the inherited visual walk. Keep the
            // route-owned controls at the authority height once their templates exist.
            ApplyComboBoxAuthorityChrome(_levels);
            AvaloniaCompactDialogChrome.ApplyTextBox(_level0Start, Chrome);
            AvaloniaCompactDialogChrome.ApplyTextBox(_level1Start, Chrome);
            ApplyComboBoxAuthorityChrome(_level0Format);
            ApplyComboBoxAuthorityChrome(_level1Format);
            ApplyComboBoxAuthorityChrome(_level2Format);
            foreach (var button in actionRow.Children.OfType<Button>())
                AvaloniaCompactDialogChrome.ApplyButton(button, Chrome, button.MinWidth, button.IsDefault);
            _levels.Focus();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Close(null);
            args.Handled = true;
        };
    }

    public static Task<MultilevelListDefinition?> ShowAsync(Window owner, IReadOnlyList<ListNumberFormat> formats) =>
        new MultilevelListDialog(formats).ShowDialog<MultilevelListDefinition?>(owner);

    private async void Accept()
    {
        if (TryBuildResult(out var result, out var validation))
        {
            Close(result);
            return;
        }
        await AvaloniaUserMessageDialog.ShowWarningAsync(
            this,
            validation?.Message ?? MultilevelListDialogPlanner.PositiveStartAtMessage);
        FocusValidationTarget(validation);
    }

    // The visual harness and headless tests need the WPF validation state without opening a
    // nested modal warning window that would block their dispatcher.
    internal void ValidateForTest()
    {
        if (TryBuildResult(out _, out var validation))
            return;
        FocusValidationTarget(validation);
    }

    private bool TryBuildResult(
        out MultilevelListDefinition? result,
        out MultilevelListDialogValidation? validation) =>
        MultilevelListDialogPlanner.TryBuildResult(
            new MultilevelListDialogInput(
                _levels.SelectedIndex, _level0Start.Text, _level1Start.Text,
                _level0Format.SelectedIndex, _level1Format.SelectedIndex, _level2Format.SelectedIndex),
            CultureInfo.CurrentCulture,
            out result,
            out validation);

    private void FocusValidationTarget(MultilevelListDialogValidation? validation)
    {
        var target = validation?.Field == MultilevelListDialogField.Level1StartAt ? _level1Start : _level0Start;
        AvaloniaCompactDialogChrome.FocusAndSelect(target);
    }

    private static void AddField(StackPanel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brushes.Black,
            FontFamily = Chrome.FontFamily,
            FontSize = Chrome.FontSize,
            Margin = new Thickness(0, 0, 0, 2),
        });
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        // Avalonia's text line metrics are taller than WPF's default TextBlock line box;
        // Keep WPF's twelve-pixel label-to-label rhythm after Avalonia's compact
        // templates render the authority-specific control heights.
        control.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(control);
    }

    private static ComboBox Combo(IEnumerable<string> items, int selectedIndex, double minWidth)
    {
        var combo = new ComboBox
        {
            ItemsSource = items.ToArray(),
            SelectedIndex = selectedIndex,
            MinWidth = minWidth,
        };
        ApplyComboBoxAuthorityChrome(combo);
        return combo;
    }

    private static void ApplyComboBoxAuthorityChrome(ComboBox combo)
    {
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, Chrome);
        combo.BorderBrush = ComboBorderBrush;
    }

    private static TextBox TextBox(string text, double minWidth)
    {
        var box = new TextBox { Text = text, MinWidth = minWidth };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, Chrome);
        return box;
    }
}

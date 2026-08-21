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

internal sealed partial class MultilevelListDialog : FreeWDialogWindow
{
    private static AvaloniaCompactDialogChromeStyle Chrome => AvaloniaCompactDialogChrome.WindowsStyle with
    {
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
    private static IBrush ComboBorderBrush => new SolidColorBrush(Color.FromRgb(172, 172, 172));
    private readonly MultilevelListDialogSession _session;
    private readonly ComboBox _levels;
    private readonly TextBox _level0Start;
    private readonly TextBox _level1Start;
    private readonly ComboBox _level0Format;
    private readonly ComboBox _level1Format;
    private readonly ComboBox _level2Format;

    internal MultilevelListDialog(IReadOnlyList<ListNumberFormat> currentFormats)
        : base(Chrome)
    {
        _session = MultilevelListDialogPlanner.CreateSession(currentFormats, CultureInfo.CurrentCulture);
        var state = _session.InitialState;
        Title = MultilevelListDialogPlanner.Title;
        // Match the WPF prompt's outer width. The visual harness subtracts the native
        // frame when arranging Avalonia, so this remains the same content contract in
        // both the desktop app and paired evidence captures.
        Width = MultilevelListDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _levels = Combo(_session.LevelChoices, state.LevelsIndex, MultilevelListDialogPlanner.LevelsMinWidth);
        _level0Start = TextBox(state.Level0StartAtText, MultilevelListDialogPlanner.StartAtMinWidth);
        _level1Start = TextBox(state.Level1StartAtText, MultilevelListDialogPlanner.StartAtMinWidth);
        var labels = _session.NumberFormatChoices.Select(choice => choice.Label).ToArray();
        _level0Format = Combo(labels, state.Level0FormatIndex, MultilevelListDialogPlanner.NumberFormatMinWidth);
        _level1Format = Combo(labels, state.Level1FormatIndex, MultilevelListDialogPlanner.NumberFormatMinWidth);
        _level2Format = Combo(labels, state.Level2FormatIndex, MultilevelListDialogPlanner.NumberFormatMinWidth);
        _levels.SelectionChanged += (_, _) => _session.UpdateLevels(_levels.SelectedIndex);
        _level0Start.TextChanged += (_, _) => _session.UpdateLevel0StartAt(_level0Start.Text);
        _level1Start.TextChanged += (_, _) => _session.UpdateLevel1StartAt(_level1Start.Text);
        _level0Format.SelectionChanged += (_, _) => _session.UpdateLevel0Format(_level0Format.SelectedIndex);
        _level1Format.SelectionChanged += (_, _) => _session.UpdateLevel1Format(_level1Format.SelectedIndex);
        _level2Format.SelectionChanged += (_, _) => _session.UpdateLevel2Format(_level2Format.SelectedIndex);

        var panel = new StackPanel { Margin = new Thickness(MultilevelListDialogPlanner.OuterMargin) };
        panel.Children.Add(new TextBlock
        {
            Text = MultilevelListDialogPlanner.Description,
            Foreground = Brushes.Black,
            FontFamily = Chrome.FontFamily,
            FontSize = Chrome.FontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        AddField(panel, MultilevelListDialogPlanner.LevelsLabel, _levels);
        AddField(panel, MultilevelListDialogPlanner.Level0StartAtLabel, _level0Start);
        AddField(panel, MultilevelListDialogPlanner.Level1StartAtLabel, _level1Start);
        AddField(panel, MultilevelListDialogPlanner.Level0NumberStyleLabel, _level0Format);
        AddField(panel, MultilevelListDialogPlanner.Level1NumberStyleLabel, _level1Format);
        AddField(panel, MultilevelListDialogPlanner.Level2NumberStyleLabel, _level2Format);
        var actionRow = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            Accept,
            () => Close(null),
            buttonWidth: MultilevelListDialogPlanner.ButtonWidth,
            // The shared 24/26-DIP controls render two DIPs taller than the WPF
            // prompt's rounded 96-DPI content stack. Keep the authority's terminal
            // button edge aligned without changing the shared control contract.
            margin: new Thickness(0, 9, 0, 0),
            style: Chrome);
        panel.Children.Add(actionRow);
        Content = panel;
        Opened += (_, _) =>
        {
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
        SynchronizeSession();
        var acceptance = _session.PlanAcceptance();
        if (acceptance.IsAccepted)
        {
            Close(acceptance.Definition);
            return;
        }
        await AvaloniaUserMessageDialog.ShowWarningAsync(
            this,
            acceptance.Validation?.Message ?? MultilevelListDialogPlanner.PositiveStartAtMessage);
        FocusValidationTarget(acceptance.Validation);
    }

    private void SynchronizeSession()
    {
        _session.UpdateLevels(_levels.SelectedIndex);
        _session.UpdateLevel0StartAt(_level0Start.Text);
        _session.UpdateLevel1StartAt(_level1Start.Text);
        _session.UpdateLevel0Format(_level0Format.SelectedIndex);
        _session.UpdateLevel1Format(_level1Format.SelectedIndex);
        _session.UpdateLevel2Format(_level2Format.SelectedIndex);
    }

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

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class MultilevelListDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = new(FontFamily.Default);
    private readonly ComboBox _levels;
    private readonly TextBox _level0Start;
    private readonly TextBox _level1Start;
    private readonly ComboBox _level0Format;
    private readonly ComboBox _level1Format;
    private readonly ComboBox _level2Format;
    private readonly TextBlock _status = new();

    internal MultilevelListDialog(IReadOnlyList<ListNumberFormat> currentFormats)
    {
        var state = MultilevelListDialogPlanner.BuildInitialState(currentFormats, CultureInfo.CurrentCulture);
        Title = MultilevelListDialogPlanner.Title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _levels = Combo(Enumerable.Range(1, 9).Select(value => value.ToString(CultureInfo.CurrentCulture)), state.LevelsIndex);
        _level0Start = TextBox(state.Level0StartAtText);
        _level1Start = TextBox(state.Level1StartAtText);
        var labels = MultilevelListDialogPlanner.NumberFormatChoices.Select(choice => choice.Label).ToArray();
        _level0Format = Combo(labels, state.Level0FormatIndex);
        _level1Format = Combo(labels, state.Level1FormatIndex);
        _level2Format = Combo(labels, state.Level2FormatIndex);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome, new Thickness(0, 6, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "Configure multilevel list levels.", Margin = new Thickness(0, 0, 0, 8) });
        AddField(panel, "Number of levels (1-9):", _levels);
        AddField(panel, "Level 1 start at:", _level0Start);
        AddField(panel, "Level 2 start at:", _level1Start);
        AddField(panel, "Level 1 number style:", _level0Format);
        AddField(panel, "Level 2 number style:", _level1Format);
        AddField(panel, "Level 3 number style:", _level2Format);
        panel.Children.Add(_status);
        var ok = Button("OK", true, false, Accept);
        var cancel = Button("Cancel", false, true, () => Close(null));
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)));
        Content = panel;
        Opened += (_, _) => _levels.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Close(null);
            args.Handled = true;
        };
    }

    public static Task<MultilevelListDefinition?> ShowAsync(Window owner, IReadOnlyList<ListNumberFormat> formats) =>
        new MultilevelListDialog(formats).ShowDialog<MultilevelListDefinition?>(owner);

    private void Accept()
    {
        if (MultilevelListDialogPlanner.TryBuildResult(
                new MultilevelListDialogInput(
                    _levels.SelectedIndex, _level0Start.Text, _level1Start.Text,
                    _level0Format.SelectedIndex, _level1Format.SelectedIndex, _level2Format.SelectedIndex),
                CultureInfo.CurrentCulture, out var result, out var validation))
        {
            Close(result);
            return;
        }
        _status.Text = validation?.Message ?? MultilevelListDialogPlanner.PositiveStartAtMessage;
        var target = validation?.Field == MultilevelListDialogField.Level1StartAt ? _level1Start : _level0Start;
        target.Focus();
        target.SelectAll();
    }

    private static void AddField(StackPanel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
        panel.Children.Add(control);
    }

    private static ComboBox Combo(IEnumerable<string> items, int selectedIndex)
    {
        var combo = new ComboBox { ItemsSource = items.ToArray(), SelectedIndex = selectedIndex };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, Chrome);
        return combo;
    }

    private static TextBox TextBox(string text)
    {
        var box = new TextBox { Text = text };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, Chrome);
        return box;
    }

    private static Button Button(string text, bool isDefault, bool isCancel, Action click)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, Chrome, 72, isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}

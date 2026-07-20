using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class FootnoteEndnoteOptionsDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = new(FontFamily.Default);
    private readonly ComboBox _footnoteFormat;
    private readonly TextBox _footnoteStart;
    private readonly ComboBox _footnoteRestart;
    private readonly ComboBox _endnoteFormat;
    private readonly TextBox _endnoteStart;
    private readonly ComboBox _endnoteRestart;
    private readonly TextBlock _status = new();

    internal FootnoteEndnoteOptionsDialog(NoteNumberingOptions footnote, NoteNumberingOptions endnote)
    {
        var state = FootnoteEndnoteOptionsDialogPlanner.BuildInitialState(footnote, endnote, CultureInfo.CurrentCulture);
        Title = FootnoteEndnoteOptionsDialogPlanner.Title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _footnoteFormat = Combo(FootnoteEndnoteOptionsDialogPlanner.FormatItems.Select(item => item.Label), state.FootnoteFormatIndex);
        _footnoteStart = TextBox(state.FootnoteStartAtText);
        _footnoteRestart = Combo(FootnoteEndnoteOptionsDialogPlanner.FootnoteRestartItems.Select(item => item.Label), state.FootnoteRestartIndex);
        _endnoteFormat = Combo(FootnoteEndnoteOptionsDialogPlanner.FormatItems.Select(item => item.Label), state.EndnoteFormatIndex);
        _endnoteStart = TextBox(state.EndnoteStartAtText);
        _endnoteRestart = Combo(FootnoteEndnoteOptionsDialogPlanner.EndnoteRestartItems.Select(item => item.Label), state.EndnoteRestartIndex);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome, new Thickness(0, 6, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(14) };
        AddSection(panel, FootnoteEndnoteOptionsDialogPlanner.FootnotesSectionLabel, _footnoteFormat, _footnoteStart, _footnoteRestart);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 8) });
        AddSection(panel, FootnoteEndnoteOptionsDialogPlanner.EndnotesSectionLabel, _endnoteFormat, _endnoteStart, _endnoteRestart);
        panel.Children.Add(_status);
        var ok = Button("OK", true, false, Accept);
        var cancel = Button("Cancel", false, true, () => Close(null));
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)));
        Content = panel;
        Opened += (_, _) => _footnoteFormat.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Close(null);
            args.Handled = true;
        };
    }

    public static Task<FootnoteEndnoteOptionsDialogResult?> ShowAsync(
        Window owner,
        NoteNumberingOptions footnote,
        NoteNumberingOptions endnote) =>
        new FootnoteEndnoteOptionsDialog(footnote, endnote)
            .ShowDialog<FootnoteEndnoteOptionsDialogResult?>(owner);

    internal FootnoteEndnoteOptionsDialogResult? BuildResultForTest() =>
        FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(
            Input(), CultureInfo.CurrentCulture, out var result, out _) ? result : null;

    private void Accept()
    {
        if (FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(
                Input(), CultureInfo.CurrentCulture, out var result, out var validation))
        {
            Close(result);
            return;
        }
        _status.Text = validation?.Message ?? FootnoteEndnoteOptionsDialogPlanner.PositiveStartAtMessage;
        var target = validation?.Field == FootnoteEndnoteOptionsDialogField.EndnoteStartAt ? _endnoteStart : _footnoteStart;
        target.Focus();
        target.SelectAll();
    }

    private FootnoteEndnoteOptionsDialogInput Input() => new(
        _footnoteFormat.SelectedIndex, _footnoteStart.Text, _footnoteRestart.SelectedIndex,
        _endnoteFormat.SelectedIndex, _endnoteStart.Text, _endnoteRestart.SelectedIndex);

    private static void AddSection(StackPanel panel, string heading, ComboBox format, TextBox start, ComboBox restart)
    {
        panel.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        AddField(panel, FootnoteEndnoteOptionsDialogPlanner.NumberFormatLabel, format);
        AddField(panel, FootnoteEndnoteOptionsDialogPlanner.StartAtLabel, start);
        AddField(panel, FootnoteEndnoteOptionsDialogPlanner.NumberingLabel, restart);
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

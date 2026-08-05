using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

internal sealed class DateTimeDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly DateTimeDialogSession _session;
    private readonly ListBox _list;
    private readonly CheckBox _updateAutomatically;

    internal DateTimeDialog(DateTime moment, CultureInfo culture)
    {
        _session = new DateTimeDialogSession(moment, culture);
        Title = DateTimeDialogSession.Title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _list = new ListBox
        {
            Height = 170,
            ItemsSource = _session.Formats,
            SelectedIndex = 0,
        };
        AvaloniaCompactDialogChrome.ApplyListBox(_list, Chrome);
        _list.DoubleTapped += (_, _) => Accept();

        _updateAutomatically = new CheckBox
        {
            Content = DateTimeDialogSession.UpdateAutomaticallyLabel,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var ok = Button("OK", isDefault: true, click: Accept);
        var cancel = Button("Cancel", isCancel: true, click: () => Close(null));
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock { Text = DateTimeDialogSession.FormatsLabel, Margin = new Thickness(0, 0, 0, 6) },
                _list,
                _updateAutomatically,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)),
            },
        };
        Opened += (_, _) => _list.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            Close(null);
            args.Handled = true;
        };
    }

    public static Task<DateTimeDialogResult?> ShowAsync(Window owner, DateTime moment, CultureInfo culture) =>
        new DateTimeDialog(moment, culture).ShowDialog<DateTimeDialogResult?>(owner);

    internal DateTimeDialogResult BuildResultForTest(int selectedIndex, bool updateAutomatically)
    {
        _session.UpdateSelection(Math.Clamp(selectedIndex, 0, _session.Formats.Count - 1));
        _session.UpdateAutomatically(updateAutomatically);
        return _session.PlanAcceptance()!;
    }

    private void Accept()
    {
        _session.UpdateSelection(_list.SelectedIndex);
        _session.UpdateAutomatically(_updateAutomatically.IsChecked == true);
        var result = _session.PlanAcceptance();
        if (result is not null)
            Close(result);
    }

    private static Button Button(string text, bool isDefault = false, bool isCancel = false, Action? click = null)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, Chrome, 72, isDefault);
        if (click is not null)
            button.Click += (_, _) => click();
        return button;
    }
}

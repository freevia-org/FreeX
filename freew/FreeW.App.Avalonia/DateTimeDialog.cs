using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed record DateTimeDialogResult(string Text, bool IsField, string? FieldInstruction);

internal sealed class DateTimeDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly DateTime _moment;
    private readonly CultureInfo _culture;
    private readonly IReadOnlyList<DateTimeFormat> _formats;
    private readonly ListBox _list;
    private readonly CheckBox _updateAutomatically;

    internal DateTimeDialog(DateTime moment, CultureInfo culture)
    {
        _moment = moment;
        _culture = culture;
        _formats = DateTimeFormats.Build(moment, culture);
        Title = "Date and Time";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _list = new ListBox
        {
            Height = 170,
            ItemsSource = _formats.Select(format => format.Text).ToArray(),
            SelectedIndex = 0,
        };
        AvaloniaCompactDialogChrome.ApplyListBox(_list, Chrome);
        _list.DoubleTapped += (_, _) => Accept();

        _updateAutomatically = new CheckBox
        {
            Content = "Update automatically",
            Margin = new Thickness(0, 8, 0, 0),
        };

        var ok = Button("OK", isDefault: true, click: Accept);
        var cancel = Button("Cancel", isCancel: true, click: () => Close(null));
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock { Text = "Available formats:", Margin = new Thickness(0, 0, 0, 6) },
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
        var index = Math.Clamp(selectedIndex, 0, _formats.Count - 1);
        var text = _formats[index].Text;
        if (!updateAutomatically)
            return new DateTimeDialogResult(text, false, null);

        var keyword = index is 2 or 3 ? "TIME" : "DATE";
        var picture = DateTimeFormats.BuildFieldPicture(index, _culture);
        return new DateTimeDialogResult(text, true, $@" {keyword} \@ ""{picture}"" ");
    }

    private void Accept() => Close(BuildResultForTest(_list.SelectedIndex, _updateAutomatically.IsChecked == true));

    private static Button Button(string text, bool isDefault = false, bool isCancel = false, Action? click = null)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, Chrome, 72, isDefault);
        if (click is not null)
            button.Click += (_, _) => click();
        return button;
    }
}

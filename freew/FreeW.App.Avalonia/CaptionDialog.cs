using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Shell.Avalonia;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed record CaptionDialogResult(CaptionLabel Label, string Text);

internal sealed class CaptionDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = AvaloniaCompactDialogChrome.WindowsStyle;
    private static readonly CaptionLabel[] Labels =
    [
        CaptionLabel.Figure,
        CaptionLabel.Table,
        CaptionLabel.Equation,
    ];

    private readonly ComboBox _label;
    private readonly TextBox _text;

    internal CaptionDialog(CaptionLabel defaultLabel)
    {
        Title = "Insert Caption";
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _label = new ComboBox
        {
            ItemsSource = Labels.Select(Captions.LabelText).ToArray(),
            SelectedIndex = Math.Max(0, Array.IndexOf(Labels, defaultLabel)),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(_label, Chrome);

        _text = new TextBox();
        AvaloniaCompactDialogChrome.ApplyTextBox(_text, Chrome);

        var ok = Button("OK", isDefault: true, click: Accept);
        var cancel = Button("Cancel", isCancel: true, click: () => Close(null));
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Label:" },
                _label,
                new TextBlock { Text = "Caption:" },
                _text,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 8, 0, 0)),
            },
        };

        Opened += (_, _) => _text.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            Close(null);
            args.Handled = true;
        };
    }

    public static Task<CaptionDialogResult?> ShowAsync(Window owner, CaptionLabel defaultLabel) =>
        new CaptionDialog(defaultLabel).ShowDialog<CaptionDialogResult?>(owner);

    internal CaptionDialogResult BuildResultForTest(int selectedIndex, string? text)
    {
        var index = Math.Clamp(selectedIndex, 0, Labels.Length - 1);
        return new CaptionDialogResult(Labels[index], text?.Trim() ?? string.Empty);
    }

    internal CaptionLabel SelectedLabelForTest =>
        Labels[Math.Clamp(_label.SelectedIndex, 0, Labels.Length - 1)];

    private void Accept() => Close(BuildResultForTest(_label.SelectedIndex, _text.Text));

    private static Button Button(string text, bool isDefault = false, bool isCancel = false, Action? click = null)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, Chrome, 72, isDefault);
        if (click is not null)
            button.Click += (_, _) => click();
        return button;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed partial class CaptionDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly CaptionDialogPlan _plan;
    private readonly ComboBox _label;
    private readonly TextBox _text;

    internal CaptionDialog(CaptionLabel defaultLabel)
    {
        _plan = CaptionDialogPlanner.Build(defaultLabel);
        Title = _plan.Title;
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _label = new ComboBox
        {
            ItemsSource = _plan.Choices.Select(choice => choice.Label).ToArray(),
            SelectedIndex = _plan.SelectedIndex,
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
                new TextBlock { Text = _plan.LabelPrompt },
                _label,
                new TextBlock { Text = _plan.CaptionPrompt },
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

    private CaptionDialogResult BuildResult(int selectedIndex, string? text)
        => CaptionDialogPlanner.BuildResult(selectedIndex, text);

    private void Accept() => Close(BuildResult(_label.SelectedIndex, _text.Text));

    private static Button Button(string text, bool isDefault = false, bool isCancel = false, Action? click = null)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, Chrome, 72, isDefault);
        if (click is not null)
            button.Click += (_, _) => click();
        return button;
    }
}

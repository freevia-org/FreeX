using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>Owner-modal Insert &gt; Header/Footer text prompt matching the WPF TextPrompt contract.</summary>
internal sealed class HeaderFooterTextDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly TextBox _text;

    private HeaderFooterTextDialog(bool footer, string initial)
    {
        var plan = HeaderFooterTextDialogPlanner.Build(footer, initial);
        Title = plan.Title;
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _text = new TextBox
        {
            Text = plan.InitialText,
            Width = 340,
            AcceptsReturn = false,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(_text, DialogChromeStyle);

        var ok = new Button
        {
            Content = UiText.Get("Common_OkText"),
            IsDefault = true,
            MinWidth = 78,
        };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 78, isDefault: true);
        ok.Click += (_, _) => Close(HeaderFooterTextDialogPlanner.BuildResult(_text.Text));

        var cancel = new Button
        {
            Content = UiText.Get("Common_CancelText"),
            IsCancel = true,
            MinWidth = 78,
        };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 78);
        cancel.Click += (_, _) => Close(null);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(0, 10, 0, 0));
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = plan.PromptLabel },
                _text,
                buttons,
            },
        };

        Opened += (_, _) =>
        {
            _text.Focus();
            _text.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
    }

    public static Task<string?> ShowAsync(Window owner, bool footer, string initial) =>
        new HeaderFooterTextDialog(footer, initial).ShowDialog<string?>(owner);
}

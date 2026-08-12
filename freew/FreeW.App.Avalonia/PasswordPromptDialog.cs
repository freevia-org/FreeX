using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>Single-field password prompt matching the FreeW WPF authority surface.</summary>
internal sealed partial class PasswordPromptDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly TextBox _passwordBox = new()
    {
        MinWidth = 220,
        PasswordChar = '*',
    };
    private readonly PasswordPromptDialogSession _session;

    public string? Result { get; private set; }

    private PasswordPromptDialog(string title, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(prompt);
        _session = new PasswordPromptDialogSession(title, prompt);

        Title = _session.State.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        AutomationProperties.SetAutomationId(this, PasswordPromptDialogSession.WindowAutomationId);
        AutomationProperties.SetName(this, _session.State.Title);
        AutomationProperties.SetAutomationId(_passwordBox, PasswordPromptDialogSession.PasswordAutomationId);
        AutomationProperties.SetName(_passwordBox, _session.State.Prompt);
        AvaloniaCompactDialogChrome.ApplyTextBox(_passwordBox, DialogChromeStyle);

        var body = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
        };
        body.Children.Add(new TextBlock
        {
            Text = _session.State.Prompt,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(_passwordBox);

        var ok = new Button { Content = UiText.Get("Common_OkText"), IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, 72, isDefault: true);
        AutomationProperties.SetAutomationId(ok, PasswordPromptDialogSession.AcceptButtonAutomationId);
        ok.Click += (_, _) => Accept();

        var cancel = new Button { Content = UiText.Get("Common_CancelText"), IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, 72);
        AutomationProperties.SetAutomationId(cancel, PasswordPromptDialogSession.CancelButtonAutomationId);
        cancel.Click += (_, _) => Close();
        body.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(0, 4, 0, 0)));

        Content = body;
        Opened += (_, _) =>
        {
            _passwordBox.Focus();
            _passwordBox.SelectAll();
        };
    }

    public static async Task<string?> ShowAsync(Window owner, string title, string prompt)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var dialog = new PasswordPromptDialog(title, prompt);
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }

    private void Accept() => Accept(close: true);

    private void Accept(bool close)
    {
        _session.UpdatePassword(_passwordBox.Text);
        Result = _session.PlanAcceptance();
        if (close)
            Close();
    }
}

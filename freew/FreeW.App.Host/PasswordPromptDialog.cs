using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// A minimal single-field password prompt dialog. Used by <see cref="RestrictEditingDialog"/> to ask
/// the user to enter the document-protection password when removing protection. Returns the entered
/// password string, or null if the user cancelled.
/// </summary>
internal sealed class PasswordPromptDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly PasswordBox _passwordBox = new() { MinWidth = 220 };
    private readonly PasswordPromptDialogSession _session;
    private string? _result;

    private PasswordPromptDialog(Window? owner, string title, string prompt)
    {
        _session = new PasswordPromptDialogSession(title, prompt);
        Owner = owner;
        Title = _session.State.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, PasswordPromptDialogSession.WindowAutomationId);
        AutomationProperties.SetAutomationId(_passwordBox, PasswordPromptDialogSession.PasswordAutomationId);
        AutomationProperties.SetName(_passwordBox, _session.State.Prompt);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = _session.State.Prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(_passwordBox);

        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0)));

        Content = panel;
        Loaded += (_, _) => DialogFocus.Focus(_passwordBox);
    }

    private void Accept()
    {
        _session.UpdatePassword(_passwordBox.Password);
        _result = _session.PlanAcceptance();
        Close();
    }

    /// <summary>
    /// Show a password prompt with the given title and label text. Returns the entered string, or null
    /// if the user cancelled (pressed Cancel or Esc).
    /// </summary>
    public static string? Ask(Window? owner, string title, string prompt)
    {
        var dialog = new PasswordPromptDialog(owner, title, prompt);
        dialog.ShowDialog();
        return dialog._result;
    }
}

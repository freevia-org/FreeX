using System.Windows;
using System.Windows.Controls;

namespace FreeW.App.Host;

/// <summary>
/// A minimal single-field password prompt dialog. Used by <see cref="RestrictEditingDialog"/> to ask
/// the user to enter the document-protection password when removing protection. Returns the entered
/// password string, or null if the user cancelled.
/// </summary>
internal sealed class PasswordPromptDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly PasswordBox _passwordBox = new() { MinWidth = 220 };
    private string? _result;

    private PasswordPromptDialog(Window? owner, string title, string prompt)
    {
        Owner = owner;
        Title = title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(_passwordBox);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", MinWidth = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { _result = _passwordBox.Password; Close(); };
        var cancel = new Button { Content = "Cancel", MinWidth = 72, IsCancel = true };
        buttonRow.Children.Add(ok);
        buttonRow.Children.Add(cancel);
        panel.Children.Add(buttonRow);

        Content = panel;
        Loaded += (_, _) => _passwordBox.Focus();
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

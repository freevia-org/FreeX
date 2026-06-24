using System.Windows;
using System.Windows.Controls;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Restrict Editing" pane (Review &gt; Protect &gt; Restrict Editing). Lets the user choose
/// "Allow only this type of editing in the document": No changes (Read only), Tracked changes,
/// Comments, or Filling in forms — and start enforcing it, or stop protection. Maps directly onto a
/// <see cref="ProtectionSettings"/> that the writer persists as word/settings.xml's w:documentProtection
/// and the host enforces on the live editor.
///
/// <para>An optional password can be entered when starting protection. The password is hashed using the
/// OOXML legacy SHA-1 algorithm (via <see cref="ProtectionPasswordHelper"/>) and stored in the model so
/// it persists through docx save/load and is honoured by Microsoft Word. When a password is stored,
/// "Stop Protection" asks for it before removing protection.</para>
///
/// <para>Returns a <see cref="ProtectionSettings"/> (with or without a password hash) when the user
/// acted, or null if cancelled.</para>
/// </summary>
internal sealed class RestrictEditingDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // Radio options in display order, paired with the mode each enforces.
    private static readonly (string Label, ProtectionMode Mode)[] Options =
    [
        ("No changes (Read only)", ProtectionMode.ReadOnly),
        ("Tracked changes", ProtectionMode.TrackChangesOnly),
        ("Comments", ProtectionMode.CommentsOnly),
        ("Filling in forms", ProtectionMode.FillingForms)
    ];

    private readonly RadioButton[] _radios;
    private readonly PasswordBox _passwordBox;
    private readonly PasswordBox _confirmBox;
    private readonly ProtectionSettings _currentProtection;
    private ProtectionSettings? _result;

    private RestrictEditingDialog(Window? owner, ProtectionSettings current)
    {
        Owner = owner;
        Title = "Restrict Editing";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _currentProtection = current;
        _passwordBox = new PasswordBox { MinWidth = 180 };
        _confirmBox = new PasswordBox { MinWidth = 180 };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Allow only this type of editing in the document:",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _radios = new RadioButton[Options.Length];
        for (var i = 0; i < Options.Length; i++)
        {
            var (label, mode) = Options[i];
            _radios[i] = new RadioButton
            {
                Content = label,
                Margin = new Thickness(0, 3, 0, 3),
                // Seed from the current mode; default to the first option (Read only) when unprotected.
                IsChecked = current.Mode == ProtectionMode.None ? i == 0 : current.Mode == mode
            };
            panel.Children.Add(_radios[i]);
        }

        // Password entry (optional) — only relevant for Start Enforcing. Not shown when already protected.
        if (!current.IsProtected)
        {
            panel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 6) });
            panel.Children.Add(new TextBlock
            {
                Text = "Optional password (leave blank for no password):",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock { Text = "Password:", Margin = new Thickness(0, 0, 0, 2) });
            panel.Children.Add(_passwordBox);
            panel.Children.Add(new TextBlock { Text = "Confirm:", Margin = new Thickness(0, 4, 0, 2) });
            panel.Children.Add(_confirmBox);
        }

        // Two action rows. "Start Enforcing Protection" applies the chosen mode; "Stop Protection"
        // clears it (enabled only while protected). Cancel closes without changing anything.
        var enforce = new Button
        {
            Content = "Start Enforcing Protection",
            MinWidth = 200,
            Margin = new Thickness(0, 14, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = !current.IsProtected
        };
        enforce.Click += (_, _) => Enforce();
        panel.Children.Add(enforce);

        var stop = new Button
        {
            Content = "Stop Protection",
            MinWidth = 180,
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = current.IsProtected
        };
        stop.Click += (_, _) => StopProtection();
        panel.Children.Add(stop);

        // Shared Cancel row (IsCancel button so Esc closes). OK is suppressed — the two action buttons
        // above are the commit gestures, matching Word's pane.
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 72,
            IsCancel = true,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        panel.Children.Add(cancel);

        Content = panel;
        Loaded += (_, _) => _radios[0].Focus();
    }

    private void Enforce()
    {
        // Validate passwords match (if a password was entered).
        var password = _passwordBox.Password;
        var confirm = _confirmBox.Password;
        if (password != confirm)
        {
            DialogMessageHelper.ShowWarning(this, "The passwords do not match. Please re-enter.", Title);
            _passwordBox.Focus();
            return;
        }

        ProtectionMode mode = ProtectionMode.ReadOnly;
        for (var i = 0; i < _radios.Length; i++)
        {
            if (_radios[i].IsChecked == true)
            {
                mode = Options[i].Mode;
                break;
            }
        }

        _result = string.IsNullOrEmpty(password)
            ? new ProtectionSettings(mode)
            : ProtectionPasswordHelper.CreateWithPassword(mode, password);
        Close();
    }

    private void StopProtection()
    {
        // If the current protection has a password, require the user to enter it.
        if (_currentProtection.HasPassword)
        {
            var pw = PasswordPromptDialog.Ask(Owner, "Stop Protection", "Enter the password to remove protection:");
            if (pw is null)
                return; // cancelled
            if (!ProtectionPasswordHelper.VerifyPassword(_currentProtection, pw))
            {
                DialogMessageHelper.ShowWarning(this, "Incorrect password. Protection has not been removed.", Title);
                return;
            }
        }

        _result = ProtectionSettings.Unprotected;
        Close();
    }

    /// <summary>
    /// Show the pane seeded with the current protection settings. Returns the new
    /// <see cref="ProtectionSettings"/> (which may include a password hash), or null if cancelled.
    /// A return value of <see cref="ProtectionSettings.Unprotected"/> means protection was stopped.
    /// </summary>
    public static ProtectionSettings? Prompt(Window? owner, ProtectionSettings current)
    {
        var dialog = new RestrictEditingDialog(owner, current);
        dialog.ShowDialog();
        return dialog._result;
    }
}

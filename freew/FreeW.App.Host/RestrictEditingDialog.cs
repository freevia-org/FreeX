using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Restrict Editing" pane (Review &gt; Protect &gt; Restrict Editing). Lets the user choose
/// "Allow only this type of editing in the document": No changes (Read only), Tracked changes,
/// Comments, or Filling in forms — and start enforcing it, or stop protection. Maps directly onto a
/// <see cref="ProtectionMode"/> that the writer persists as word/settings.xml's w:documentProtection and
/// the host enforces on the live editor. Returns the chosen mode (<see cref="ProtectionMode.None"/> when
/// protection is stopped), or null if cancelled.
///
/// <para>No password hashing is implemented — enforcement is a simple unprotected toggle, which is
/// sufficient for FreeW's parity scope (Word permits unprotected enforcement too).</para>
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
    private ProtectionMode? _result;

    private RestrictEditingDialog(Window? owner, ProtectionMode current)
    {
        Owner = owner;
        Title = "Restrict Editing";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

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
                IsChecked = current == ProtectionMode.None ? i == 0 : current == mode
            };
            panel.Children.Add(_radios[i]);
        }

        // Two action rows. "Start Enforcing Protection" applies the chosen mode; "Stop Protection"
        // clears it (enabled only while protected). Cancel closes without changing anything.
        var enforce = new Button { Content = "Start Enforcing Protection", MinWidth = 180, Margin = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        enforce.Click += (_, _) => Enforce();
        panel.Children.Add(enforce);

        var stop = new Button { Content = "Stop Protection", MinWidth = 180, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = current != ProtectionMode.None };
        stop.Click += (_, _) => { _result = ProtectionMode.None; Close(); };
        panel.Children.Add(stop);

        // Shared Cancel row (IsCancel button so Esc closes). OK is suppressed — the two action buttons
        // above are the commit gestures, matching Word's pane.
        var cancel = new Button { Content = "Cancel", MinWidth = 72, IsCancel = true, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(cancel);

        Content = panel;
        Loaded += (_, _) => _radios[0].Focus();
    }

    private void Enforce()
    {
        for (var i = 0; i < _radios.Length; i++)
        {
            if (_radios[i].IsChecked == true)
            {
                _result = Options[i].Mode;
                Close();
                return;
            }
        }
        // No selection (shouldn't happen, one is seeded) → treat as Read only.
        _result = ProtectionMode.ReadOnly;
        Close();
    }

    /// <summary>
    /// Show the pane seeded with the current protection mode; returns the chosen mode (None when the user
    /// stops protection), or null if cancelled.
    /// </summary>
    public static ProtectionMode? Prompt(Window? owner, ProtectionMode current)
    {
        var dialog = new RestrictEditingDialog(owner, current);
        dialog.ShowDialog();
        return dialog._result;
    }
}

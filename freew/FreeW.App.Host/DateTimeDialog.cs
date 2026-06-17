using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A tiny modal dialog listing several formatted strings for the current moment (short/long date,
/// short/long time, date + time). Clicking one closes the dialog and returns its text; the caller
/// inserts it at the caret as plain text. Returns the chosen string, or null if the user cancels.
///
/// The moment is captured as <c>DateTime.Now</c> when the dialog opens; the actual formatting is done
/// by the pure, testable <see cref="DateTimeFormats"/> helper in the model project.
/// </summary>
internal sealed class DateTimeDialog : Window
{
    private string? _result;

    private DateTimeDialog(Window? owner, DateTime moment)
    {
        Owner = owner;
        Title = "Date and Time";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = "Available formats:",
            Margin = new Thickness(0, 0, 0, 6)
        });

        var list = new ListBox { Height = 160 };
        foreach (var format in DateTimeFormats.Build(moment))
            list.Items.Add(new FormatItem(format));
        list.SelectedIndex = 0;
        // Double-clicking a row inserts it immediately (matching the Word "Date and Time" dialog).
        list.MouseDoubleClick += (_, _) => Accept(list);
        panel.Children.Add(list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "OK", MinWidth = 72, Margin = new Thickness(6, 0, 0, 0), IsDefault = true };
        ok.Click += (_, _) => Accept(list);
        var cancel = new Button { Content = "Cancel", MinWidth = 72, Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        list.Focus();
    }

    private void Accept(ListBox list)
    {
        if (list.SelectedItem is FormatItem item)
        {
            _result = item.Format.Text;
            DialogResult = true;
        }
    }

    // Wraps a DateTimeFormat so the ListBox shows the inserted text but the label is available as a tooltip.
    private sealed record FormatItem(DateTimeFormat Format)
    {
        public override string ToString() => Format.Text;
    }

    /// <summary>Show the dialog (capturing <c>DateTime.Now</c>); returns the chosen string, or null if cancelled.</summary>
    public static string? Prompt(Window? owner)
    {
        var dialog = new DateTimeDialog(owner, DateTime.Now);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}

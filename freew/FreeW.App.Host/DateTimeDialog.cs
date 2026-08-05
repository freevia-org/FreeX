using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// A modal dialog listing several formatted strings for the current moment (short/long date,
/// short/long time, date + time). The user picks a format and optionally checks "Update
/// automatically" to insert a live DATE/TIME field instead of a static text string.
///
/// Returns a <see cref="DateTimeDialogResult"/> whose <see cref="DateTimeDialogResult.IsField"/>
/// indicates which path the caller should take: true → insert a DATE or TIME field using
/// <see cref="DateTimeDialogResult.FieldInstruction"/>; false → insert
/// <see cref="DateTimeDialogResult.Text"/> as plain text. Returns null if the user cancels.
/// </summary>
internal sealed class DateTimeDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly DateTimeDialogSession _session;
    private DateTimeDialogResult? _result;

    private DateTimeDialog(Window? owner, DateTime moment, CultureInfo culture)
    {
        _session = new DateTimeDialogSession(moment, culture);
        Owner = owner;
        Title = DateTimeDialogSession.Title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = DateTimeDialogSession.FormatsLabel,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var list = new ListBox { Height = 160 };
        foreach (var format in _session.Formats)
            list.Items.Add(format);
        list.SelectedIndex = 0;
        // Double-clicking a row inserts it immediately (matching the Word "Date and Time" dialog).
        list.MouseDoubleClick += (_, _) => Accept(list, updateCheckBox: null);
        panel.Children.Add(list);

        // "Update automatically" checkbox: when checked, inserts a DATE or TIME field instead
        // of static text so the value updates on every F9/field-update. Mirrors Word behaviour.
        var updateCheckBox = new CheckBox
        {
            Content = DateTimeDialogSession.UpdateAutomaticallyLabel,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "Insert a live DATE or TIME field that updates when you press F9, instead of static text."
        };
        panel.Children.Add(updateCheckBox);

        panel.Children.Add(DialogButtonRowFactory.Create(
            () => Accept(list, updateCheckBox), buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0)));

        Content = panel;
        list.Focus();
    }

    private void Accept(ListBox list, CheckBox? updateCheckBox)
    {
        _session.UpdateSelection(list.SelectedIndex);
        _session.UpdateAutomatically(updateCheckBox?.IsChecked == true);
        _result = _session.PlanAcceptance();
        if (_result is null)
            return;

        DialogResult = true;
    }

    /// <summary>
    /// Show the dialog (capturing <c>DateTime.Now</c> and <c>CultureInfo.CurrentCulture</c>);
    /// returns the result, or null if cancelled.
    /// </summary>
    public static DateTimeDialogResult? Prompt(Window? owner)
    {
        // Capture both moment and culture together so the displayed text and the \@ picture
        // string are derived from the same snapshot of the current locale.
        var dialog = new DateTimeDialog(owner, DateTime.Now, CultureInfo.CurrentCulture);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}

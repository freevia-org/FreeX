using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

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
    private DateTimeDialogResult? _result;

    private DateTimeDialog(Window? owner, DateTime moment, CultureInfo culture)
    {
        Owner = owner;
        Title = "Date and Time";
        Width = 340;
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
        var formats = DateTimeFormats.Build(moment, culture);
        foreach (var format in formats)
            list.Items.Add(new FormatItem(format));
        list.SelectedIndex = 0;
        // Double-clicking a row inserts it immediately (matching the Word "Date and Time" dialog).
        list.MouseDoubleClick += (_, _) => Accept(list, updateCheckBox: null, culture, formats);
        panel.Children.Add(list);

        // "Update automatically" checkbox: when checked, inserts a DATE or TIME field instead
        // of static text so the value updates on every F9/field-update. Mirrors Word behaviour.
        var updateCheckBox = new CheckBox
        {
            Content = "Update automatically",
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "Insert a live DATE or TIME field that updates when you press F9, instead of static text."
        };
        panel.Children.Add(updateCheckBox);

        panel.Children.Add(DialogButtonRowFactory.Create(
            () => Accept(list, updateCheckBox, culture, formats), buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0)));

        Content = panel;
        list.Focus();
    }

    private void Accept(ListBox list, CheckBox? updateCheckBox, CultureInfo culture, IReadOnlyList<DateTimeFormat> formats)
    {
        if (list.SelectedItem is not FormatItem item)
            return;

        var updateField = updateCheckBox?.IsChecked == true;
        if (updateField)
        {
            // Determine whether the chosen format is primarily time-based (the last two items,
            // "Short time" and "Long time"). Everything else maps to DATE; time-only maps to TIME.
            // The field instruction includes a \@ picture derived from the selected culture's
            // DateTimeFormatInfo patterns so the field re-renders with the same format shown to
            // the user, regardless of the system locale.
            var selectedIndex = list.SelectedIndex;
            // formats: 0=Short date, 1=Long date, 2=Short time, 3=Long time, 4=Date and time
            var isTimeOnly = selectedIndex >= 2 && selectedIndex <= 3;
            var keyword = isTimeOnly ? "TIME" : "DATE";
            // Derive the \@ picture from the culture's patterns via the shared pure helper so the
            // field picture matches exactly what DateTimeFormats.Build displayed to the user.
            var picture = DateTimeFormats.BuildFieldPicture(selectedIndex, culture);
            _result = new DateTimeDialogResult(item.Format.Text, IsField: true,
                FieldInstruction: $@" {keyword} \@ ""{picture}"" ");
        }
        else
        {
            _result = new DateTimeDialogResult(item.Format.Text, IsField: false, FieldInstruction: null);
        }

        DialogResult = true;
    }

    // Wraps a DateTimeFormat so the ListBox shows the inserted text but the label is available as a tooltip.
    private sealed record FormatItem(DateTimeFormat Format)
    {
        public override string ToString() => Format.Text;
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

/// <summary>
/// The result of <see cref="DateTimeDialog.Prompt"/>. When <see cref="IsField"/> is false the
/// caller inserts <see cref="Text"/> as plain text. When true the caller inserts a live DATE/TIME
/// complex field using <see cref="FieldInstruction"/> (e.g. <c> DATE \@ "M/d/yyyy" </c>); the
/// <see cref="Text"/> is then the initial cached result for immediate display.
/// </summary>
internal sealed record DateTimeDialogResult(string Text, bool IsField, string? FieldInstruction);

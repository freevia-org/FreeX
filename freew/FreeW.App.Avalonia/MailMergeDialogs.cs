using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-MAIL: modal dialogs for the Mailings tab — a recipient-list (CSV) editor and a merge-field-name
/// picker. Both are thin, dependency-free Avalonia windows that return their result (or <c>null</c> on
/// cancel) so the ribbon glue (<see cref="Ribbon.MailMergeEngine"/>) stays UI-agnostic and testable.
/// Mail-SEND is out of scope; these only gather a recipient list and a field name.
/// </summary>
internal static class MailMergeDialogs
{
    /// <summary>
    /// Recipient-list dialog: a multi-line CSV editor (first line = column headers). When the document
    /// already has merge fields, <paramref name="seedHeader"/> pre-fills the header line as a hint. Returns
    /// the entered CSV text, or <c>null</c> if cancelled / empty.
    /// </summary>
    public static async Task<string?> AskRecipientCsvAsync(Window owner, string seedHeader)
    {
        var dialog = new Window
        {
            Title = "Select Recipients",
            Width = 460,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
        };

        var hint = new TextBlock
        {
            Text = "Type or paste a recipient list as CSV. The first line is the column headers.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 14, 16, 6),
        };

        var editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            Margin = new Thickness(16, 0, 16, 0),
            Text = string.IsNullOrWhiteSpace(seedHeader) ? string.Empty : seedHeader + "\n",
            PlaceholderText ="FirstName,LastName,City…",
        };
        Grid.SetRow(editor, 1);

        string? result = null;

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72 };
        ok.Click += (_, _) =>
        {
            var text = editor.Text ?? string.Empty;
            result = string.IsNullOrWhiteSpace(text) ? null : text;
            dialog.Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 10, 16, 14),
            Children = { ok, cancel },
        };

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var hintHost = hint; Grid.SetRow(hintHost, 0);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(hintHost);
        grid.Children.Add(editor);
        grid.Children.Add(buttons);
        dialog.Content = grid;

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// Merge-field picker: an editable combo seeded with the available <paramref name="fieldNames"/> (the
    /// loaded recipient list's columns). The user can pick one or type a new name. Returns the chosen name,
    /// or <c>null</c> if cancelled / blank.
    /// </summary>
    public static async Task<string?> AskMergeFieldNameAsync(Window owner, IReadOnlyList<string> fieldNames)
    {
        var dialog = new Window
        {
            Title = "Insert Merge Field",
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var label = new TextBlock
        {
            Text = "Field name:",
            Margin = new Thickness(16, 16, 16, 4),
        };
        Grid.SetRow(label, 0);

        var combo = new ComboBox
        {
            ItemsSource = fieldNames,
            Margin = new Thickness(16, 0, 16, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = fieldNames.Count > 0 ? 0 : -1,
        };
        Grid.SetRow(combo, 1);

        // Also allow free text entry for a field not in the loaded list.
        var freeText = new TextBox
        {
            PlaceholderText ="…or type a field name",
            Margin = new Thickness(16, 8, 16, 0),
        };
        Grid.SetRow(freeText, 2);

        string? result = null;

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72 };
        ok.Click += (_, _) =>
        {
            var typed = freeText.Text?.Trim();
            result = !string.IsNullOrWhiteSpace(typed)
                ? typed
                : combo.SelectedItem as string;
            dialog.Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 12, 16, 14),
            Children = { ok, cancel },
        };
        Grid.SetRow(buttons, 3);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        grid.Children.Add(label);
        grid.Children.Add(combo);
        grid.Children.Add(freeText);
        grid.Children.Add(buttons);
        dialog.Content = grid;

        await dialog.ShowDialog(owner);
        return result;
    }
}

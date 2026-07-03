using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-MAIL: modal dialogs for the Mailings tab — a recipient-list (CSV) editor and a merge-field-name
/// picker. Both are thin, dependency-free Avalonia windows that return their result (or <c>null</c> on
/// cancel) so the ribbon glue (<see cref="Ribbon.MailMergeEngine"/>) stays UI-agnostic and testable.
/// Mail-SEND is out of scope; these only gather a recipient list and a field name.
/// </summary>
internal static class MailMergeDialogs
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

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
        AvaloniaCompactDialogChrome.ApplyTextBox(editor, DialogChromeStyle, fixedHeight: false);
        Grid.SetRow(editor, 1);

        string? result = null;

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) =>
        {
            var text = editor.Text ?? string.Empty;
            result = string.IsNullOrWhiteSpace(text) ? null : text;
            dialog.Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 10, 16, 14));

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
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
        Grid.SetRow(combo, 1);

        // Also allow free text entry for a field not in the loaded list.
        var freeText = new TextBox
        {
            PlaceholderText ="…or type a field name",
            Margin = new Thickness(16, 8, 16, 0),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(freeText, DialogChromeStyle);
        Grid.SetRow(freeText, 2);

        string? result = null;

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) =>
        {
            var typed = freeText.Text?.Trim();
            result = !string.IsNullOrWhiteSpace(typed)
                ? typed
                : combo.SelectedItem as string;
            dialog.Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 12, 16, 14));
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

    public static async Task<MailMergeRuleIfDialogResult?> AskMergeRuleIfAsync(
        Window owner,
        IReadOnlyList<string> fieldNames)
    {
        var dialog = CreateDialog("If...Then...Else", 380, 300);
        var fieldBox = CreateTextBox(fieldNames.FirstOrDefault() ?? string.Empty, "Field name");
        var opCombo = CreateOperatorCombo();
        var valueBox = CreateTextBox(string.Empty, "Comparison value");
        var trueBox = CreateTextBox(string.Empty, "Text if true");
        var falseBox = CreateTextBox(string.Empty, "Text if false");

        void RefreshValueEnabled()
        {
            var op = MailMergeRuleDialogPlanner.GetConditionOperator(opCombo.SelectedIndex);
            valueBox.IsEnabled = MailMergeRuleDialogPlanner.IsComparisonValueEnabled(op);
        }

        opCombo.SelectionChanged += (_, _) => RefreshValueEnabled();
        RefreshValueEnabled();

        MailMergeRuleIfDialogResult? result = null;
        var content = CreateForm(
            ("Field name:", (Control)fieldBox),
            ("Comparison:", opCombo),
            ("Compare to:", valueBox),
            ("Then insert:", trueBox),
            ("Otherwise insert:", falseBox));
        AddActions(dialog, content, () =>
        {
            result = MailMergeRuleDialogPlanner.CreateIfResult(
                fieldBox.Text,
                opCombo.SelectedIndex,
                valueBox.Text,
                trueBox.Text,
                falseBox.Text);
        });

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergeRuleConditionDialogResult?> AskMergeRuleConditionAsync(
        Window owner,
        IReadOnlyList<string> fieldNames,
        string title)
    {
        var dialog = CreateDialog(title, 360, 230);
        var fieldBox = CreateTextBox(fieldNames.FirstOrDefault() ?? string.Empty, "Field name");
        var opCombo = CreateOperatorCombo();
        var valueBox = CreateTextBox(string.Empty, "Comparison value");

        void RefreshValueEnabled()
        {
            var op = MailMergeRuleDialogPlanner.GetConditionOperator(opCombo.SelectedIndex);
            valueBox.IsEnabled = MailMergeRuleDialogPlanner.IsComparisonValueEnabled(op);
        }

        opCombo.SelectionChanged += (_, _) => RefreshValueEnabled();
        RefreshValueEnabled();

        MailMergeRuleConditionDialogResult? result = null;
        var content = CreateForm(
            ("Field name:", (Control)fieldBox),
            ("Comparison:", opCombo),
            ("Compare to:", valueBox));
        AddActions(dialog, content, () =>
        {
            result = MailMergeRuleDialogPlanner.CreateConditionResult(
                fieldBox.Text,
                opCombo.SelectedIndex,
                valueBox.Text);
        });

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> AskMergeRulePromptAsync(Window owner, string title, string prompt)
    {
        var dialog = CreateDialog(title, 340, 165);
        var valueBox = CreateTextBox(string.Empty, prompt);
        string? result = null;
        var content = CreateForm((prompt, (Control)valueBox));
        AddActions(dialog, content, () =>
        {
            result = string.IsNullOrWhiteSpace(valueBox.Text) ? null : valueBox.Text.Trim();
        });

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergeRuleNameValueDialogResult?> AskMergeRuleNameValueAsync(
        Window owner,
        string title,
        string valueLabel)
    {
        var dialog = CreateDialog(title, 360, 210);
        var nameBox = CreateTextBox(string.Empty, "Bookmark name");
        var valueBox = CreateTextBox(string.Empty, valueLabel);
        MailMergeRuleNameValueDialogResult? result = null;
        var content = CreateForm(
            ("Bookmark name:", (Control)nameBox),
            (valueLabel, (Control)valueBox));
        AddActions(dialog, content, () =>
        {
            result = string.IsNullOrWhiteSpace(nameBox.Text)
                ? null
                : MailMergeRuleDialogPlanner.CreateNameValueResult(nameBox.Text.Trim(), valueBox.Text);
        });

        await dialog.ShowDialog(owner);
        return result;
    }

    private static Window CreateDialog(string title, double width, double height) =>
        new()
        {
            Title = title,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

    private static TextBox CreateTextBox(string text, string placeholder)
    {
        var box = new TextBox
        {
            Text = text,
            PlaceholderText = placeholder,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static ComboBox CreateOperatorCombo()
    {
        var combo = new ComboBox
        {
            ItemsSource = MailMergeRuleDialogPlanner.GetConditionOperators()
                .Select(choice => choice.Label)
                .ToArray(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
        return combo;
    }

    private static Grid CreateForm(params (string Label, Control Control)[] rows)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("112,*"),
            RowDefinitions = new RowDefinitions(string.Join(",", rows.Select(_ => "Auto"))),
            Margin = new Thickness(16, 16, 16, 0),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var label = new TextBlock
            {
                Text = rows[i].Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8),
            };
            var control = rows[i].Control;
            control.Margin = new Thickness(0, 0, 0, 8);

            Grid.SetRow(label, i);
            Grid.SetColumn(label, 0);
            Grid.SetRow(control, i);
            Grid.SetColumn(control, 1);
            grid.Children.Add(label);
            grid.Children.Add(control);
        }

        return grid;
    }

    private static void AddActions(Window dialog, Control content, Action onOk)
    {
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) =>
        {
            onOk();
            dialog.Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => dialog.Close();

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 10, 16, 14));
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(content, 0);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(content);
        grid.Children.Add(buttons);
        dialog.Content = grid;
    }
}

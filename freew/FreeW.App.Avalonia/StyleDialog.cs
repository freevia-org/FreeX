using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class StyleDialog : FreeWDialogWindow
{
    private readonly IReadOnlyList<KeyValuePair<string, string>> _basedOnEntries;
    private readonly IReadOnlyList<KeyValuePair<string, string>> _nextEntries;
    private readonly TextBox _name = new() { MinWidth = 280 };
    private readonly ComboBox _basedOn = new() { MinWidth = 280 };
    private readonly ComboBox _nextStyle = new() { MinWidth = 280 };
    private readonly CheckBox _bold = new() { Content = "Bold", Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox _italic = new() { Content = "Italic", Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox _underline = new() { Content = "Underline" };
    private readonly ComboBox _size = new() { MinWidth = 100 };
    private readonly ComboBox _color = new() { MinWidth = 160 };
    private readonly ComboBox _alignment = new() { MinWidth = 160 };
    private readonly TextBlock _status = new() { Foreground = Brushes.Red, IsVisible = false };
    private readonly RunFormatting _seedRun;
    private readonly ParagraphFormatting _seedParagraph;

    private StyleDialog(
        string title,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? fixedName,
        string? defaultBasedOnId,
        RunFormatting seedRun,
        ParagraphFormatting seedParagraph,
        string? defaultNextStyleId)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _seedRun = seedRun;
        _seedParagraph = seedParagraph;
        _basedOnEntries = StyleDialogPlanner.BuildStyleOptions(styleNamesById, "(none)");
        _nextEntries = StyleDialogPlanner.BuildStyleOptions(styleNamesById, "(same style)");

        _name.Text = fixedName ?? string.Empty;
        _name.IsReadOnly = fixedName is not null;
        _basedOn.ItemsSource = _basedOnEntries.Select(e => e.Key).ToArray();
        _basedOn.SelectedIndex = IndexOfId(_basedOnEntries, defaultBasedOnId);
        _nextStyle.ItemsSource = _nextEntries.Select(e => e.Key).ToArray();
        _nextStyle.SelectedIndex = IndexOfId(_nextEntries, defaultNextStyleId);
        _bold.IsChecked = seedRun.Bold;
        _italic.IsChecked = seedRun.Italic;
        _underline.IsChecked = seedRun.Underline;
        _size.ItemsSource = StyleDialogPlanner.FontSizes.Select(s => s.Label).ToArray();
        _size.SelectedIndex = StyleDialogPlanner.IndexOfSize(seedRun.FontSizePt);
        _color.ItemsSource = StyleDialogPlanner.Colors.Select(c => c.Label).ToArray();
        _color.SelectedIndex = StyleDialogPlanner.IndexOfColor(seedRun.ColorHex);
        _alignment.ItemsSource = StyleDialogPlanner.AlignmentLabels.ToArray();
        _alignment.SelectedIndex = (int)seedParagraph.Alignment;

        var effects = new StackPanel { Orientation = Orientation.Horizontal };
        effects.Children.Add(_bold);
        effects.Children.Add(_italic);
        effects.Children.Add(_underline);

        var panel = new StackPanel { Margin = new Thickness(16) };
        AddRow(panel, "Name:", _name);
        AddRow(panel, "Style based on:", _basedOn);
        AddRow(panel, "Style for following paragraph:", _nextStyle);
        AddRow(panel, "Formatting:", effects);
        AddRow(panel, "Font size:", _size);
        AddRow(panel, "Text color:", _color);
        AddRow(panel, "Alignment:", _alignment);
        panel.Children.Add(_status);

        var ok = Button("OK", (_, _) => Accept());
        ok.IsDefault = true;
        var cancel = Button("Cancel", (_, _) => Close(null));
        cancel.IsCancel = true;
        panel.Children.Add(ButtonRow(ok, cancel));
        Content = panel;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
    }

    public static Task<StyleDefinitionResult?> AskNewAsync(
        Window owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? defaultBasedOnId) =>
        new StyleDialog("New Style", styleNamesById, fixedName: null, defaultBasedOnId,
            RunFormatting.Default, ParagraphFormatting.Default, defaultNextStyleId: null)
            .ShowDialog<StyleDefinitionResult?>(owner);

    public static Task<StyleDefinitionResult?> AskModifyAsync(
        Window owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        DocumentStyle existing) =>
        new StyleDialog($"Modify Style - {existing.Name}", styleNamesById, fixedName: existing.Name,
            existing.BasedOnStyleId, existing.Run, existing.Paragraph, existing.NextStyleId)
            .ShowDialog<StyleDefinitionResult?>(owner);

    public static async Task ShowNewAndApplyAsync(Window owner, DocumentView editor)
    {
        var definition = await AskNewAsync(owner, StyleNamesById(editor.Document), editor.CurrentParagraphStyleId);
        if (definition is null)
            return;

        editor.CreateParagraphStyleAndApply(
            definition.Name,
            definition.BasedOnId,
            definition.Run,
            definition.Paragraph,
            definition.NextStyleId);
        editor.Focus();
    }

    internal static IReadOnlyDictionary<string, string> StyleNamesById(TextDocument document) =>
        document.Styles.ToDictionary(kv => kv.Key, kv => kv.Value.Name, StringComparer.Ordinal);

    private void Accept()
    {
        var input = new StyleDialogInput(
            _name.Text,
            SelectedId(_basedOnEntries, _basedOn.SelectedIndex),
            SelectedId(_nextEntries, _nextStyle.SelectedIndex),
            _bold.IsChecked == true,
            _italic.IsChecked == true,
            _underline.IsChecked == true,
            _size.SelectedIndex,
            _color.SelectedIndex,
            _alignment.SelectedIndex);

        if (!StyleDialogPlanner.TryBuildDefinition(
                input,
                _seedRun,
                _seedParagraph,
                out var result,
                out var validation))
        {
            ShowStatus(StyleDialogPlanner.ValidationMessageFor(validation));
            return;
        }

        Close(result);
    }

    private void ShowStatus(string message)
    {
        _status.Text = message;
        _status.IsVisible = true;
    }

    private static string? SelectedId(IReadOnlyList<KeyValuePair<string, string>> entries, int index) =>
        index > 0 && index < entries.Count ? entries[index].Value : null;

    private static int IndexOfId(IReadOnlyList<KeyValuePair<string, string>> entries, string? id)
    {
        if (string.IsNullOrEmpty(id))
            return 0;
        for (var i = 1; i < entries.Count; i++)
        {
            if (entries[i].Value == id)
                return i;
        }
        return 0;
    }

    private static void AddRow(Panel panel, string label, Control field)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
        field.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(field);
    }

    private static StackPanel ButtonRow(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        foreach (var button in buttons)
            row.Children.Add(button);
        return row;
    }

    private static Button Button(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button { Content = text, MinWidth = 76, Margin = new Thickness(6, 0, 0, 0) };
        button.Click += click;
        return button;
    }
}

internal sealed class ManageStylesDialog : FreeWDialogWindow
{
    private readonly TextDocument _document;
    private readonly ListBox _styles = new() { MinHeight = 220, MinWidth = 320 };
    private readonly ComboBox _sortOrder = new() { MinWidth = 180 };
    private readonly Button _apply;
    private readonly Button _modify;
    private readonly Button _delete;
    private readonly List<StyleDialogRow> _rows = [];

    private ManageStylesDialog(TextDocument document, string? preselectStyleId)
    {
        _document = document;
        Title = "Manage Styles";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _sortOrder.ItemsSource = new[] { "Alphabetical", "By type (built-ins first)", "By use (most-used first)" };
        _sortOrder.SelectedIndex = 0;
        _sortOrder.SelectionChanged += (_, _) => RebuildList(SelectedOrder(), SelectedRow()?.Id ?? preselectStyleId);
        _styles.SelectionChanged += (_, _) => SyncButtons();

        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        sortRow.Children.Add(new TextBlock
        {
            Text = "Sort:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        sortRow.Children.Add(_sortOrder);

        var listPane = new StackPanel();
        listPane.Children.Add(sortRow);
        listPane.Children.Add(_styles);

        _apply = Button("Apply", (_, _) =>
        {
            if (SelectedRow() is { } row)
                Close(new ManageStyleAction.Apply(row.Id));
        });
        _apply.IsDefault = true;
        _modify = Button("Modify...", (_, _) =>
        {
            if (SelectedRow() is { } row)
                Close(new ManageStyleAction.Modify(row.Id));
        });
        _delete = Button("Delete", (_, _) =>
        {
            if (SelectedRow() is { IsBuiltIn: false } row)
                Close(new ManageStyleAction.Delete(row.Id));
        });
        var close = Button("Close", (_, _) => Close(null));
        close.IsCancel = true;

        var buttonPane = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 0, 0, 0) };
        buttonPane.Children.Add(_apply);
        buttonPane.Children.Add(_modify);
        buttonPane.Children.Add(_delete);
        buttonPane.Children.Add(close);

        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        body.Children.Add(listPane);
        body.Children.Add(buttonPane);
        Content = body;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };

        RebuildList(StyleDialogSortOrder.Alphabetical, preselectStyleId);
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        while (true)
        {
            var action = await new ManageStylesDialog(editor.Document, editor.CurrentParagraphStyleId)
                .ShowDialog<ManageStyleAction?>(owner);
            if (action is null)
                return;

            switch (action)
            {
                case ManageStyleAction.Apply apply:
                    editor.ApplyNamedStyle(apply.StyleId);
                    editor.Focus();
                    return;

                case ManageStyleAction.Delete delete:
                    editor.DeleteParagraphStyle(delete.StyleId);
                    continue;

                case ManageStyleAction.Modify modify:
                    if (!editor.Document.Styles.TryGetValue(modify.StyleId, out var existing))
                        continue;
                    var definition = await StyleDialog.AskModifyAsync(owner, StyleDialog.StyleNamesById(editor.Document), existing);
                    if (definition is null)
                        continue;
                    editor.ModifyParagraphStyle(
                        modify.StyleId,
                        definition.Run,
                        definition.Paragraph,
                        definition.BasedOnId,
                        definition.NextStyleId);
                    continue;
            }
        }
    }

    internal static IReadOnlyList<StyleDialogRow> BuildRows(TextDocument document, StyleDialogSortOrder order) =>
        StyleDialogPlanner.BuildRows(document, order);

    private void RebuildList(StyleDialogSortOrder order, string? selectedStyleId)
    {
        _rows.Clear();
        _rows.AddRange(BuildRows(_document, order));
        _styles.ItemsSource = _rows.Select(row => row.Display).ToArray();

        var index = _rows.FindIndex(row => row.Id == selectedStyleId);
        _styles.SelectedIndex = index >= 0 ? index : (_rows.Count > 0 ? 0 : -1);
        SyncButtons();
    }

    private StyleDialogSortOrder SelectedOrder() => _sortOrder.SelectedIndex switch
    {
        1 => StyleDialogSortOrder.ByType,
        2 => StyleDialogSortOrder.ByUse,
        _ => StyleDialogSortOrder.Alphabetical,
    };

    private StyleDialogRow? SelectedRow() =>
        _styles.SelectedIndex >= 0 && _styles.SelectedIndex < _rows.Count ? _rows[_styles.SelectedIndex] : null;

    private void SyncButtons()
    {
        var row = SelectedRow();
        _apply.IsEnabled = row is not null;
        _modify.IsEnabled = row is not null;
        _delete.IsEnabled = row is { IsBuiltIn: false };
    }

    private static Button Button(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button { Content = text, MinWidth = 86, Margin = new Thickness(0, 0, 0, 8) };
        button.Click += click;
        return button;
    }
}

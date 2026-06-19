using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// The values captured by <see cref="StyleDialog"/>: a style name, a based-on style id (or null), and
/// the run/paragraph formatting the style carries. These feed <see cref="StyleManager.CreateStyle"/>
/// (New Style) or <see cref="StyleManager.ModifyStyle"/> (Modify Style).
/// </summary>
internal sealed record StyleDefinition(
    string Name,
    string? BasedOnId,
    RunFormatting Run,
    ParagraphFormatting Paragraph);

/// <summary>
/// A small modal form that captures (or edits) a custom paragraph style: name, a few formatting options
/// (bold / italic / underline, size, colour, alignment) and a based-on style. It is intentionally
/// pragmatic — the heavy lifting (id generation, catalog mutation, the built-in guard) lives in the pure
/// <see cref="StyleManager"/>; this is just the WPF surface that gathers a <see cref="StyleDefinition"/>.
/// </summary>
internal static class StyleDialog
{
    // A small named colour palette mirroring the Home > Font text-colour picker. The empty hex means
    // "Automatic" (no explicit colour on the style's run).
    private static readonly (string Label, string? Hex)[] Colors =
    [
        ("Automatic", null),
        ("Black", "#000000"),
        ("Dark Red", "#C00000"),
        ("Red", "#FF0000"),
        ("Blue accent", "#2F5496"),
        ("Blue", "#0070C0"),
        ("Green", "#00B050"),
        ("Purple", "#7030A0"),
        ("Grey", "#7F7F7F"),
    ];

    private static readonly (string Label, double Size)[] Sizes =
    [
        ("(default)", 0), ("8", 8), ("9", 9), ("10", 10), ("11", 11), ("12", 12),
        ("14", 14), ("16", 16), ("18", 18), ("24", 24), ("28", 28), ("36", 36),
    ];

    /// <summary>
    /// Show the New Style dialog. <paramref name="styleNamesById"/> is the document's catalog used to
    /// populate the Based On dropdown; <paramref name="defaultBasedOnId"/> pre-selects an entry (e.g. the
    /// caret paragraph's current style). Returns the captured definition, or null if cancelled.
    /// </summary>
    public static StyleDefinition? AskNew(
        Window? owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? defaultBasedOnId) =>
        Show(owner, "New Style", styleNamesById, fixedName: null, defaultBasedOnId,
            RunFormatting.Default, ParagraphFormatting.Default, isModify: false);

    /// <summary>
    /// Show the Modify Style dialog seeded with an existing style's name/based-on/formatting. The name is
    /// shown read-only (the style id is stable). Returns the edited definition, or null if cancelled.
    /// </summary>
    public static StyleDefinition? AskModify(
        Window? owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        DocumentStyle existing) =>
        Show(owner, $"Modify Style — {existing.Name}", styleNamesById, fixedName: existing.Name,
            existing.BasedOnStyleId, existing.Run, existing.Paragraph, isModify: true);

    private static StyleDefinition? Show(
        Window? owner,
        string title,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? fixedName,
        string? defaultBasedOnId,
        RunFormatting seedRun,
        ParagraphFormatting seedPara,
        bool isModify)
    {
        StyleDefinition? result = null;

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var name = new TextBox { MinWidth = 280, Text = fixedName ?? string.Empty };
        if (isModify)
            name.IsReadOnly = true;

        var basedOn = new ComboBox { MinWidth = 280 };
        var basedOnEntries = new List<KeyValuePair<string, string>> { new("(none)", string.Empty) };
        basedOnEntries.AddRange(styleNamesById
            .OrderBy(kv => kv.Value, System.StringComparer.OrdinalIgnoreCase)
            .Select(kv => new KeyValuePair<string, string>(kv.Value, kv.Key)));
        basedOn.ItemsSource = basedOnEntries;
        basedOn.DisplayMemberPath = "Key";
        basedOn.SelectedIndex = 0;
        if (defaultBasedOnId is { Length: > 0 })
        {
            var match = basedOnEntries.FindIndex(kv => kv.Value == defaultBasedOnId);
            if (match >= 0)
                basedOn.SelectedIndex = match;
        }

        var bold = new CheckBox { Content = "Bold", IsChecked = seedRun.Bold, Margin = new Thickness(0, 0, 12, 0) };
        var italic = new CheckBox { Content = "Italic", IsChecked = seedRun.Italic, Margin = new Thickness(0, 0, 12, 0) };
        var underline = new CheckBox { Content = "Underline", IsChecked = seedRun.Underline };
        var effects = new StackPanel { Orientation = Orientation.Horizontal };
        effects.Children.Add(bold);
        effects.Children.Add(italic);
        effects.Children.Add(underline);

        var size = new ComboBox { MinWidth = 100 };
        size.ItemsSource = Sizes.Select(s => s.Label).ToList();
        size.SelectedIndex = IndexOfSize(seedRun.FontSizePt);

        var color = new ComboBox { MinWidth = 160 };
        color.ItemsSource = Colors.Select(c => c.Label).ToList();
        color.SelectedIndex = IndexOfColor(seedRun.ColorHex);

        var alignment = new ComboBox { MinWidth = 160 };
        var alignLabels = new[] { "Left", "Center", "Right", "Justify" };
        alignment.ItemsSource = alignLabels;
        alignment.SelectedIndex = (int)seedPara.Alignment;

        void Accept()
        {
            var styleName = name.Text.Trim();
            if (styleName.Length == 0)
            {
                DialogMessageHelper.ShowWarning(dialog, "Please enter a style name.", "New Style");
                return;
            }

            var run = seedRun with
            {
                Bold = bold.IsChecked == true,
                Italic = italic.IsChecked == true,
                Underline = underline.IsChecked == true,
                FontSizePt = Sizes[Math.Max(0, size.SelectedIndex)].Size is var pt && pt > 0 ? pt : null,
                ColorHex = Colors[Math.Max(0, color.SelectedIndex)].Hex,
            };
            var para = seedPara with
            {
                Alignment = (FreeW.Core.Model.TextAlignment)Math.Max(0, alignment.SelectedIndex),
            };
            var chosenBasedOn = (basedOn.SelectedItem is KeyValuePair<string, string> kv && kv.Value.Length > 0)
                ? kv.Value
                : null;

            result = new StyleDefinition(styleName, chosenBasedOn, run, para);
            dialog.DialogResult = true;
        }

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(16) };
        AddRow(panel, "Name:", name);
        AddRow(panel, "Style based on:", basedOn);
        AddRow(panel, "Formatting:", effects);
        AddRow(panel, "Font size:", size);
        AddRow(panel, "Text colour:", color);
        AddRow(panel, "Alignment:", alignment);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        if (isModify)
            basedOn.Focus();
        else
            name.Focus();

        return dialog.ShowDialog() == true ? result : null;
    }

    private static int IndexOfSize(double? sizePt)
    {
        if (sizePt is not { } pt)
            return 0;
        for (var i = 0; i < Sizes.Length; i++)
        {
            if (Math.Abs(Sizes[i].Size - pt) < 0.01)
                return i;
        }
        return 0;
    }

    private static int IndexOfColor(string? hex)
    {
        if (hex is null)
            return 0;
        for (var i = 0; i < Colors.Length; i++)
        {
            if (string.Equals(Colors[i].Hex, hex, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    private static void AddRow(Panel panel, string label, UIElement field)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 2),
        });
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(field);
    }
}

/// <summary>
/// The outcome of the <see cref="ManageStylesDialog"/>: apply / modify / delete the selected style.
/// A null result (dialog closed without a button) means "do nothing".
/// </summary>
internal abstract record ManageStyleAction
{
    public sealed record Apply(string StyleId) : ManageStyleAction;
    public sealed record Modify(string StyleId) : ManageStyleAction;
    public sealed record Delete(string StyleId) : ManageStyleAction;
}

/// <summary>
/// A pragmatic Manage Styles dialog: a list of the document's styles (built-ins flagged) with Apply,
/// Modify and Delete buttons. Delete is disabled for built-in styles (the pure <see cref="StyleManager"/>
/// also refuses them, so this is just UI affordance). Returns the chosen action, or null if cancelled.
/// </summary>
internal static class ManageStylesDialog
{
    private sealed record StyleRow(string Id, string Display, bool IsBuiltIn);

    public static ManageStyleAction? Ask(Window? owner, TextDocument model, string? preselectStyleId)
    {
        ManageStyleAction? result = null;

        var dialog = new Window
        {
            Title = "Manage Styles",
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var rows = model.Styles.Values
            .Select(s => new StyleRow(s.Id, StyleManager.IsBuiltIn(s.Id) ? $"{s.Name}  (built-in)" : s.Name, StyleManager.IsBuiltIn(s.Id)))
            .OrderBy(r => r.Display, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        var list = new ListBox { MinWidth = 320, MinHeight = 220 };
        list.ItemsSource = rows;
        list.DisplayMemberPath = nameof(StyleRow.Display);
        var preselect = rows.FindIndex(r => r.Id == preselectStyleId);
        list.SelectedIndex = preselect >= 0 ? preselect : 0;

        var apply = new Button { Content = "Apply", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        var modify = new Button { Content = "Modify…", MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        var delete = new Button { Content = "Delete", MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };

        void SyncButtons()
        {
            var row = list.SelectedItem as StyleRow;
            var hasSelection = row is not null;
            apply.IsEnabled = hasSelection;
            modify.IsEnabled = hasSelection;
            delete.IsEnabled = hasSelection && row is { IsBuiltIn: false };
        }

        list.SelectionChanged += (_, _) => SyncButtons();
        SyncButtons();

        apply.Click += (_, _) =>
        {
            if (list.SelectedItem is StyleRow row)
            {
                result = new ManageStyleAction.Apply(row.Id);
                dialog.DialogResult = true;
            }
        };
        modify.Click += (_, _) =>
        {
            if (list.SelectedItem is StyleRow row)
            {
                result = new ManageStyleAction.Modify(row.Id);
                dialog.DialogResult = true;
            }
        };
        delete.Click += (_, _) =>
        {
            if (list.SelectedItem is StyleRow { IsBuiltIn: false } row)
            {
                result = new ManageStyleAction.Delete(row.Id);
                dialog.DialogResult = true;
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 0, 0, 0) };
        buttons.Children.Add(apply);
        buttons.Children.Add(modify);
        buttons.Children.Add(delete);
        buttons.Children.Add(close);

        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        body.Children.Add(list);
        body.Children.Add(buttons);
        dialog.Content = body;

        list.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }
}

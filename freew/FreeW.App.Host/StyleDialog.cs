using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A small modal form that captures or edits a custom paragraph style and delegates shared
/// validation and option planning to <see cref="StyleDialogPlanner"/>.
/// </summary>
internal static class StyleDialog
{
    /// <summary>
    /// Show the New Style dialog. <paramref name="styleNamesById"/> is the document's catalog used to
    /// populate the Based On dropdown; <paramref name="defaultBasedOnId"/> pre-selects an entry (e.g. the
    /// caret paragraph's current style). Returns the captured definition, or null if cancelled.
    /// </summary>
    public static StyleDefinitionResult? AskNew(
        Window? owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? defaultBasedOnId) =>
        Show(owner, "New Style", styleNamesById, fixedName: null, defaultBasedOnId,
            RunFormatting.Default, ParagraphFormatting.Default, defaultNextStyleId: null, isModify: false);

    /// <summary>
    /// Show the Modify Style dialog seeded with an existing style's name/based-on/formatting. The name is
    /// shown read-only (the style id is stable). Returns the edited definition, or null if cancelled.
    /// </summary>
    public static StyleDefinitionResult? AskModify(
        Window? owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        DocumentStyle existing) =>
        Show(owner, $"Modify Style — {existing.Name}", styleNamesById, fixedName: existing.Name,
            existing.BasedOnStyleId, existing.Run, existing.Paragraph, existing.NextStyleId, isModify: true);

    private static StyleDefinitionResult? Show(
        Window? owner,
        string title,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? fixedName,
        string? defaultBasedOnId,
        RunFormatting seedRun,
        ParagraphFormatting seedPara,
        string? defaultNextStyleId,
        bool isModify)
    {
        StyleDefinitionResult? result = null;

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var name = new TextBox
        {
            MinWidth = 280,
            Height = StyleDialogMetrics.NameTextBoxHeight,
            MinHeight = StyleDialogMetrics.NameTextBoxHeight,
            MaxHeight = StyleDialogMetrics.NameTextBoxHeight,
            Text = fixedName ?? string.Empty,
        };
        if (isModify)
            name.IsReadOnly = true;

        var basedOn = new ComboBox { MinWidth = 280 };
        var basedOnEntries = StyleDialogPlanner.BuildStyleOptions(styleNamesById, "(none)").ToList();
        basedOn.ItemsSource = basedOnEntries;
        basedOn.DisplayMemberPath = "Key";
        basedOn.SelectedIndex = 0;
        if (defaultBasedOnId is { Length: > 0 })
        {
            var match = basedOnEntries.FindIndex(kv => kv.Value == defaultBasedOnId);
            if (match >= 0)
                basedOn.SelectedIndex = match;
        }

        // "Style for following paragraph" (Word's w:next): the style the next paragraph takes when Enter is
        // pressed at the end of one carrying this style. "(same style)" maps to null (keep this style).
        var nextStyle = new ComboBox { MinWidth = 280 };
        var nextEntries = StyleDialogPlanner.BuildStyleOptions(styleNamesById, "(same style)").ToList();
        nextStyle.ItemsSource = nextEntries;
        nextStyle.DisplayMemberPath = "Key";
        nextStyle.SelectedIndex = 0;
        if (defaultNextStyleId is { Length: > 0 })
        {
            var match = nextEntries.FindIndex(kv => kv.Value == defaultNextStyleId);
            if (match >= 0)
                nextStyle.SelectedIndex = match;
        }

        var bold = new CheckBox { Content = "Bold", IsChecked = seedRun.Bold, Margin = new Thickness(0, 0, 12, 0) };
        var italic = new CheckBox { Content = "Italic", IsChecked = seedRun.Italic, Margin = new Thickness(0, 0, 12, 0) };
        var underline = new CheckBox { Content = "Underline", IsChecked = seedRun.Underline };
        var effects = new StackPanel { Orientation = Orientation.Horizontal };
        effects.Children.Add(bold);
        effects.Children.Add(italic);
        effects.Children.Add(underline);

        var size = new ComboBox { MinWidth = 100 };
        size.ItemsSource = StyleDialogPlanner.FontSizes.Select(s => s.Label).ToList();
        size.SelectedIndex = StyleDialogPlanner.IndexOfSize(seedRun.FontSizePt);

        var color = new ComboBox { MinWidth = 160 };
        color.ItemsSource = StyleDialogPlanner.Colors.Select(c => c.Label).ToList();
        color.SelectedIndex = StyleDialogPlanner.IndexOfColor(seedRun.ColorHex);

        var alignment = new ComboBox { MinWidth = 160 };
        alignment.ItemsSource = StyleDialogPlanner.AlignmentLabels.ToList();
        alignment.SelectedIndex = (int)seedPara.Alignment;

        void Accept()
        {
            var input = new StyleDialogInput(
                name.Text,
                SelectedId(basedOn.SelectedItem),
                SelectedId(nextStyle.SelectedItem),
                bold.IsChecked == true,
                italic.IsChecked == true,
                underline.IsChecked == true,
                size.SelectedIndex,
                color.SelectedIndex,
                alignment.SelectedIndex);

            if (!StyleDialogPlanner.TryBuildDefinition(input, seedRun, seedPara, out result, out var validation))
            {
                DialogMessageHelper.ShowWarning(dialog, StyleDialogPlanner.ValidationMessageFor(validation), "New Style");
                return;
            }

            dialog.DialogResult = true;
        }

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 72,
            rowMargin: new Thickness(0, StyleDialogMetrics.ActionRowTopMargin, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(StyleDialogMetrics.DialogMargin) };
        AddRow(panel, "Name:", name);
        AddRow(panel, "Style based on:", basedOn);
        AddRow(panel, "Style for following paragraph:", nextStyle);
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

    private static string? SelectedId(object? selectedItem) =>
        selectedItem is KeyValuePair<string, string> { Value.Length: > 0 } kv ? kv.Value : null;

    private static void AddRow(Panel panel, string label, UIElement field)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 2),
        });
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 0, 0, StyleDialogMetrics.FieldBottomMargin);
        panel.Children.Add(field);
    }
}
/// <summary>
/// A Manage Styles dialog: a list of document styles with Apply, Modify, Delete, and sort-order controls.
/// </summary>
internal static class ManageStylesDialog
{
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

        // Sort order picker - Alphabetical / By Type / By Use.
        var sortOrderBox = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 0, 8) };
        sortOrderBox.Items.Add("Alphabetical");
        sortOrderBox.Items.Add("By type (built-ins first)");
        sortOrderBox.Items.Add("By use (most-used first)");
        sortOrderBox.SelectedIndex = 0;

        var list = new ListBox { MinWidth = 320, MinHeight = 220 };

        // Rebuild the list whenever the sort order changes.
        void RebuildList(StyleDialogSortOrder order)
        {
            var currentId = (list.SelectedItem as StyleDialogRow)?.Id ?? preselectStyleId;

            var rows = BuildRows(model, order);
            list.ItemsSource = rows;
            list.DisplayMemberPath = nameof(StyleDialogRow.Display);

            var preselect = rows.FindIndex(r => r.Id == currentId);
            list.SelectedIndex = preselect >= 0 ? preselect : 0;
        }

        sortOrderBox.SelectionChanged += (_, _) =>
        {
            var order = sortOrderBox.SelectedIndex switch
            {
                1 => StyleDialogSortOrder.ByType,
                2 => StyleDialogSortOrder.ByUse,
                _ => StyleDialogSortOrder.Alphabetical,
            };
            RebuildList(order);
        };

        RebuildList(StyleDialogSortOrder.Alphabetical);

        var apply  = new Button { Content = "Apply",   IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        var modify = new Button { Content = "Modify…",               MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        var delete = new Button { Content = "Delete",                MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        var close  = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };

        void SyncButtons()
        {
            var row = list.SelectedItem as StyleDialogRow;
            var hasSelection = row is not null;
            apply.IsEnabled  = hasSelection;
            modify.IsEnabled = hasSelection;
            delete.IsEnabled = hasSelection && row is { IsBuiltIn: false };
        }

        list.SelectionChanged += (_, _) => SyncButtons();
        SyncButtons();

        apply.Click += (_, _) =>
        {
            if (list.SelectedItem is StyleDialogRow row)
            {
                result = new ManageStyleAction.Apply(row.Id);
                dialog.DialogResult = true;
            }
        };
        modify.Click += (_, _) =>
        {
            if (list.SelectedItem is StyleDialogRow row)
            {
                result = new ManageStyleAction.Modify(row.Id);
                dialog.DialogResult = true;
            }
        };
        delete.Click += (_, _) =>
        {
            if (list.SelectedItem is StyleDialogRow { IsBuiltIn: false } row)
            {
                result = new ManageStyleAction.Delete(row.Id);
                dialog.DialogResult = true;
            }
        };

        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        sortRow.Children.Add(new TextBlock
        {
            Text = "Sort:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        sortRow.Children.Add(sortOrderBox);

        var listPane = new StackPanel();
        listPane.Children.Add(sortRow);
        listPane.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 0, 0, 0) };
        buttons.Children.Add(apply);
        buttons.Children.Add(modify);
        buttons.Children.Add(delete);
        buttons.Children.Add(close);

        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        body.Children.Add(listPane);
        body.Children.Add(buttons);
        dialog.Content = body;

        list.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }

    private static List<StyleDialogRow> BuildRows(TextDocument model, StyleDialogSortOrder order) =>
        StyleDialogPlanner.BuildRows(model, order).ToList();
}

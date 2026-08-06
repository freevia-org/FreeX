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
        Show(owner, StyleDialogPlanner.CreateNewSession(styleNamesById, defaultBasedOnId));

    /// <summary>
    /// Show the Modify Style dialog seeded with an existing style's name/based-on/formatting. The name is
    /// shown read-only (the style id is stable). Returns the edited definition, or null if cancelled.
    /// </summary>
    public static StyleDefinitionResult? AskModify(
        Window? owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        DocumentStyle existing) =>
        Show(owner, StyleDialogPlanner.CreateModifySession(styleNamesById, existing));

    private static StyleDefinitionResult? Show(
        Window? owner,
        StyleDialogSession session)
    {
        StyleDefinitionResult? result = null;
        var state = session.InitialState;
        var text = StyleDialogPlanner.Text;

        var dialog = new Window
        {
            Title = state.Title,
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
            Text = state.Name,
            IsReadOnly = state.NameIsReadOnly,
        };

        var basedOn = new ComboBox { MinWidth = 280, Height = StyleDialogMetrics.ComboBoxHeight };
        basedOn.ItemsSource = state.BasedOnOptions;
        basedOn.DisplayMemberPath = "Key";
        basedOn.SelectedIndex = state.BasedOnIndex;

        // "Style for following paragraph" (Word's w:next): the style the next paragraph takes when Enter is
        // pressed at the end of one carrying this style. "(same style)" maps to null (keep this style).
        var nextStyle = new ComboBox { MinWidth = 280, Height = StyleDialogMetrics.ComboBoxHeight };
        nextStyle.ItemsSource = state.NextStyleOptions;
        nextStyle.DisplayMemberPath = "Key";
        nextStyle.SelectedIndex = state.NextStyleIndex;

        var bold = new CheckBox { Content = "Bold", IsChecked = state.Bold, Height = StyleDialogMetrics.CheckBoxHeight, Margin = new Thickness(0, 0, 12, 0) };
        var italic = new CheckBox { Content = "Italic", IsChecked = state.Italic, Height = StyleDialogMetrics.CheckBoxHeight, Margin = new Thickness(0, 0, 12, 0) };
        var underline = new CheckBox { Content = "Underline", IsChecked = state.Underline, Height = StyleDialogMetrics.CheckBoxHeight };
        var effects = new StackPanel { Orientation = Orientation.Horizontal };
        effects.Children.Add(bold);
        effects.Children.Add(italic);
        effects.Children.Add(underline);

        var size = new ComboBox { MinWidth = 100, Height = StyleDialogMetrics.ComboBoxHeight };
        size.ItemsSource = StyleDialogPlanner.FontSizes.Select(s => s.Label).ToList();
        size.SelectedIndex = state.FontSizeIndex;

        var color = new ComboBox { MinWidth = 160, Height = StyleDialogMetrics.ComboBoxHeight };
        color.ItemsSource = StyleDialogPlanner.Colors.Select(c => c.Label).ToList();
        color.SelectedIndex = state.ColorIndex;

        var alignment = new ComboBox { MinWidth = 160, Height = StyleDialogMetrics.ComboBoxHeight };
        alignment.ItemsSource = StyleDialogPlanner.AlignmentLabels.ToList();
        alignment.SelectedIndex = state.AlignmentIndex;

        void Accept()
        {
            var acceptance = session.PlanAcceptance(new StyleDialogControlState(
                name.Text,
                basedOn.SelectedIndex,
                nextStyle.SelectedIndex,
                bold.IsChecked == true,
                italic.IsChecked == true,
                underline.IsChecked == true,
                size.SelectedIndex,
                color.SelectedIndex,
                alignment.SelectedIndex));

            if (!acceptance.IsAccepted)
            {
                DialogMessageHelper.ShowWarning(
                    dialog,
                    acceptance.ErrorMessage ?? string.Empty,
                    session.ValidationTitle);
                if (acceptance.FocusField == StyleDialogField.Name)
                    name.Focus();
                return;
            }

            result = acceptance.Result;
            dialog.DialogResult = true;
        }

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 72,
            rowMargin: new Thickness(0, StyleDialogMetrics.ActionRowTopMargin, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(StyleDialogMetrics.DialogMargin) };
        AddRow(panel, text.NameLabel, name);
        AddRow(panel, text.BasedOnLabel, basedOn);
        AddRow(panel, text.NextStyleLabel, nextStyle);
        AddRow(panel, text.FormattingLabel, effects);
        AddRow(panel, text.FontSizeLabel, size);
        AddRow(panel, text.TextColorLabel, color);
        AddRow(panel, text.AlignmentLabel, alignment);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        if (state.InitialFocus == StyleDialogFocusTarget.BasedOn)
            basedOn.Focus();
        else
            name.Focus();

        return dialog.ShowDialog() == true ? result : null;
    }

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
        var session = StyleDialogPlanner.CreateManageStylesSession(model, preselectStyleId);
        var text = StyleDialogPlanner.Text;

        var dialog = new Window
        {
            Title = text.ManageTitle,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        // Sort order picker - Alphabetical / By Type / By Use.
        var sortOrderBox = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var label in StyleDialogPlanner.ManageStyleSortLabels)
            sortOrderBox.Items.Add(label);
        sortOrderBox.SelectedIndex = session.State.SortIndex;

        var list = new ListBox { MinWidth = 320, MinHeight = 220 };

        // Rebuild the list whenever the sort order changes.
        void RebuildList(int sortIndex)
        {
            var state = session.PlanSort(sortIndex);
            list.ItemsSource = state.Rows;
            list.DisplayMemberPath = nameof(StyleDialogRow.Display);
            list.SelectedIndex = state.SelectedIndex;
        }

        sortOrderBox.SelectionChanged += (_, _) => RebuildList(sortOrderBox.SelectedIndex);

        RebuildList(sortOrderBox.SelectedIndex);

        var apply  = new Button { Content = text.ApplyLabel,   IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        var modify = new Button { Content = text.ModifyLabel,               MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        var delete = new Button { Content = text.DeleteLabel,                MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        var close  = new Button { Content = text.CloseLabel, IsCancel = true, MinWidth = 80 };

        void SyncButtons()
        {
            var buttons = session.SelectRow(list.SelectedIndex).Buttons;
            apply.IsEnabled = buttons.ApplyEnabled;
            modify.IsEnabled = buttons.ModifyEnabled;
            delete.IsEnabled = buttons.DeleteEnabled;
        }

        list.SelectionChanged += (_, _) => SyncButtons();
        SyncButtons();

        apply.Click += (_, _) =>
        {
            if (session.PlanAction(ManageStyleActionKind.Apply, list.SelectedIndex) is { } action)
            {
                result = action;
                dialog.DialogResult = true;
            }
        };
        modify.Click += (_, _) =>
        {
            if (session.PlanAction(ManageStyleActionKind.Modify, list.SelectedIndex) is { } action)
            {
                result = action;
                dialog.DialogResult = true;
            }
        };
        delete.Click += (_, _) =>
        {
            if (session.PlanAction(ManageStyleActionKind.Delete, list.SelectedIndex) is { } action)
            {
                result = action;
                dialog.DialogResult = true;
            }
        };

        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        sortRow.Children.Add(new TextBlock
        {
            Text = text.SortLabel,
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
}

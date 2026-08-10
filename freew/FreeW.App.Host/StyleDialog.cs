using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
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

    public static StyleDefinitionResult? AskNew(
        Window? owner,
        TextDocument document,
        string? defaultBasedOnId) =>
        Show(owner, StyleDialogPlanner.CreateNewSession(document, defaultBasedOnId));

    /// <summary>
    /// Show the Modify Style dialog seeded with an existing style's name/based-on/formatting. The name is
    /// shown read-only (the style id is stable). Returns the edited definition, or null if cancelled.
    /// </summary>
    public static StyleDefinitionResult? AskModify(
        Window? owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        DocumentStyle existing) =>
        Show(owner, StyleDialogPlanner.CreateModifySession(styleNamesById, existing));

    public static StyleDefinitionResult? AskModify(
        Window? owner,
        TextDocument document,
        DocumentStyle existing) =>
        Show(owner, StyleDialogPlanner.CreateModifySession(document, existing));

    private static StyleDefinitionResult? Show(
        Window? owner,
        StyleDialogSession session)
    {
        StyleDefinitionResult? result = null;
        var state = session.InitialState;
        var surface = StyleDialogPlanner.Surface;

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
            MinWidth = surface.Field(StyleDialogFieldKind.Name).MinWidth,
            Height = StyleDialogMetrics.NameTextBoxHeight,
            MinHeight = StyleDialogMetrics.NameTextBoxHeight,
            MaxHeight = StyleDialogMetrics.NameTextBoxHeight,
            Text = state.Name,
            IsReadOnly = state.NameIsReadOnly,
        };

        var basedOn = new ComboBox { MinWidth = surface.Field(StyleDialogFieldKind.BasedOn).MinWidth, Height = StyleDialogMetrics.ComboBoxHeight };
        basedOn.ItemsSource = state.BasedOnOptions;
        basedOn.DisplayMemberPath = "Key";
        basedOn.SelectedIndex = state.BasedOnIndex;

        // "Style for following paragraph" (Word's w:next): the style the next paragraph takes when Enter is
        // pressed at the end of one carrying this style. "(same style)" maps to null (keep this style).
        var nextStyle = new ComboBox { MinWidth = surface.Field(StyleDialogFieldKind.NextStyle).MinWidth, Height = StyleDialogMetrics.ComboBoxHeight };
        nextStyle.ItemsSource = state.NextStyleOptions;
        nextStyle.DisplayMemberPath = "Key";
        nextStyle.SelectedIndex = state.NextStyleIndex;

        var effectControls = surface.Effects.ToDictionary(
            spec => spec.Kind,
            spec => new CheckBox
            {
                Content = spec.Label,
                IsChecked = state.EffectValue(spec.Kind),
                Height = StyleDialogMetrics.CheckBoxHeight,
                Margin = new Thickness(0, 0, spec.Kind == StyleDialogEffectKind.Underline ? 0 : 12, 0),
            });
        var effects = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var spec in surface.Effects)
        {
            AutomationProperties.SetAutomationId(effectControls[spec.Kind], spec.AutomationId);
            effects.Children.Add(effectControls[spec.Kind]);
        }

        var size = new ComboBox { MinWidth = surface.Field(StyleDialogFieldKind.FontSize).MinWidth, Height = StyleDialogMetrics.ComboBoxHeight };
        size.ItemsSource = StyleDialogPlanner.FontSizes.Select(s => s.Label).ToList();
        size.SelectedIndex = state.FontSizeIndex;

        var color = new ComboBox { MinWidth = surface.Field(StyleDialogFieldKind.TextColor).MinWidth, Height = StyleDialogMetrics.ComboBoxHeight };
        color.ItemsSource = StyleDialogPlanner.Colors.Select(c => c.Label).ToList();
        color.SelectedIndex = state.ColorIndex;

        var alignment = new ComboBox { MinWidth = surface.Field(StyleDialogFieldKind.Alignment).MinWidth, Height = StyleDialogMetrics.ComboBoxHeight };
        alignment.ItemsSource = StyleDialogPlanner.AlignmentLabels.ToList();
        alignment.SelectedIndex = state.AlignmentIndex;

        var fields = new Dictionary<StyleDialogFieldKind, UIElement>
        {
            [StyleDialogFieldKind.Name] = name,
            [StyleDialogFieldKind.BasedOn] = basedOn,
            [StyleDialogFieldKind.NextStyle] = nextStyle,
            [StyleDialogFieldKind.Formatting] = effects,
            [StyleDialogFieldKind.FontSize] = size,
            [StyleDialogFieldKind.TextColor] = color,
            [StyleDialogFieldKind.Alignment] = alignment,
        };
        foreach (var spec in surface.Fields)
            AutomationProperties.SetAutomationId(fields[spec.Kind], spec.AutomationId);

        void Accept()
        {
            var acceptance = session.PlanAcceptance(StyleDialogPlanner.CaptureControlState(
                name.Text,
                basedOn.SelectedIndex,
                nextStyle.SelectedIndex,
                size.SelectedIndex,
                color.SelectedIndex,
                alignment.SelectedIndex,
                kind => effectControls[kind].IsChecked == true));

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
            buttonWidth: surface.ActionButtonWidth,
            rowMargin: new Thickness(0, StyleDialogMetrics.ActionRowTopMargin, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(StyleDialogMetrics.DialogMargin) };
        foreach (var spec in surface.Fields)
            AddRow(panel, spec.Label, fields[spec.Kind]);
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
        var surface = StyleDialogPlanner.Surface.Manage;

        var dialog = new Window
        {
            Title = surface.Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        // Sort order picker - Alphabetical / By Type / By Use.
        var sortField = surface.Field(ManageStyleFieldKind.Sort);
        var sortOrderBox = new ComboBox { MinWidth = sortField.MinWidth, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(sortOrderBox, sortField.AutomationId);
        foreach (var label in StyleDialogPlanner.ManageStyleSortLabels)
            sortOrderBox.Items.Add(label);
        sortOrderBox.SelectedIndex = session.State.SortIndex;

        var listField = surface.Field(ManageStyleFieldKind.Styles);
        var list = new ListBox { MinWidth = listField.MinWidth, MinHeight = listField.MinHeight };
        AutomationProperties.SetAutomationId(list, listField.AutomationId);

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

        var actionButtons = surface.Actions.ToDictionary(
            spec => spec.Kind,
            spec => new Button
            {
                Content = spec.Label,
                IsDefault = spec.IsDefault,
                IsCancel = spec.IsCancel,
                MinWidth = surface.ActionButtonWidth,
                Margin = spec.Kind == ManageStyleCommandKind.Close
                    ? new Thickness(0)
                    : new Thickness(0, 0, 0, 8),
            });
        foreach (var spec in surface.Actions)
        {
            var button = actionButtons[spec.Kind];
            AutomationProperties.SetAutomationId(button, spec.AutomationId);
            button.Click += (_, _) =>
            {
                if (spec.ActionKind is not { } actionKind)
                {
                    dialog.Close();
                    return;
                }

                if (session.PlanAction(actionKind, list.SelectedIndex) is not { } action)
                    return;
                result = action;
                dialog.DialogResult = true;
            };
        }

        void SyncButtons()
        {
            var buttons = session.SelectRow(list.SelectedIndex).Buttons;
            foreach (var spec in surface.Actions)
                actionButtons[spec.Kind].IsEnabled = buttons.IsEnabled(spec.Kind);
        }

        list.SelectionChanged += (_, _) => SyncButtons();
        SyncButtons();

        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        sortRow.Children.Add(new TextBlock
        {
            Text = sortField.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        sortRow.Children.Add(sortOrderBox);

        var listPane = new StackPanel();
        listPane.Children.Add(sortRow);
        listPane.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var spec in surface.Actions)
            buttons.Children.Add(actionButtons[spec.Kind]);

        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        body.Children.Add(listPane);
        body.Children.Add(buttons);
        dialog.Content = body;

        list.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }
}

using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A small "Define New Multilevel List" dialog for the backed FreeW multilevel options.
/// </summary>
internal static class MultilevelListDialog
{
    /// <summary>
    /// Show the "Define New Multilevel List" dialog seeded with the current selection. Returns the chosen
    /// definition, or null if cancelled.
    /// </summary>
    public static MultilevelListDefinition? Prompt(
        Window? owner,
        IReadOnlyList<ListNumberFormat>? currentNumberFormats = null)
    {
        MultilevelListDefinition? result = null;

        MultilevelListDialogSession session = MultilevelListDialogPlanner.CreateSession(
            currentNumberFormats,
            System.Globalization.CultureInfo.CurrentCulture);
        var dialog = new Window
        {
            Title = MultilevelListDialogPlanner.Title,
            Width = MultilevelListDialogPlanner.DialogWidth,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var state = session.InitialState;
        var levelsBox = new ComboBox
        {
            MinWidth = MultilevelListDialogPlanner.LevelsMinWidth,
            Margin = new Thickness(0, 0, 0, 8)
        };
        foreach (var choice in session.LevelChoices)
            levelsBox.Items.Add(choice);
        levelsBox.SelectedIndex = state.LevelsIndex;

        var startAt0Box = new TextBox { Text = state.Level0StartAtText, MinWidth = MultilevelListDialogPlanner.StartAtMinWidth, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Start-at for level 1 (1-based)" };
        var startAt1Box = new TextBox { Text = state.Level1StartAtText, MinWidth = MultilevelListDialogPlanner.StartAtMinWidth, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Start-at for level 2 (1-based)" };
        var level0FormatBox = NumberFormatBox(session.NumberFormatChoices, state.Level0FormatIndex);
        var level1FormatBox = NumberFormatBox(session.NumberFormatChoices, state.Level1FormatIndex);
        var level2FormatBox = NumberFormatBox(session.NumberFormatChoices, state.Level2FormatIndex);
        levelsBox.SelectionChanged += (_, _) => session.UpdateLevels(levelsBox.SelectedIndex);
        startAt0Box.TextChanged += (_, _) => session.UpdateLevel0StartAt(startAt0Box.Text);
        startAt1Box.TextChanged += (_, _) => session.UpdateLevel1StartAt(startAt1Box.Text);
        level0FormatBox.SelectionChanged += (_, _) => session.UpdateLevel0Format(level0FormatBox.SelectedIndex);
        level1FormatBox.SelectionChanged += (_, _) => session.UpdateLevel1Format(level1FormatBox.SelectedIndex);
        level2FormatBox.SelectionChanged += (_, _) => session.UpdateLevel2Format(level2FormatBox.SelectedIndex);

        var panel = new StackPanel { Margin = new Thickness(MultilevelListDialogPlanner.OuterMargin) };
        panel.Children.Add(new TextBlock
        {
            Text = MultilevelListDialogPlanner.Description,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        AddRow(panel, MultilevelListDialogPlanner.LevelsLabel, levelsBox);
        AddRow(panel, MultilevelListDialogPlanner.Level0StartAtLabel, startAt0Box);
        AddRow(panel, MultilevelListDialogPlanner.Level1StartAtLabel, startAt1Box);
        AddRow(panel, MultilevelListDialogPlanner.Level0NumberStyleLabel, level0FormatBox);
        AddRow(panel, MultilevelListDialogPlanner.Level1NumberStyleLabel, level1FormatBox);
        AddRow(panel, MultilevelListDialogPlanner.Level2NumberStyleLabel, level2FormatBox);

        void Accept()
        {
            SynchronizeSession();
            var acceptance = session.PlanAcceptance();
            if (!acceptance.IsAccepted)
            {
                DialogMessageHelper.ShowWarning(dialog, acceptance.Validation?.Message ?? MultilevelListDialogPlanner.PositiveStartAtMessage);
                var target = acceptance.Validation?.Field == MultilevelListDialogField.Level1StartAt ? startAt1Box : startAt0Box;
                DialogFocus.FocusAndSelect(target);
                return;
            }
            result = acceptance.Definition;
            dialog.DialogResult = true;
        }

        void SynchronizeSession()
        {
            session.UpdateLevels(levelsBox.SelectedIndex);
            session.UpdateLevel0StartAt(startAt0Box.Text);
            session.UpdateLevel1StartAt(startAt1Box.Text);
            session.UpdateLevel0Format(level0FormatBox.SelectedIndex);
            session.UpdateLevel1Format(level1FormatBox.SelectedIndex);
            session.UpdateLevel2Format(level2FormatBox.SelectedIndex);
        }

        panel.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: MultilevelListDialogPlanner.ButtonWidth,
            rowMargin: new Thickness(0, 12, 0, 0)));
        dialog.Content = panel;
        levelsBox.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }

    private static void AddRow(Panel panel, string label, UIElement field)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(field);
    }

    private static ComboBox NumberFormatBox(
        IReadOnlyList<MultilevelListNumberFormatChoice> choices,
        int selectedIndex)
    {
        var box = new ComboBox
        {
            MinWidth = MultilevelListDialogPlanner.NumberFormatMinWidth,
            Margin = new Thickness(0, 0, 0, 8)
        };
        foreach (var choice in choices)
            box.Items.Add(choice);
        box.SelectedIndex = selectedIndex;
        return box;
    }
}

using System.Windows;
using System.Windows.Controls;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A predefined multilevel-list format applied by the Multilevel List dropdown gallery.
/// </summary>
internal sealed record MultilevelListPreset(string Name, string Description, Action<DocumentView> Apply);

/// <summary>
/// The per-level configuration captured by the "Define New Multilevel List" dialog.
/// </summary>
/// <summary>
/// A small "Define New Multilevel List" dialog for the backed FreeW multilevel options.
/// </summary>
internal static class MultilevelListDialog
{
    /// <summary>
    /// The catalog of predefined multilevel-list formats shown in the Multilevel List dropdown gallery.
    /// </summary>
    public static readonly MultilevelListPreset[] Presets =
    [
        new(
            "Outline: 1. / 1.1. / 1.1.1.",
            "Decimal outline using the standard FreeW multilevel list.",
            view =>
            {
                view.ApplyMultiLevelListDefinition(new MultilevelListDefinition(
                    MultiLevelListFormat.LevelCount,
                    null,
                    null,
                    MultiLevelListFormat.DecimalNumberFormats));
            }),
        new(
            "Outline: 1. / a. / i.",
            "Decimal + lower-letter + lower-roman per-level numbering.",
            view =>
            {
                view.ApplyMultiLevelListDefinition(new MultilevelListDefinition(
                    MultiLevelListFormat.LevelCount,
                    null,
                    null,
                    MultiLevelListFormat.DecimalLowerLetterLowerRomanNumberFormats));
            }),
        new(
            "Outline (Headings): link to Heading styles",
            "Apply multilevel list and map each level to Heading 1-3 styles.",
            view =>
            {
                view.ApplyMultiLevelListDefinition(new MultilevelListDefinition(
                    MultiLevelListFormat.LevelCount,
                    null,
                    null,
                    MultiLevelListFormat.DecimalNumberFormats,
                    LinkToHeadingStyles: true));
            }),
    ];

    /// <summary>
    /// Show the "Define New Multilevel List" dialog seeded with the current selection. Returns the chosen
    /// definition, or null if cancelled.
    /// </summary>
    public static MultilevelListDefinition? Prompt(
        Window? owner,
        IReadOnlyList<ListNumberFormat>? currentNumberFormats = null)
    {
        MultilevelListDefinition? result = null;

        var dialog = new Window
        {
            Title = "Define New Multilevel List",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var state = MultilevelListDialogPlanner.BuildInitialState(
            currentNumberFormats,
            System.Globalization.CultureInfo.CurrentCulture);
        var levelsBox = new ComboBox { MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 1; i <= 9; i++)
            levelsBox.Items.Add(i.ToString());
        levelsBox.SelectedIndex = state.LevelsIndex;

        var startAt0Box = new TextBox { Text = state.Level0StartAtText, MinWidth = 60, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Start-at for level 1 (1-based)" };
        var startAt1Box = new TextBox { Text = state.Level1StartAtText, MinWidth = 60, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Start-at for level 2 (1-based)" };
        var level0FormatBox = NumberFormatBox(state.Level0FormatIndex);
        var level1FormatBox = NumberFormatBox(state.Level1FormatIndex);
        var level2FormatBox = NumberFormatBox(state.Level2FormatIndex);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Configure multilevel list levels.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        AddRow(panel, "Number of levels (1-9):", levelsBox);
        AddRow(panel, "Level 1 start at:",        startAt0Box);
        AddRow(panel, "Level 2 start at:",        startAt1Box);
        AddRow(panel, "Level 1 number style:",    level0FormatBox);
        AddRow(panel, "Level 2 number style:",    level1FormatBox);
        AddRow(panel, "Level 3 number style:",    level2FormatBox);

        void Accept()
        {
            if (!MultilevelListDialogPlanner.TryBuildResult(
                    new MultilevelListDialogInput(
                        levelsBox.SelectedIndex,
                        startAt0Box.Text,
                        startAt1Box.Text,
                        level0FormatBox.SelectedIndex,
                        level1FormatBox.SelectedIndex,
                        level2FormatBox.SelectedIndex),
                    System.Globalization.CultureInfo.CurrentCulture,
                    out result,
                    out var validation))
            {
                DialogMessageHelper.ShowWarning(dialog, validation?.Message ?? MultilevelListDialogPlanner.PositiveStartAtMessage);
                var target = validation?.Field == MultilevelListDialogField.Level1StartAt ? startAt1Box : startAt0Box;
                DialogFocus.FocusAndSelect(target);
                return;
            }
            dialog.DialogResult = true;
        }

        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0)));
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

    private static ComboBox NumberFormatBox(int selectedIndex)
    {
        var box = new ComboBox { MinWidth = 130, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var choice in MultilevelListDialogPlanner.NumberFormatChoices)
            box.Items.Add(choice);
        box.SelectedIndex = selectedIndex;
        return box;
    }
}

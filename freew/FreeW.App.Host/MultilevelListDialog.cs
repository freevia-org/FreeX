using System.Windows;
using System.Windows.Controls;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A predefined multilevel-list format applied by the Multilevel List dropdown gallery.
/// </summary>
internal sealed record MultilevelListPreset(string Name, string Description, Action<DocumentView> Apply);

/// <summary>
/// The per-level configuration captured by the "Define New Multilevel List" dialog.
/// </summary>
internal sealed record MultilevelListDefinition(
    /// <summary>Number of active levels (1-9).</summary>
    int Levels,
    /// <summary>Start-at value for level 0 (1-based; null = continue).</summary>
    int? Level0StartAt,
    /// <summary>Start-at value for level 1 (1-based; null = continue).</summary>
    int? Level1StartAt,
    /// <summary>Number formats for the modelled multilevel definition.</summary>
    IReadOnlyList<ListNumberFormat> NumberFormats);

/// <summary>
/// A small "Define New Multilevel List" dialog for the backed FreeW multilevel options.
/// </summary>
internal static class MultilevelListDialog
{
    private sealed record NumberFormatChoice(string Label, ListNumberFormat Format)
    {
        public override string ToString() => Label;
    }

    private static readonly NumberFormatChoice[] NumberFormatChoices =
    [
        new("1, 2, 3", ListNumberFormat.Decimal),
        new("a, b, c", ListNumberFormat.LowerLetter),
        new("A, B, C", ListNumberFormat.UpperLetter),
        new("i, ii, iii", ListNumberFormat.LowerRoman),
        new("I, II, III", ListNumberFormat.UpperRoman)
    ];

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
                view.ApplyMultiLevelList();
                view.ApplyMultiLevelNumberFormats(MultiLevelListFormat.DecimalNumberFormats);
            }),
        new(
            "Outline: 1. / a. / i.",
            "Decimal + lower-letter + lower-roman per-level numbering.",
            view =>
            {
                view.ApplyMultiLevelList();
                view.ApplyMultiLevelNumberFormats(MultiLevelListFormat.DecimalLowerLetterLowerRomanNumberFormats);
            }),
        new(
            "Outline (Headings): link to Heading styles",
            "Apply multilevel list and map each level to Heading 1-3 styles.",
            view =>
            {
                view.ApplyMultiLevelList();
                var fmt = view.CurrentParagraphFormatting;
                var headingStyleId = fmt.ListLevel switch
                {
                    0 => "Heading1",
                    1 => "Heading2",
                    _ => "Heading3",
                };
                if (view.Model.Styles.ContainsKey(headingStyleId))
                    view.SetParagraphStyle(headingStyleId);
                view.ApplyMultiLevelNumberFormats(MultiLevelListFormat.DecimalNumberFormats);
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

        var levelsBox = new ComboBox { MinWidth = 80, Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 1; i <= 9; i++)
            levelsBox.Items.Add(i.ToString());
        levelsBox.SelectedIndex = 8;

        var startAt0Box = new TextBox { Text = "1", MinWidth = 60, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Start-at for level 1 (1-based)" };
        var startAt1Box = new TextBox { Text = "1", MinWidth = 60, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Start-at for level 2 (1-based)" };
        var level0FormatBox = NumberFormatBox(GetFormat(currentNumberFormats, 0));
        var level1FormatBox = NumberFormatBox(GetFormat(currentNumberFormats, 1));
        var level2FormatBox = NumberFormatBox(GetFormat(currentNumberFormats, 2));

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
            var levels = levelsBox.SelectedIndex + 1;

            int? s0 = null, s1 = null;
            if (startAt0Box.Text.Trim().Length > 0)
            {
                if (!int.TryParse(startAt0Box.Text.Trim(), out var v0) || v0 < 1)
                {
                    DialogMessageHelper.ShowWarning(dialog, "Level 1 start-at must be a positive integer.");
                    return;
                }
                s0 = v0;
            }
            if (startAt1Box.Text.Trim().Length > 0)
            {
                if (!int.TryParse(startAt1Box.Text.Trim(), out var v1) || v1 < 1)
                {
                    DialogMessageHelper.ShowWarning(dialog, "Level 2 start-at must be a positive integer.");
                    return;
                }
                s1 = v1;
            }

            var formats = MultiLevelListFormat.DecimalNumberFormats.ToArray();
            formats[0] = SelectedNumberFormat(level0FormatBox);
            formats[1] = SelectedNumberFormat(level1FormatBox);
            formats[2] = SelectedNumberFormat(level2FormatBox);
            result = new MultilevelListDefinition(levels, s0, s1, formats);
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

    private static ComboBox NumberFormatBox(ListNumberFormat selected)
    {
        var box = new ComboBox { MinWidth = 130, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var choice in NumberFormatChoices)
            box.Items.Add(choice);
        box.SelectedItem = NumberFormatChoices.First(c => c.Format == selected);
        return box;
    }

    private static ListNumberFormat SelectedNumberFormat(ComboBox box) =>
        box.SelectedItem is NumberFormatChoice choice ? choice.Format : ListNumberFormat.Decimal;

    private static ListNumberFormat GetFormat(IReadOnlyList<ListNumberFormat>? formats, int level) =>
        formats is not null && level < formats.Count ? formats[level] : ListNumberFormat.Decimal;
}

using System.Windows;
using System.Windows.Controls;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A predefined multilevel-list format applied by the Multilevel List dropdown gallery.
/// Encodes the list kind and an optional link-to-style (heading style) for each level. Because the
/// FreeW model represents multilevel lists as a single <see cref="ListKind.MultiLevel"/> definition
/// (the decimal accumulating counter 1., 1.1., 1.1.1. … serialised to word/numbering.xml), presets that
/// differ only in number style are not yet fully separable in the model. The name is shown in the gallery
/// picker and as a tooltip; the <see cref="Apply"/> lambda is the concrete application action.
/// </summary>
internal sealed record MultilevelListPreset(string Name, string Description, Action<DocumentView> Apply);

/// <summary>
/// The per-level configuration captured by the "Define New Multilevel List" dialog. Per-level number
/// style (Roman / letter / decimal) is not yet backed by the model and is deferred; the dialog surfaces
/// only the backed subset: start-at value per level and which levels to include.
/// </summary>
internal sealed record MultilevelListDefinition(
    /// <summary>Number of active levels (1–9).</summary>
    int Levels,
    /// <summary>Start-at value for level 0 (1-based; null = continue).</summary>
    int? Level0StartAt,
    /// <summary>Start-at value for level 1 (1-based; null = continue).</summary>
    int? Level1StartAt);

/// <summary>
/// A small "Define New Multilevel List" dialog that captures the backed subset of per-level options:
/// number of active outline levels and optional start-at value for the first two levels.
/// Per-level number style (Roman, letter, decimal) is not yet modelled and is noted as deferred.
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
            "Decimal outline (1., 1.1., 1.1.1. …) — the standard FreeW multilevel list.",
            view => view.ApplyMultiLevelList()),
        new(
            "Outline: 1. / a. / i.",
            "Decimal + letter + roman — applied as the same multilevel counter (per-level style is a render hint).",
            view => view.ApplyMultiLevelList()),
        new(
            "Outline (Headings): link to Heading styles",
            "Apply multilevel list and map each level to Heading 1–3 styles.",
            view =>
            {
                view.ApplyMultiLevelList();
                // Link-to-style is a render hint: set the paragraph style to the matching heading level
                // (the list level was already set by ApplyMultiLevelList / ChangeListLevel).
                var fmt = view.CurrentParagraphFormatting;
                var headingStyleId = fmt.ListLevel switch
                {
                    0 => "Heading1",
                    1 => "Heading2",
                    _ => "Heading3",
                };
                if (view.Model.Styles.ContainsKey(headingStyleId))
                    view.SetParagraphStyle(headingStyleId);
            }),
    ];

    /// <summary>
    /// Show the "Define New Multilevel List" dialog seeded with the current selection. Returns the chosen
    /// definition, or null if cancelled.
    /// </summary>
    public static MultilevelListDefinition? Prompt(Window? owner)
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
        levelsBox.SelectedIndex = 8; // default to 9 levels (Word default)

        var startAt0Box = new TextBox { Text = "1", MinWidth = 60, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Start-at for level 1 (1-based)" };
        var startAt1Box = new TextBox { Text = "1", MinWidth = 60, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Start-at for level 2 (1-based)" };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Configure multilevel list levels.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        AddRow(panel, "Number of levels (1–9):", levelsBox);
        AddRow(panel, "Level 1 start at:",        startAt0Box);
        AddRow(panel, "Level 2 start at:",        startAt1Box);

        panel.Children.Add(new TextBlock
        {
            Text = "Note: per-level number style (Roman numerals, letters) is not yet backed by the FreeW model — deferred.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 10,
            Margin = new Thickness(0, 10, 0, 0),
        });

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

            result = new MultilevelListDefinition(levels, s0, s1);
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
}

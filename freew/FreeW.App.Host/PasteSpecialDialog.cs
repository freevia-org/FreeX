using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// A small modal Paste Special dialog matching the subset of Word's "Paste Special" that FreeW can
/// back. Available paste options depend on what the clipboard actually holds:
/// <list type="bullet">
/// <item>"Keep Source Formatting" and "Merge Formatting" are always offered when the clipboard has text.</item>
/// <item>"Keep Text Only" (plain text) is always offered when the clipboard has text.</item>
/// </list>
/// Keep Source Formatting imports clipboard RTF at an empty body paragraph; other positions retain the
/// merge-formatting text path. The dialog returns the chosen <see cref="PasteSpecialOption"/>, or null if
/// cancelled or the clipboard has no usable content.
/// </summary>
internal static class PasteSpecialDialog
{
    public static PasteSpecialOption? Prompt(Window? owner)
    {
        // Check the clipboard before showing any UI; no usable text → nothing to offer.
        bool hasText;
        try
        {
            hasText = System.Windows.Clipboard.ContainsText();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            hasText = false;
        }

        if (!hasText)
        {
            DialogMessageHelper.ShowWarning(
                owner as Window,
                "The clipboard is empty or does not contain text that can be pasted.");
            return null;
        }

        // Build the list of backed options; order matches Word's Paste Special dialog.
        var options = PasteSpecialOptionCatalog.Options;

        PasteSpecialOption? result = null;

        var dialog = new Window
        {
            Title = "Paste Special",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var list = new ListBox
        {
            MinWidth = 340,
            MinHeight = 90,
            Margin = new Thickness(0, 0, 0, 8),
            SelectionMode = SelectionMode.Single,
        };
        foreach (var opt in options)
            list.Items.Add(opt.Label);
        list.SelectedIndex = 0;

        var description = new TextBlock
        {
            Text = options[0].Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DarkGray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 32,
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < options.Count)
                description.Text = options[list.SelectedIndex].Description;
        };

        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
            {
                result = options[list.SelectedIndex].Option;
                dialog.DialogResult = true;
            }
        };

        void Accept()
        {
            if (list.SelectedIndex >= 0)
            {
                result = options[list.SelectedIndex].Option;
                dialog.DialogResult = true;
            }
        }

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Paste As:",
            FontWeight = System.Windows.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(list);
        panel.Children.Add(description);
        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 4, 0, 0)));

        dialog.Content = panel;
        list.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }
}

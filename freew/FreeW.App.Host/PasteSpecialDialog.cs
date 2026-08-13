using System.Windows;
using System.Windows.Controls;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
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
    public static PasteSpecialOption? Prompt(
        Window? owner,
        IPlatformClipboard? platformClipboard = null)
    {
        // Check the clipboard before showing any UI; no text format → nothing to offer.
        var clipboard = platformClipboard ?? new WpfPlatformClipboard(owner?.Dispatcher);
        var read = clipboard.ReadTextAsync().AsTask().GetAwaiter().GetResult();
        var hasText = read.Status == PlatformClipboardReadStatus.Success;

        if (!hasText)
        {
            DialogMessageHelper.ShowWarning(
                owner as Window,
                PasteSpecialDialogSession.EmptyClipboardMessage);
            return null;
        }

        var session = new PasteSpecialDialogSession();

        PasteSpecialOption? result = null;

        var dialog = new Window
        {
            Title = PasteSpecialDialogSession.Title,
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
        foreach (var opt in session.Options)
            list.Items.Add(opt.Label);
        list.SelectedIndex = 0;

        var description = new TextBlock
        {
            Text = session.State.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DarkGray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 32,
        };

        list.SelectionChanged += (_, _) =>
        {
            description.Text = session.UpdateSelection(list.SelectedIndex).Description;
        };

        list.MouseDoubleClick += (_, _) =>
        {
            session.UpdateSelection(list.SelectedIndex);
            if (session.PlanAcceptance() is { } option)
            {
                result = option;
                dialog.DialogResult = true;
            }
        };

        void Accept()
        {
            session.UpdateSelection(list.SelectedIndex);
            if (session.PlanAcceptance() is { } option)
            {
                result = option;
                dialog.DialogResult = true;
            }
        }

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = PasteSpecialDialogSession.PasteAsLabel,
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

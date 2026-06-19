using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FreeW.App.Host;

internal sealed record CompareDocumentsChoice(string OriginalFilePath, string Author);

/// <summary>
/// Minimal two-step Review > Compare picker: choose the original DOCX, then confirm the reviewer name.
/// </summary>
internal sealed class CompareDocumentsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _author;

    private CompareDocumentsDialog(Window? owner, string defaultAuthor, string revisedTitle, string originalPath)
    {
        if (owner is not null)
            Owner = owner;

        Title = "Compare Documents";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(revisedTitle)
                ? "Compare the selected original document with the current document."
                : $"Compare the selected original document with \"{revisedTitle}\".",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        panel.Children.Add(new TextBlock { Text = "Original document" });
        panel.Children.Add(new TextBox
        {
            Text = originalPath,
            IsReadOnly = true,
            Margin = new Thickness(0, 3, 0, 10)
        });

        panel.Children.Add(new TextBlock { Text = "Label changes as" });
        _author = new TextBox
        {
            Text = defaultAuthor,
            Margin = new Thickness(0, 3, 0, 14)
        };
        panel.Children.Add(_author);

        var compare = new Button
        {
            Content = "Compare",
            MinWidth = 88,
            IsDefault = true,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 8, 0)
        };
        compare.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_author.Text))
                DialogResult = true;
        };

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            IsCancel = true,
            Padding = new Thickness(8, 3, 8, 3)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(compare);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
    }

    public static CompareDocumentsChoice? Prompt(Window? owner, string defaultAuthor, string revisedTitle)
    {
        var picker = new OpenFileDialog
        {
            Filter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*",
            DefaultExt = ".docx",
            Title = "Compare: pick the ORIGINAL document"
        };

        if (picker.ShowDialog(owner) != true)
            return null;

        var dialog = new CompareDocumentsDialog(owner, defaultAuthor, revisedTitle, picker.FileName);
        if (dialog.ShowDialog() != true)
            return null;

        var author = dialog._author.Text.Trim();
        return string.IsNullOrWhiteSpace(author)
            ? null
            : new CompareDocumentsChoice(picker.FileName, author);
    }
}

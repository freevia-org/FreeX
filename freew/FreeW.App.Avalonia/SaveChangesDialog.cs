using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;

namespace FreeW.App.Avalonia;

/// <summary>
/// A simple 3-way modal: Save / Don't save / Cancel.
/// Returns the matching <see cref="SaveChangesPrompt"/> value, or
/// <see cref="SaveChangesPrompt.Cancel"/> if the user closes the window.
/// </summary>
internal sealed class SaveChangesDialog : Window
{
    private SaveChangesDialog(string documentName, string action)
    {
        Title = "FreeW";
        Width = 420;
        Height = 170;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var message = new TextBlock
        {
            Text = $"Do you want to save changes to \"{documentName}\" before {action}?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 20),
        };

        var save = new Button { Content = "Save", MinWidth = 82, IsDefault = true };
        save.Click += (_, _) => Close(SaveChangesPrompt.Save);

        var dontSave = new Button { Content = "Don't save", MinWidth = 82, Margin = new Thickness(8, 0, 0, 0) };
        dontSave.Click += (_, _) => Close(SaveChangesPrompt.DontSave);

        var cancel = new Button { Content = "Cancel", MinWidth = 82, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => Close(SaveChangesPrompt.Cancel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
            Children = { save, dontSave, cancel },
        };

        Content = new StackPanel
        {
            Children = { message, buttons },
        };
    }

    /// <summary>
    /// Show the dialog and return the user's choice.
    /// Must be called from the UI thread.
    /// </summary>
    public static async Task<SaveChangesPrompt> ShowAsync(Window owner, string documentName, string action)
    {
        var dialog = new SaveChangesDialog(documentName, action);
        var result = await dialog.ShowDialog<object?>(owner);

        // ShowDialog returns whatever was passed to Close(). If the user dismisses
        // via the OS close button Close() is called with null — treat that as Cancel.
        return result is SaveChangesPrompt prompt ? prompt : SaveChangesPrompt.Cancel;
    }
}

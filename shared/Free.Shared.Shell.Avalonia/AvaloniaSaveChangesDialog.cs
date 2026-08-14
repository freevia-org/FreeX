using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaSaveChangesPromptText(
    string WindowTitle,
    string Message,
    string SaveButtonText,
    string DontSaveButtonText,
    string CancelButtonText)
{
    public static AvaloniaSaveChangesPromptText ForDocumentAction(
        string windowTitle,
        string documentName,
        string action) =>
        new(
            windowTitle,
            $"Do you want to save changes to \"{documentName}\" before {action}?",
            "Save",
            "Don't save",
            "Cancel");
}

/// <summary>
/// Shared Avalonia dirty-gate dialog for sister document apps using <see cref="FileCommandWorkflow"/>.
/// </summary>
public sealed class AvaloniaSaveChangesDialog : AvaloniaDialogWindow
{
    private readonly Button _saveButton;

    private AvaloniaSaveChangesDialog(AvaloniaSaveChangesPromptText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Title = text.WindowTitle;
        Width = 420;
        Height = 170;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var message = new TextBlock
        {
            Text = text.Message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 20),
        };

        _saveButton = CreateButton(
            text.SaveButtonText,
            SaveChangesPrompt.Save,
            isDefault: true,
            isCancel: false,
            margin: default);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
            Children =
            {
                _saveButton,
                CreateButton(text.DontSaveButtonText, SaveChangesPrompt.DontSave, isDefault: false, isCancel: false, margin: new Thickness(8, 0, 0, 0)),
                CreateButton(text.CancelButtonText, SaveChangesPrompt.Cancel, isDefault: false, isCancel: true, margin: new Thickness(8, 0, 0, 0)),
            },
        };

        Content = new StackPanel
        {
            Children = { message, buttons },
        };
        Opened += (_, _) => _saveButton.Focus();
    }

    public static Task<SaveChangesPrompt> ShowAsync(
        Window owner,
        AvaloniaSaveChangesPromptText text)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new AvaloniaSaveChangesDialog(text);
        return ShowDialogCoreAsync(dialog, owner);
    }

    public static AvaloniaSaveChangesDialog CreateForTests(AvaloniaSaveChangesPromptText text) => new(text);

    private static async Task<SaveChangesPrompt> ShowDialogCoreAsync(Window dialog, Window owner)
    {
        var result = await dialog.ShowDialog<object?>(owner);
        return result is SaveChangesPrompt prompt ? prompt : SaveChangesPrompt.Cancel;
    }

    private Button CreateButton(
        string content,
        SaveChangesPrompt result,
        bool isDefault,
        bool isCancel,
        Thickness margin)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 82,
            IsDefault = isDefault,
            IsCancel = isCancel,
            Margin = margin,
        };
        button.Click += (_, _) => Close(result);
        return button;
    }
}

using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// WPF message-box surface for small-document file commands. The lifecycle decisions live in
/// <see cref="FileCommandWorkflow"/>; this helper owns the repeated Windows prompt rendering.
/// </summary>
public static class FileCommandMessageBox
{
    public static SaveChangesPrompt PromptSaveChanges(
        Window owner,
        string displayName,
        string action,
        string appTitle)
    {
        var result = WpfMessageBoxRealizer.Show(
            owner,
            $"Do you want to save changes to {displayName} before {action}?",
            appTitle,
            UserMessageButtons.YesNoCancel,
            UserMessageIcon.Warning);

        return result switch
        {
            UserMessageResult.Yes => SaveChangesPrompt.Save,
            UserMessageResult.No => SaveChangesPrompt.DontSave,
            _ => SaveChangesPrompt.Cancel,
        };
    }

    public static void ShowError(Window owner, string summary, Exception exception, string appTitle)
    {
        ArgumentNullException.ThrowIfNull(exception);

        WpfMessageBoxRealizer.Show(
            owner,
            $"{summary}:\n{exception.Message}",
            appTitle,
            UserMessageButtons.Ok,
            UserMessageIcon.Error);
    }
}

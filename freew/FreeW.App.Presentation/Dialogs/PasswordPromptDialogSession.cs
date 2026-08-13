namespace FreeW.App.Presentation.Dialogs;

public sealed record PasswordPromptDialogState(
    string Title,
    string Prompt,
    string Password);

/// <summary>
/// Owns the renderer-neutral input projection and accepted value for the paired password prompts.
/// A null native textbox value is normalized to the empty string, matching PasswordBox semantics.
/// </summary>
public sealed class PasswordPromptDialogSession
{
    public const string WindowAutomationId = "PasswordPromptDialog";
    public const string PasswordAutomationId = "PasswordPromptPasswordBox";
    public const string AcceptButtonAutomationId = "PasswordPromptOkButton";
    public const string CancelButtonAutomationId = "PasswordPromptCancelButton";

    private PasswordPromptDialogState _state;

    public PasswordPromptDialogSession(string title, string prompt)
    {
        _state = new PasswordPromptDialogState(title, prompt, Password: string.Empty);
    }

    public PasswordPromptDialogState State => _state;

    public PasswordPromptDialogState UpdatePassword(string? password)
    {
        _state = _state with { Password = password ?? string.Empty };
        return _state;
    }

    public string PlanAcceptance() => _state.Password;
}

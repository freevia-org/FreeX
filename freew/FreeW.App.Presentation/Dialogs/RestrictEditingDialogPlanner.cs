using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record RestrictEditingModeOption(string Label, ProtectionMode Mode);

public sealed record RestrictEditingDialogPlan(
    ProtectionSettings CurrentProtection,
    int SelectedModeIndex,
    bool CanStartProtection,
    bool CanStopProtection,
    bool ShowStartPasswordFields,
    bool ShowStopPasswordField,
    string StatusText);

public sealed record RestrictEditingDialogPresentationPlan(
    int DialogWidth,
    int ContentMargin,
    int PromptBottomMargin,
    int ModeOptionVerticalMargin,
    int RadioButtonHeight,
    int TextBoxHeight,
    int PasswordSeparatorTopMargin,
    int PasswordSeparatorBottomMargin,
    int PasswordPromptBottomMargin,
    int StartActionTopMargin,
    int ActionButtonBottomMargin,
    int CancelActionTopMargin,
    bool ShowStatusText,
    string? DefaultButtonText,
    string InitialFocusTarget,
    IReadOnlyList<string> ActionButtonOrder);

public enum RestrictEditingDialogOutcomeKind
{
    Accepted,
    Cancelled,
    ValidationFailed,
}

public sealed record RestrictEditingDialogOutcome(
    RestrictEditingDialogOutcomeKind Kind,
    ProtectionSettings? Settings = null,
    string? ValidationMessage = null)
{
    public bool IsAccepted => Kind == RestrictEditingDialogOutcomeKind.Accepted;
}

public delegate ValueTask<string?> RestrictEditingPasswordPrompt(string title, string prompt);

public sealed class RestrictEditingDialogSession
{
    private readonly ProtectionSettings _currentProtection;

    public RestrictEditingDialogSession(ProtectionSettings? current)
    {
        _currentProtection = current ?? ProtectionSettings.Unprotected;
        InitialPlan = RestrictEditingDialogPlanner.BuildPlan(_currentProtection);
    }

    public RestrictEditingDialogPlan InitialPlan { get; }

    public RestrictEditingDialogOutcome Start(
        int selectedModeIndex,
        string? password,
        string? confirmation)
    {
        if (!RestrictEditingDialogPlanner.TryCreateStartSettings(
                ModeAt(selectedModeIndex),
                password,
                confirmation,
                out var settings,
                out var validationMessage))
        {
            return new RestrictEditingDialogOutcome(
                RestrictEditingDialogOutcomeKind.ValidationFailed,
                ValidationMessage: validationMessage);
        }

        return new RestrictEditingDialogOutcome(RestrictEditingDialogOutcomeKind.Accepted, settings);
    }

    public async ValueTask<RestrictEditingDialogOutcome> StopAsync(
        RestrictEditingPasswordPrompt? prompt = null)
    {
        string? password = null;
        if (_currentProtection.HasPassword)
        {
            ArgumentNullException.ThrowIfNull(prompt);
            password = await prompt(
                RestrictEditingDialogPlanner.StopButtonText,
                RestrictEditingDialogPlanner.StopPasswordPrompt);
            if (password is null)
                return new RestrictEditingDialogOutcome(RestrictEditingDialogOutcomeKind.Cancelled);
        }

        if (!RestrictEditingDialogPlanner.TryCreateStopSettings(
                _currentProtection,
                password,
                out var settings,
                out var validationMessage))
        {
            return new RestrictEditingDialogOutcome(
                RestrictEditingDialogOutcomeKind.ValidationFailed,
                ValidationMessage: validationMessage);
        }

        return new RestrictEditingDialogOutcome(RestrictEditingDialogOutcomeKind.Accepted, settings);
    }

    public RestrictEditingDialogOutcome Cancel() =>
        new(RestrictEditingDialogOutcomeKind.Cancelled);

    public static ProtectionMode ModeAt(int selectedModeIndex) =>
        selectedModeIndex >= 0 && selectedModeIndex < RestrictEditingDialogPlanner.ModeOptions.Count
            ? RestrictEditingDialogPlanner.ModeOptions[selectedModeIndex].Mode
            : ProtectionMode.ReadOnly;
}

public static class RestrictEditingDialogPlanner
{
    public const string Title = "Restrict Editing";
    public const string RestrictionPrompt = "Allow only this type of editing in the document:";
    public const string StartButtonText = "Start Enforcing Protection";
    public const string StopButtonText = "Stop Protection";
    public const string CancelButtonText = "Cancel";
    public const string OptionalPasswordPrompt = "Optional password (leave blank for no password):";
    public const string PasswordLabel = "Password:";
    public const string ConfirmLabel = "Confirm:";
    public const string StopPasswordPrompt = "Enter the password to remove protection:";
    public const string PasswordMismatchMessage = "The passwords do not match. Please re-enter.";
    public const string IncorrectPasswordMessage = "Incorrect password. Protection has not been removed.";

    public static readonly RestrictEditingDialogPresentationPlan Presentation = new(
        DialogWidth: 360,
        ContentMargin: 14,
        PromptBottomMargin: 8,
        ModeOptionVerticalMargin: 3,
        RadioButtonHeight: 16,
        TextBoxHeight: 20,
        PasswordSeparatorTopMargin: 10,
        PasswordSeparatorBottomMargin: 6,
        PasswordPromptBottomMargin: 4,
        StartActionTopMargin: 14,
        ActionButtonBottomMargin: 4,
        CancelActionTopMargin: 8,
        ShowStatusText: false,
        DefaultButtonText: null,
        InitialFocusTarget: "first-mode",
        ActionButtonOrder: [StartButtonText, StopButtonText, CancelButtonText]);

    public static readonly IReadOnlyList<RestrictEditingModeOption> ModeOptions =
    [
        new("No changes (Read only)", ProtectionMode.ReadOnly),
        new("Tracked changes", ProtectionMode.TrackChangesOnly),
        new("Comments", ProtectionMode.CommentsOnly),
        new("Filling in forms", ProtectionMode.FillingForms)
    ];

    public static RestrictEditingDialogPlan BuildPlan(ProtectionSettings? current)
    {
        var protection = current ?? ProtectionSettings.Unprotected;
        return new RestrictEditingDialogPlan(
            protection,
            FindModeIndex(protection.Mode),
            CanStartProtection: !protection.IsProtected,
            CanStopProtection: protection.IsProtected,
            ShowStartPasswordFields: !protection.IsProtected,
            ShowStopPasswordField: protection.IsProtected && protection.HasPassword,
            StatusText: BuildStatusText(protection));
    }

    public static ProtectionMode NormalizeMode(ProtectionMode mode) =>
        ModeOptions.Any(option => option.Mode == mode)
            ? mode
            : ProtectionMode.ReadOnly;

    public static int FindModeIndex(ProtectionMode mode)
    {
        var normalized = NormalizeMode(mode);
        for (var i = 0; i < ModeOptions.Count; i++)
        {
            if (ModeOptions[i].Mode == normalized)
                return i;
        }

        return 0;
    }

    public static bool TryCreateStartSettings(
        ProtectionMode selectedMode,
        string? password,
        string? confirmation,
        out ProtectionSettings settings,
        out string? validationMessage)
    {
        var passwordText = password ?? string.Empty;
        var confirmationText = confirmation ?? string.Empty;
        if (!string.Equals(passwordText, confirmationText, StringComparison.Ordinal))
        {
            settings = ProtectionSettings.Unprotected;
            validationMessage = PasswordMismatchMessage;
            return false;
        }

        var mode = NormalizeMode(selectedMode);
        settings = string.IsNullOrEmpty(passwordText)
            ? new ProtectionSettings(mode)
            : ProtectionPasswordHelper.CreateWithPassword(mode, passwordText);
        validationMessage = null;
        return true;
    }

    public static bool TryCreateStopSettings(
        ProtectionSettings current,
        string? password,
        out ProtectionSettings settings,
        out string? validationMessage)
    {
        if (current.HasPassword && !ProtectionPasswordHelper.VerifyPassword(current, password ?? string.Empty))
        {
            settings = current;
            validationMessage = IncorrectPasswordMessage;
            return false;
        }

        settings = ProtectionSettings.Unprotected;
        validationMessage = null;
        return true;
    }

    public static string BuildStatusText(ProtectionSettings current)
    {
        if (!current.IsProtected)
            return "Protection is not enforced.";

        var modeLabel = ModeOptions[FindModeIndex(current.Mode)].Label;
        return current.HasPassword
            ? $"Protection is enforced: {modeLabel}. A password is required to stop protection."
            : $"Protection is enforced: {modeLabel}.";
    }
}

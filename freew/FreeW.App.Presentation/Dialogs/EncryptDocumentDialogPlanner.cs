using System.Globalization;
using Free.Shared.Opc;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum EncryptDocumentDecisionKind
{
    /// <summary>The input cannot be accepted; <see cref="EncryptDocumentDecision.Error"/> says why.</summary>
    Invalid,

    /// <summary>Encrypt the document with <see cref="EncryptDocumentDecision.Password"/> from the next save on.</summary>
    SetPassword,

    /// <summary>Stop encrypting the document (the password box was left empty).</summary>
    RemovePassword,
}

public sealed record EncryptDocumentDecision(
    EncryptDocumentDecisionKind Kind,
    string? Password = null,
    string? Error = null);

/// <summary>
/// Renderer-neutral rules for File &gt; Info &gt; Protect Document &gt; Encrypt with Password and for
/// the password prompt shown when an encrypted document is opened. Hosts own only the native boxes.
/// </summary>
public static class EncryptDocumentDialogPlanner
{
    public const string WindowAutomationId = "EncryptDocumentDialog";
    public const string PasswordAutomationId = "EncryptDocumentPasswordBox";
    public const string ConfirmationAutomationId = "EncryptDocumentConfirmPasswordBox";
    public const string ErrorAutomationId = "EncryptDocumentError";

    /// <summary>
    /// An empty password removes encryption (Word's behaviour); otherwise the password must be
    /// re-entered identically and fit Word's 255-character limit.
    /// </summary>
    public static EncryptDocumentDecision Plan(string? password, string? confirmation, BackstageInfoSafetyText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        password ??= string.Empty;
        confirmation ??= string.Empty;

        if (password.Length == 0)
            return new(EncryptDocumentDecisionKind.RemovePassword);

        if (password.Length > OoxmlPackageEncryption.MaxPasswordLength)
        {
            return new(
                EncryptDocumentDecisionKind.Invalid,
                Error: string.Format(
                    CultureInfo.CurrentCulture,
                    text.EncryptDialogTooLongFormat,
                    OoxmlPackageEncryption.MaxPasswordLength));
        }

        return string.Equals(password, confirmation, StringComparison.Ordinal)
            ? new(EncryptDocumentDecisionKind.SetPassword, password)
            : new(EncryptDocumentDecisionKind.Invalid, Error: text.EncryptDialogMismatch);
    }

    /// <summary>
    /// Applies an accepted decision to <paramref name="document"/>. Returns whether the document's
    /// encryption state changed (so the host marks it dirty). Not an undoable edit, as in Word: the
    /// password is file-level state, not document content.
    /// </summary>
    public static bool Apply(TextDocument document, EncryptDocumentDecision decision)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(decision);

        var next = decision.Kind switch
        {
            EncryptDocumentDecisionKind.SetPassword => decision.Password,
            EncryptDocumentDecisionKind.RemovePassword => null,
            _ => document.EncryptionPassword,
        };
        if (string.IsNullOrEmpty(next))
            next = null;

        if (string.Equals(document.EncryptionPassword, next, StringComparison.Ordinal))
            return false;

        document.EncryptionPassword = next;
        return true;
    }

    /// <summary>The prompt for opening an encrypted document; says so when the last attempt was wrong.</summary>
    public static string FormatOpenPrompt(DocumentPasswordRequest request, BackstageInfoSafetyText text)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(text);

        return string.Format(
            CultureInfo.CurrentCulture,
            request.PreviousPasswordWasIncorrect ? text.OpenPasswordRetryPromptFormat : text.OpenPasswordPromptFormat,
            request.FileName);
    }
}

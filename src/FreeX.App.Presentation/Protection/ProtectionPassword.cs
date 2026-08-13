namespace FreeX.App.Presentation.Protection;

/// <summary>
/// Portable validation for the password / confirm-password pair shared by the Protect Sheet and
/// Protect Workbook dialogs. This deals only with presence and confirm-match — it performs no
/// hashing, comparison against a stored secret, or any other cryptography. Turning a plaintext
/// entry into the stored representation is the Core model's responsibility.
/// </summary>
public static class ProtectionPassword
{
    /// <summary>Normalizes an absent or empty entry to null before command composition.</summary>
    public static string? Normalize(string? password) => IsSet(password) ? password : null;

    /// <summary>True when a non-empty password has been entered.</summary>
    public static bool IsSet(string? password) => !string.IsNullOrEmpty(password);

    /// <summary>
    /// True when the confirmation entry matches the original. An empty/absent password needs no
    /// confirmation and is always considered matching; a set password matches only when the
    /// confirmation is byte-for-byte identical.
    /// </summary>
    public static bool ConfirmationMatches(string? password, string? confirmation) =>
        string.Equals(password ?? "", confirmation ?? "", StringComparison.Ordinal);

    /// <summary>
    /// Validates the password/confirm pair for a Protect action, returning a structured result the
    /// dialog can use to enable its accept button or surface a mismatch message.
    /// </summary>
    public static ProtectionPasswordValidation Validate(string? password, string? confirmation)
    {
        var isSet = IsSet(password);
        if (!isSet)
            return ProtectionPasswordValidation.Valid(isSet: false);

        return ConfirmationMatches(password, confirmation)
            ? ProtectionPasswordValidation.Valid(isSet: true)
            : ProtectionPasswordValidation.Mismatch();
    }
}

/// <summary>
/// Outcome of validating a protect dialog's password/confirm pair.
/// </summary>
/// <param name="IsValid">True when the dialog may proceed.</param>
/// <param name="IsPasswordSet">True when a non-empty password was supplied.</param>
/// <param name="ConfirmationMismatch">True when a password was supplied but the confirmation differs.</param>
public sealed record ProtectionPasswordValidation(bool IsValid, bool IsPasswordSet, bool ConfirmationMismatch)
{
    internal static ProtectionPasswordValidation Valid(bool isSet) =>
        new(IsValid: true, IsPasswordSet: isSet, ConfirmationMismatch: false);

    internal static ProtectionPasswordValidation Mismatch() =>
        new(IsValid: false, IsPasswordSet: true, ConfirmationMismatch: true);
}

using FreeX.Core.Model;

namespace FreeX.App.Presentation.Protection;

/// <summary>
/// Portable, round-trippable backing model for the Protect Sheet dialog.
/// </summary>
/// <remarks>
/// It carries exactly what the dialog edits: the optional password (with confirmation, for
/// presence/match validation only — never hashed here) and the set of allowed actions that remain
/// available while the sheet is protected. The enabled actions map directly onto the Core
/// <see cref="Sheet.ProtectionPermissions"/> list (the list stores the actions a protected sheet
/// still permits), so <see cref="ToCorePermissions"/> / <see cref="FromCorePermissions"/> round-trip
/// without loss for every modelled toggle.
/// </remarks>
public sealed record ProtectSheetOptions
{
    /// <summary>The plaintext password entered, or null/empty when none is set.</summary>
    public string? Password { get; init; }

    /// <summary>The confirmation entry, used only for presence/match validation.</summary>
    public string? PasswordConfirmation { get; init; }

    /// <summary>
    /// The allowed actions that remain available while the sheet is protected, in dialog order.
    /// </summary>
    public IReadOnlyList<SheetProtectionPermission> EnabledPermissions { get; init; } = [];

    /// <summary>True when a non-empty password is set.</summary>
    public bool HasPassword => ProtectionPassword.IsSet(Password);

    /// <summary>True when a given action is checked.</summary>
    public bool IsEnabled(SheetProtectionPermission permission) => EnabledPermissions.Contains(permission);

    /// <summary>Validates the password/confirm pair for this protect action.</summary>
    public ProtectionPasswordValidation ValidatePassword() =>
        ProtectionPassword.Validate(Password, PasswordConfirmation);

    /// <summary>
    /// The default dialog state for an unprotected sheet: no password and only the two
    /// "Select" toggles enabled.
    /// </summary>
    public static ProtectSheetOptions Default { get; } = new()
    {
        EnabledPermissions = SheetProtectionOptions.DefaultEnabledPermissions,
    };

    /// <summary>
    /// Builds the dialog state from a set of checked actions, normalising them into dialog order and
    /// dropping any duplicates or undefined values.
    /// </summary>
    public static ProtectSheetOptions FromCorePermissions(
        IEnumerable<SheetProtectionPermission> permissions,
        string? password = null,
        string? passwordConfirmation = null) => new()
    {
        Password = password,
        PasswordConfirmation = passwordConfirmation,
        EnabledPermissions = NormaliseOrder(permissions),
    };

    /// <summary>
    /// Projects the checked actions onto the Core permission set the protect command consumes,
    /// emitting them in canonical dialog order with duplicates and undefined values removed.
    /// </summary>
    public IReadOnlyList<SheetProtectionPermission> ToCorePermissions() => NormaliseOrder(EnabledPermissions);

    /// <summary>
    /// The password value to store on <see cref="Sheet.ProtectionPassword"/>: null when none is set,
    /// otherwise the plaintext entry (the Core model owns turning it into its stored form).
    /// </summary>
    public string? ToCorePassword() => HasPassword ? Password : null;

    private static IReadOnlyList<SheetProtectionPermission> NormaliseOrder(
        IEnumerable<SheetProtectionPermission> permissions)
    {
        var selected = permissions.Where(Enum.IsDefined).ToHashSet();
        return SheetProtectionOptions.OrderedPermissions.Where(selected.Contains).ToList();
    }
}

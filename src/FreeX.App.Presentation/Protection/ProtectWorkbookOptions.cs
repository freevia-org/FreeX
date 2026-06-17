using FreeX.Core.Model;

namespace FreeX.App.Presentation.Protection;

/// <summary>
/// Portable backing model for the Protect Workbook dialog: the structure/windows toggles plus the
/// optional password (presence/confirm validation only — no hashing here).
/// </summary>
/// <remarks>
/// The dialog exposes two checkboxes, "Structure" and "Windows". The Core model persists structure
/// protection via <see cref="Workbook.IsStructureProtected"/> /
/// <see cref="Workbook.StructureProtectionPassword"/>; it has no field for window-layout protection,
/// so <see cref="ProtectWindows"/> is carried for dialog fidelity but is NOT round-tripped through
/// Core (see <see cref="ToCoreStructureProtected"/>). Structure defaults to checked, matching the
/// dialog's out-of-the-box state.
/// </remarks>
public sealed record ProtectWorkbookOptions
{
    /// <summary>Whether workbook structure (add/delete/rename/move sheets) is protected.</summary>
    public bool ProtectStructure { get; init; } = true;

    /// <summary>
    /// Whether the workbook window layout is protected. Carried for dialog fidelity only; the Core
    /// model does not persist this flag.
    /// </summary>
    public bool ProtectWindows { get; init; }

    /// <summary>The plaintext password entered, or null/empty when none is set.</summary>
    public string? Password { get; init; }

    /// <summary>The confirmation entry, used only for presence/match validation.</summary>
    public string? PasswordConfirmation { get; init; }

    /// <summary>True when a non-empty password is set.</summary>
    public bool HasPassword => ProtectionPassword.IsSet(Password);

    /// <summary>Validates the password/confirm pair for this protect action.</summary>
    public ProtectionPasswordValidation ValidatePassword() =>
        ProtectionPassword.Validate(Password, PasswordConfirmation);

    /// <summary>The default dialog state: structure protected, windows unprotected, no password.</summary>
    public static ProtectWorkbookOptions Default { get; } = new();

    /// <summary>
    /// The value to store on <see cref="Workbook.IsStructureProtected"/>. Window protection is not
    /// modelled by Core, so only the structure toggle survives the projection.
    /// </summary>
    public bool ToCoreStructureProtected() => ProtectStructure;

    /// <summary>
    /// The password value to store on <see cref="Workbook.StructureProtectionPassword"/>: null when
    /// none is set, otherwise the plaintext entry (Core owns turning it into its stored form).
    /// </summary>
    public string? ToCorePassword() => HasPassword ? Password : null;

    /// <summary>
    /// Rebuilds the dialog state from the Core structure-protection flag. Window protection cannot be
    /// recovered (Core does not persist it) and is left at its default.
    /// </summary>
    public static ProtectWorkbookOptions FromCore(
        bool structureProtected,
        string? password = null,
        string? passwordConfirmation = null) => new()
    {
        ProtectStructure = structureProtected,
        Password = password,
        PasswordConfirmation = passwordConfirmation,
    };
}

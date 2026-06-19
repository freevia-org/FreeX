namespace Free.Shared.AppServices;

/// <summary>How aggressively an app claims a file extension.</summary>
public enum AssociationOwnership
{
    /// <summary>The app becomes the default handler (only for types nobody else owns).</summary>
    Default,
    /// <summary>The app is added to the "Open with" list but the existing default handler is preserved.</summary>
    OpenWith,
}

/// <summary>
/// One file type an app can handle, and how it should be registered. App-neutral: each app
/// (FreeX .fxl, FreeP .fxp, FreeW .docx) supplies its own list of definitions and the shared
/// registrar performs the OS work.
/// </summary>
public sealed record FileAssociationDefinition(
    string Extension,
    string ProgId,
    string FriendlyName,
    AssociationOwnership Ownership);

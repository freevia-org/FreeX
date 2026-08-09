namespace FreeX.App.Presentation.DefinedNames;

/// <summary>
/// Portable localization descriptor for a defined-name validation failure.
/// Renderers resolve <see cref="ResourceKey"/> through their localization facade and may use
/// <see cref="FallbackText"/> when no localized value is available.
/// </summary>
public readonly record struct DefinedNameValidationMessage(
    DefinedNameError Error,
    string ResourceKey,
    string FallbackText)
{
    public string Resolve(Func<string, string> textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        var resolved = textProvider(ResourceKey);
        return string.IsNullOrWhiteSpace(resolved) ? FallbackText : resolved;
    }
}

/// <summary>
/// Single source of defined-name validation message and localization policy for every renderer.
/// </summary>
public static class DefinedNameValidationMessages
{
    public static DefinedNameValidationMessage Describe(DefinedNameError error) => error switch
    {
        DefinedNameError.Blank => Message(
            error,
            "NamedRange_NameRequiredMessage",
            "Please enter a name."),
        DefinedNameError.TooLong => Message(
            error,
            "InsertLoc_NameErrorTooLong",
            "The name is too long (255 characters maximum)."),
        DefinedNameError.InvalidFirstCharacter => Message(
            error,
            "InsertLoc_NameErrorInvalidFirstChar",
            "A name must start with a letter, underscore, or backslash."),
        DefinedNameError.InvalidCharacter => Message(
            error,
            "InsertLoc_NameErrorInvalidChar",
            "A name may contain only letters, digits, periods, and underscores (no spaces)."),
        DefinedNameError.LooksLikeReference => Message(
            error,
            "InsertLoc_NameErrorLooksLikeReference",
            "A name cannot look like a cell reference."),
        DefinedNameError.Reserved => Message(
            error,
            "InsertLoc_NameErrorReserved",
            "That name is reserved."),
        DefinedNameError.Duplicate => Message(
            error,
            "InsertLoc_NameErrorDuplicate",
            "A name with that text already exists in this scope."),
        _ => Message(
            error,
            "InsertLoc_NameErrorGeneric",
            "Enter a valid name."),
    };

    private static DefinedNameValidationMessage Message(
        DefinedNameError error,
        string resourceKey,
        string fallbackText) =>
        new(error, resourceKey, fallbackText);
}

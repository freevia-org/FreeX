namespace FreeX.App.Presentation.DefinedNames;

/// <summary>
/// Portable localization descriptor for a typed validation failure.
/// Renderers resolve <see cref="ResourceKey"/> through their localization facade and may use
/// <see cref="FallbackText"/> when no localized value is available.
/// </summary>
public readonly record struct LocalizedValidationMessage<TError>(
    TError Error,
    string ResourceKey,
    string FallbackText)
    where TError : struct, Enum
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
    public static LocalizedValidationMessage<DefinedNameError> Describe(DefinedNameError error) => error switch
    {
        DefinedNameError.Blank => new(
            error,
            "NamedRange_NameRequiredMessage",
            "Please enter a name."),
        DefinedNameError.TooLong => new(
            error,
            "InsertLoc_NameErrorTooLong",
            "The name is too long (255 characters maximum)."),
        DefinedNameError.InvalidFirstCharacter => new(
            error,
            "InsertLoc_NameErrorInvalidFirstChar",
            "A name must start with a letter, underscore, or backslash."),
        DefinedNameError.InvalidCharacter => new(
            error,
            "InsertLoc_NameErrorInvalidChar",
            "A name may contain only letters, digits, periods, and underscores (no spaces)."),
        DefinedNameError.LooksLikeReference => new(
            error,
            "InsertLoc_NameErrorLooksLikeReference",
            "A name cannot look like a cell reference."),
        DefinedNameError.Reserved => new(
            error,
            "InsertLoc_NameErrorReserved",
            "That name is reserved."),
        DefinedNameError.Duplicate => new(
            error,
            "InsertLoc_NameErrorDuplicate",
            "A name with that text already exists in this scope."),
        _ => new(
            error,
            "InsertLoc_NameErrorGeneric",
            "Enter a valid name."),
    };
}

/// <summary>
/// Single source of Refers To validation message and localization policy for every renderer.
/// </summary>
public static class RefersToValidationMessages
{
    public static LocalizedValidationMessage<RefersToError> Describe(RefersToError error) => error switch
    {
        RefersToError.Blank => new(
            error,
            "InsertLoc_RefersToErrorBlank",
            "Enter a Refers To expression."),
        RefersToError.NotAFormula => new(
            error,
            "InsertLoc_RefersToErrorNotAFormula",
            "Refers To must be a valid formula or reference."),
        _ => new(
            error,
            "InsertLoc_EnterValidRefersTo",
            "Enter a valid Refers To expression."),
    };
}

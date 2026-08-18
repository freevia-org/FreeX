using Free.Shared.AppServices;

namespace FreeX.App.Presentation.DefinedNames;

/// <summary>
/// Associates a typed validation failure with its shared localization descriptor.
/// </summary>
public readonly record struct LocalizedValidationMessage<TError>(
    TError Error,
    ResourceTextDescriptor Text)
    where TError : struct, Enum
{
    public string Resolve(Func<string, string> textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        return Text.Resolve(textProvider);
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
            new ResourceTextDescriptor(
                "NamedRange_NameRequiredMessage",
                "Please enter a name.")),
        DefinedNameError.TooLong => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_NameErrorTooLong",
                "The name is too long (255 characters maximum).")),
        DefinedNameError.InvalidFirstCharacter => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_NameErrorInvalidFirstChar",
                "A name must start with a letter, underscore, or backslash.")),
        DefinedNameError.InvalidCharacter => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_NameErrorInvalidChar",
                "A name may contain only letters, digits, periods, and underscores (no spaces).")),
        DefinedNameError.LooksLikeReference => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_NameErrorLooksLikeReference",
                "A name cannot look like a cell reference.")),
        DefinedNameError.Reserved => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_NameErrorReserved",
                "That name is reserved.")),
        DefinedNameError.ReservedPrefix => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_NameErrorReserved",
                "That name is reserved.")),
        DefinedNameError.Duplicate => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_NameErrorDuplicate",
                "A name with that text already exists in this scope.")),
        _ => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_NameErrorGeneric",
                "Enter a valid name.")),
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
            new ResourceTextDescriptor(
                "InsertLoc_RefersToErrorBlank",
                "Enter a Refers To expression.")),
        RefersToError.NotAFormula => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_RefersToErrorNotAFormula",
                "Refers To must be a valid formula or reference.")),
        _ => new(
            error,
            new ResourceTextDescriptor(
                "InsertLoc_EnterValidRefersTo",
                "Enter a valid Refers To expression.")),
    };
}

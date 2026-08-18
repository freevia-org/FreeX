using FreeX.Core.Model;

namespace FreeX.App.Presentation.DefinedNames;

/// <summary>
/// Specific reasons a defined-name string can be rejected. <see cref="None"/> means the name is valid.
/// </summary>
public enum DefinedNameError
{
    /// <summary>The name passed every rule.</summary>
    None = 0,

    /// <summary>The name was blank or whitespace only.</summary>
    Blank,

    /// <summary>The name exceeded 255 characters.</summary>
    TooLong,

    /// <summary>The first character was not a letter, underscore, or backslash.</summary>
    InvalidFirstCharacter,

    /// <summary>A character after the first was not a letter, digit, period, or underscore (this includes spaces).</summary>
    InvalidCharacter,

    /// <summary>The name looks like an A1 or R1C1 cell reference.</summary>
    LooksLikeReference,

    /// <summary>The name is a reserved single-letter column macro token (C, c, R, r).</summary>
    Reserved,

    /// <summary>The name starts with the "_xlnm." or "_xlchart." prefix Excel reserves for its own built-in defined names.</summary>
    ReservedPrefix,

    /// <summary>Another name in the same scope already uses this text (case-insensitive).</summary>
    Duplicate
}

/// <summary>Outcome of validating a single defined-name string.</summary>
public readonly record struct DefinedNameValidationResult(DefinedNameError Error)
{
    /// <summary>True when the name passed every rule.</summary>
    public bool IsValid => Error == DefinedNameError.None;

    /// <summary>A valid result singleton.</summary>
    public static DefinedNameValidationResult Valid { get; } = new(DefinedNameError.None);

    /// <summary>Build a failing result for the supplied error.</summary>
    public static DefinedNameValidationResult Fail(DefinedNameError error) => new(error);
}

/// <summary>
/// Portable validator for defined-name (named-range) text. It mirrors the rules the desktop hosts apply
/// through the Core workbook model: the first character must be a letter, underscore, or backslash
/// (backslash is allowed because Excel uses it to start macro/XLM-sheet defined names);
/// subsequent characters must be letters, digits, periods, or underscores (so spaces are rejected);
/// the name may not look like an A1 or R1C1 cell reference; the single-letter macro tokens R and C are
/// reserved; a name may not start with the "_xlnm." or "_xlchart." prefix Excel reserves for its own
/// built-in defined names (matching Workbook.ValidateNamedRangeName's HasReservedExcelPrefix check —
/// without this the live Name Manager/Define Name dialogs would accept a name like "_xlnm.Foo" only
/// for the command layer to reject it on Save with no matching live feedback); the length may not
/// exceed 255; and, within a scope, names are unique case-insensitively.
/// Pure data in, pure data out — it touches no renderer or host types.
/// </summary>
public static class DefinedNameValidator
{
    /// <summary>The maximum length Excel allows for a defined name.</summary>
    public const int MaxNameLength = 255;

    /// <summary>
    /// Validate <paramref name="name"/> against the structural rules only (no scope/uniqueness check).
    /// The name is validated as supplied; callers that allow surrounding whitespace should trim first.
    /// </summary>
    public static DefinedNameValidationResult Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DefinedNameValidationResult.Fail(DefinedNameError.Blank);

        if (name.Length > MaxNameLength)
            return DefinedNameValidationResult.Fail(DefinedNameError.TooLong);

        if (!IsValidStart(name[0]))
            return DefinedNameValidationResult.Fail(DefinedNameError.InvalidFirstCharacter);

        for (var i = 1; i < name.Length; i++)
        {
            if (!IsValidBodyCharacter(name[i]))
                return DefinedNameValidationResult.Fail(DefinedNameError.InvalidCharacter);
        }

        if (IsReservedToken(name))
            return DefinedNameValidationResult.Fail(DefinedNameError.Reserved);

        if (HasReservedExcelPrefix(name))
            return DefinedNameValidationResult.Fail(DefinedNameError.ReservedPrefix);

        if (LooksLikeReference(name))
            return DefinedNameValidationResult.Fail(DefinedNameError.LooksLikeReference);

        return DefinedNameValidationResult.Valid;
    }

    /// <summary>
    /// Validate <paramref name="name"/> structurally and then check it is not already used by another name
    /// within the same scope. <paramref name="existingNamesInScope"/> are the names already defined in the
    /// target scope; <paramref name="originalName"/> (when supplied) is excluded from the duplicate check so
    /// editing a name in place does not collide with itself.
    /// </summary>
    public static DefinedNameValidationResult Validate(
        string? name,
        IEnumerable<string> existingNamesInScope,
        string? originalName = null)
    {
        var structural = Validate(name);
        if (!structural.IsValid)
            return structural;

        foreach (var existing in existingNamesInScope)
        {
            if (originalName is not null
                && string.Equals(existing, originalName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                return DefinedNameValidationResult.Fail(DefinedNameError.Duplicate);
        }

        return DefinedNameValidationResult.Valid;
    }

    private static bool IsValidStart(char ch) =>
        char.IsLetter(ch) || ch == '_' || ch == '\\';

    private static bool IsValidBodyCharacter(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '.';

    private static bool IsReservedToken(string name) =>
        name.Length == 1 && (name[0] is 'R' or 'r' or 'C' or 'c');

    /// <summary>Matches Workbook.ValidateNamedRangeName's HasReservedExcelPrefix check (Workbook.cs).</summary>
    private static bool HasReservedExcelPrefix(string name) =>
        name.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeReference(string name) =>
        CellAddress.TryParse(name, SheetId.New(), out _) || IsR1C1Reference(name);

    private static bool IsR1C1Reference(string name)
    {
        if (name.Length < 4 || char.ToUpperInvariant(name[0]) != 'R')
            return false;

        var cIndex = name.IndexOf("C", 1, StringComparison.OrdinalIgnoreCase);
        if (cIndex <= 1 || cIndex == name.Length - 1)
            return false;

        return uint.TryParse(name[1..cIndex], out var row)
            && uint.TryParse(name[(cIndex + 1)..], out var col)
            && row is >= 1 and <= CellAddress.MaxRow
            && col is >= 1 and <= CellAddress.MaxCol;
    }
}

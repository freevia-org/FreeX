using FreeX.Core.Formula;

namespace FreeX.App.Presentation.DefinedNames;

/// <summary>Specific reasons a draft's refers-to text can be rejected. <see cref="None"/> means valid.</summary>
public enum RefersToError
{
    /// <summary>The refers-to text passed validation.</summary>
    None = 0,

    /// <summary>The refers-to text was blank or whitespace only.</summary>
    Blank,

    /// <summary>The refers-to text was not a parseable formula/reference expression.</summary>
    NotAFormula
}

/// <summary>
/// An in-progress defined name as edited in a Define Name dialog: the name text, its target scope, the
/// "refers to" expression, and an optional comment. This is a portable carrier — validation of its parts is
/// performed by <see cref="DefinedNameValidator"/> and <see cref="ValidateRefersTo"/>; no renderer or host
/// types are involved.
/// </summary>
public sealed record DefinedNameDraft(
    string Name,
    DefinedNameScope Scope,
    string RefersTo,
    string Comment = "")
{
    /// <summary>Outcome of validating a draft's <see cref="RefersTo"/> expression.</summary>
    public readonly record struct RefersToValidationResult(RefersToError Error)
    {
        /// <summary>True when the refers-to text passed validation.</summary>
        public bool IsValid => Error == RefersToError.None;

        /// <summary>A valid result singleton.</summary>
        public static RefersToValidationResult Valid { get; } = new(RefersToError.None);

        /// <summary>Build a failing result for the supplied error.</summary>
        public static RefersToValidationResult Fail(RefersToError error) => new(error);
    }

    /// <summary>
    /// Validate <paramref name="refersTo"/> as a formula/reference expression. A leading '=' is optional and
    /// ignored. The check is best-effort: it runs the Core formula tokenizer and parser and accepts any text
    /// that parses cleanly; anything that fails to parse (or is blank) is rejected. This mirrors the desktop
    /// hosts treating the refers-to field as a formula expression rather than a fixed cell-range syntax.
    /// </summary>
    public static RefersToValidationResult ValidateRefersTo(string? refersTo)
    {
        if (string.IsNullOrWhiteSpace(refersTo))
            return RefersToValidationResult.Fail(RefersToError.Blank);

        try
        {
            var tokens = new Lexer(refersTo).Tokenize();
            _ = new Parser(tokens).Parse();
            return RefersToValidationResult.Valid;
        }
        catch (Exception ex) when (ex is FormulaParseException or FormatException or ArgumentException or InvalidOperationException)
        {
            return RefersToValidationResult.Fail(RefersToError.NotAFormula);
        }
    }

    /// <summary>Validate this draft's <see cref="RefersTo"/> expression.</summary>
    public RefersToValidationResult ValidateRefersTo() => ValidateRefersTo(RefersTo);
}

namespace Freexcel.Core.Formula;

/// <summary>All token types recognized by the formula lexer.</summary>
public enum TokenType
{
    // Literals
    Number,
    String,
    Boolean,
    Error,

    // References
    CellRef,       // e.g. A1, $B$2
    RangeRef,      // e.g. A1:B5 (only produced when colon separates two CellRefs)
    SheetQualifier, // e.g. "Sheet2" in Sheet2!A1 (includes the sheet name, ! already consumed)
    StructuredReferenceSelector, // e.g. "Amount" in Sales[Amount]

    // Identifiers
    FunctionName,  // e.g. SUM, IF
    NamedRange,    // e.g. MyData (identifier that is not a cell reference)

    // Operators
    Plus,
    Minus,
    Multiply,
    Divide,
    Power,
    Ampersand,     // & (string concatenation)
    Percent,       // % (divide by 100)

    // Comparison
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessOrEqual,
    GreaterOrEqual,

    // Delimiters
    OpenParen,
    CloseParen,
    Comma,
    Colon,

    // Special
    EndOfFormula
}

/// <summary>A single token produced by the lexer.</summary>
public sealed record Token(TokenType Type, string Value, int Position);

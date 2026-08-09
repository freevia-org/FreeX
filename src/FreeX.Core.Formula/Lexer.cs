using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

/// <summary>
/// Tokenizes a formula string into a stream of tokens.
/// Handles numbers, strings, cell references, operators, and function names.
/// </summary>
public sealed class Lexer
{
    private static readonly object TokenCacheGate = new();
    private static readonly Dictionary<string, Token[]> TokenCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> TokenCacheOrder = new();

    private static readonly string[] KnownErrors =
        [
            "#DIV/0!",
            "#VALUE!",
            "#REF!",
            "#NAME?",
            "#NULL!",
            "#N/A",
            "#NUM!",
            "#SPILL!",
            "#CONNECT!",
            "#BLOCKED!",
            "#UNKNOWN!",
            "#FIELD!",
            "#CALC!",
            "#GETTING_DATA"
        ];

    static Lexer()
    {
        // Sort once so ReadErrorLiteral can match longest first without re-sorting
        Array.Sort(KnownErrors, (a, b) => b.Length.CompareTo(a.Length));
    }

    private readonly string _text;
    private int _pos;

    public Lexer(string formulaText)
    {
        // Strip leading '=' if present
        _text = formulaText.StartsWith('=') ? formulaText[1..] : formulaText;
        _pos = 0;
    }

    /// <summary>Tokenize the entire formula into a list of tokens.</summary>
    public List<Token> Tokenize()
    {
        var startPosition = _pos;
        if (startPosition == 0 && TryGetCachedTokens(_text, out var cachedTokens))
        {
            _pos = _text.Length;
            return new List<Token>(cachedTokens);
        }

        var tokens = new List<Token>(Math.Min(_text.Length + 1, FormulaSafetyLimits.MaxParseTokens + 1));
        var precededByWhitespace = new List<bool>(tokens.Capacity);

        while (_pos < _text.Length)
        {
            var posBeforeSkip = _pos;
            SkipWhitespace();
            if (_pos >= _text.Length)
                break;

            var hadWhitespace = _pos > posBeforeSkip;
            var token = ReadNextToken();
            tokens.Add(token);
            precededByWhitespace.Add(hadWhitespace);

            if (tokens.Count > FormulaSafetyLimits.MaxParseTokens)
                throw new FormulaParseException(
                    $"Formula contains too many tokens; maximum is {FormulaSafetyLimits.MaxParseTokens}");
        }

        tokens.Add(new Token(TokenType.EndOfFormula, "", _pos));
        precededByWhitespace.Add(false);

        InsertIntersectionTokens(tokens, precededByWhitespace);

        if (startPosition == 0)
            AddCachedTokens(_text, tokens);

        return tokens;
    }

    /// <summary>
    /// Excel's explicit INTERSECTION reference operator is written as a plain space directly
    /// between two reference operands (e.g. the space in <c>A1:C3 B2:D4</c>) -- there is no
    /// dedicated character for it, so recognizing it means noticing where whitespace was actually
    /// skipped between two tokens that are each reference-shaped on their own (a bare cell
    /// reference or a defined name -- the same raw token that ends the first operand's range/name
    /// and starts the second's, regardless of how much of a range/name each one spans). Everywhere
    /// else a space appears in a formula -- around an operator, after a comma, inside/around
    /// parens -- at least one side of the gap is some other token kind (Comma, an operator,
    /// OpenParen/CloseParen, ...), so this insertion is a no-op there and every pre-existing
    /// whitespace-tolerant shape (e.g. <c>SUM( A1 , B1 )</c>) is completely unaffected. Runs once,
    /// after the whole formula is tokenized, since it only needs each adjacent pair's token kinds
    /// and whether whitespace separated them -- no parser-level context is required.
    /// </summary>
    private static void InsertIntersectionTokens(List<Token> tokens, List<bool> precededByWhitespace)
    {
        for (var i = tokens.Count - 1; i >= 1; i--)
        {
            if (precededByWhitespace[i] &&
                IsIntersectionOperandToken(tokens[i - 1].Type) &&
                IsIntersectionOperandToken(tokens[i].Type))
            {
                tokens.Insert(i, new Token(TokenType.Intersection, " ", tokens[i].Position));
            }
        }
    }

    /// <summary>
    /// True for the raw token kinds that can end (on the left) or start (on the right) an
    /// intersection operand: a bare cell reference or a defined-name identifier. A colon-range
    /// (A1:C3) is still just a CellRef token on each side at the lexer level (Parser combines them
    /// later), so this correctly fires on the C3 in "A1:C3 B2:D4" without needing to know the
    /// range was there at all.
    /// </summary>
    private static bool IsIntersectionOperandToken(TokenType type) =>
        type is TokenType.CellRef or TokenType.NamedRange;

    private static bool TryGetCachedTokens(string formulaText, out Token[] tokens)
    {
        lock (TokenCacheGate)
        {
            return TokenCache.TryGetValue(formulaText, out tokens!);
        }
    }

    private static void AddCachedTokens(string formulaText, List<Token> tokens)
    {
        lock (TokenCacheGate)
        {
            if (TokenCache.ContainsKey(formulaText))
                return;

            if (TokenCache.Count >= FormulaSafetyLimits.MaxTokenizedFormulaCacheEntries &&
                TokenCacheOrder.TryDequeue(out var oldest))
            {
                TokenCache.Remove(oldest);
            }

            TokenCache[formulaText] = tokens.ToArray();
            TokenCacheOrder.Enqueue(formulaText);
        }
    }

    private Token ReadNextToken()
    {
        var c = _text[_pos];

        return c switch
        {
            '\'' => ReadQuotedSheetQualifier(),
            '"' => ReadString(),
            '#' => ReadErrorLiteral(),
            '+' => SingleChar(TokenType.Plus),
            '-' => SingleChar(TokenType.Minus),
            '*' => SingleChar(TokenType.Multiply),
            '/' => SingleChar(TokenType.Divide),
            '^' => SingleChar(TokenType.Power),
            '&' => SingleChar(TokenType.Ampersand),
            '%' => SingleChar(TokenType.Percent),
            '@' => SingleChar(TokenType.ImplicitIntersection),
            '(' => SingleChar(TokenType.OpenParen),
            ')' => SingleChar(TokenType.CloseParen),
            '{' => SingleChar(TokenType.OpenBrace),
            '}' => SingleChar(TokenType.CloseBrace),
            '[' => ReadBracketToken(),
            ',' => SingleChar(TokenType.Comma),
            ';' => SingleChar(TokenType.Semicolon),
            ':' => SingleChar(TokenType.Colon),
            '=' => SingleChar(TokenType.Equal),
            '<' => ReadLessThanOrComposite(),
            '>' => ReadGreaterThanOrComposite(),
            _ when char.IsDigit(c) || c == '.' => ReadNumber(),
            // The non-'$' half of this guard is exactly SheetNameFormatter.IsUnquotedSheetNameChar
            // restricted to non-digit/non-period characters (both already routed to ReadNumber
            // above) -- i.e. "letter or underscore". Reusing the shared predicate here (rather than
            // a second hand-written char.IsLetter(c) copy) is what keeps this dispatch and
            // SheetNameFormatter.NeedsQuoting from silently drifting apart on which characters an
            // unquoted sheet-qualified reference may start with.
            _ when SheetNameFormatter.IsUnquotedSheetNameChar(c) || c == '$' => ReadIdentifierOrRef(),
            _ => throw new FormulaParseException($"Unexpected character '{c}' at position {_pos}")
        };
    }

    private Token SingleChar(TokenType type)
    {
        var token = new Token(type, SingleCharTokenValue(type), _pos);
        _pos++;
        return token;
    }

    private static string SingleCharTokenValue(TokenType type) => type switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Multiply => "*",
        TokenType.Divide => "/",
        TokenType.Power => "^",
        TokenType.Ampersand => "&",
        TokenType.Percent => "%",
        TokenType.ImplicitIntersection => "@",
        TokenType.OpenParen => "(",
        TokenType.CloseParen => ")",
        TokenType.OpenBrace => "{",
        TokenType.CloseBrace => "}",
        TokenType.Comma => ",",
        TokenType.Semicolon => ";",
        TokenType.Colon => ":",
        TokenType.Equal => "=",
        _ => ""
    };

    private Token ReadNumber()
    {
        var start = _pos;
        var hasDigit = false;
        var hasDecimal = false;
        var hasExponent = false;

        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (char.IsDigit(c))
            {
                hasDigit = true;
                _pos++;
            }
            else if (c == '.' && !hasDecimal && !hasExponent)
            {
                hasDecimal = true;
                _pos++;
            }
            else if ((c == 'e' || c == 'E') && !hasExponent)
            {
                // Only consume 'e' if at least one digit follows (optionally after a sign)
                int lookahead = _pos + 1;
                if (lookahead < _text.Length && (_text[lookahead] == '+' || _text[lookahead] == '-'))
                    lookahead++;
                if (lookahead >= _text.Length || !char.IsDigit(_text[lookahead]))
                    break;
                hasExponent = true;
                _pos++;
                if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-'))
                    _pos++;
            }
            else
            {
                break;
            }
        }

        if (!hasDigit)
            throw new FormulaParseException($"Expected number at position {start}");

        return new Token(TokenType.Number, _text[start.._pos], start);
    }

    private Token ReadString()
    {
        var start = _pos;
        _pos++; // skip opening quote
        var sb = new StringBuilder();

        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c == '"')
            {
                _pos++;
                // Excel-style escaped quote: "" inside string
                if (_pos < _text.Length && _text[_pos] == '"')
                {
                    sb.Append('"');
                    _pos++;
                }
                else
                {
                    return new Token(TokenType.String, sb.ToString(), start);
                }
            }
            else
            {
                sb.Append(c);
                _pos++;
            }
        }

        throw new FormulaParseException($"Unterminated string starting at position {start}");
    }

    /// <summary>
    /// Dispatches a leading '[' to either the unquoted on-disk external-workbook sheet-qualifier
    /// form (<c>[n]SheetName!</c>, e.g. <c>[1]Sheet1!A1</c> -- the form real Excel writes whenever
    /// the external sheet name needs no quoting of its own) or, when that shape isn't present, the
    /// ordinary structured-table-reference selector (<c>Table1[Column1]</c>, <c>[@Column1]</c>,
    /// <c>[#Totals]</c>, etc).
    /// </summary>
    private Token ReadBracketToken() =>
        TryReadExternalSheetQualifier(out var externalToken) ? externalToken : ReadStructuredReferenceSelector();

    /// <summary>
    /// Recognizes the unquoted on-disk external-workbook sheet-qualifier form <c>[n]SheetName!</c>,
    /// where <c>n</c> is the 1-based external-reference index (e.g. <c>[1]Sheet1!A1</c>), AND the
    /// sibling external-workbook DEFINED-NAME reference form <c>[n]!</c> with NO sheet segment at
    /// all (e.g. <c>[1]!TaxRate</c>, the on-disk shape for a workbook-scoped name exposed by an
    /// external link). Excel's quoted equivalents (<c>'[1]Sheet1'!A1</c> / <c>'[1]'!TaxRate</c>)
    /// already lex to a single SheetQualifier token via <see cref="ReadQuotedSheetQualifier"/> whose
    /// value is the bracketed "[1]Sheet1" (or bare "[1]") string; this produces the identical token
    /// shape/value for the unquoted forms so <see cref="ExternalSheetReferenceResolver"/> (sheet
    /// form) / <c>Parser.ParseExternalDefinedNameReference</c> (name-only form) and the rest of the
    /// parser resolve every shape the same way.
    ///
    /// Only matches when the bracket content is ALL DIGITS (the numeric external-reference index)
    /// immediately closed by ']', immediately followed by EITHER an identifier-shaped sheet name
    /// and then '!' OR '!' directly (zero-length sheet segment) -- this can never collide with a
    /// structured-table-reference selector: a structured reference selector's bracket content is a
    /// column name / '@'/'#' keyword, never immediately followed (once the bracket closes) by '!'
    /// itself or a second, unbracketed identifier and '!'. Any other shape (no digits, unterminated,
    /// no trailing '!') falls through unchanged to <see cref="ReadStructuredReferenceSelector"/>
    /// without consuming any input, so Table1[Column1], [@Column1], [#Totals], and every other
    /// structured-reference shape lex exactly as before.
    /// </summary>
    private bool TryReadExternalSheetQualifier(out Token token)
    {
        token = null!;
        var start = _pos;
        var pos = start + 1; // skip '['

        var digitsStart = pos;
        while (pos < _text.Length && char.IsDigit(_text[pos]))
            pos++;

        if (pos == digitsStart || pos >= _text.Length || _text[pos] != ']')
            return false;

        pos++; // skip ']'

        var sheetStart = pos;
        while (pos < _text.Length && (char.IsLetterOrDigit(_text[pos]) || _text[pos] == '_' || _text[pos] == '.'))
            pos++;

        // pos == sheetStart here means a zero-length sheet segment -- the "[n]!Name" external
        // defined-name shape, which is just as valid as "[n]SheetName!Ref" as long as '!' follows
        // immediately.
        if (pos >= _text.Length || _text[pos] != '!')
            return false;

        var value = _text[start..pos];
        pos++; // skip '!'

        token = new Token(TokenType.SheetQualifier, value, start);
        _pos = pos;
        return true;
    }

    private Token ReadStructuredReferenceSelector()
    {
        var start = _pos;
        _pos++; // skip opening bracket
        var selectorStart = _pos;

        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            // R12-xlsx-tables-3: a literal '[', '#', or '\'' inside a column name is escaped with a
            // leading apostrophe (e.g. [A'[B] for the literal name "A[B") — bail to the slow path,
            // which understands that escape, instead of returning this apostrophe raw.
            if (c == '[' || (c == '\'' && IsEscapableStructuredReferenceChar(_pos + 1)))
                break;

            if (c == ']')
            {
                if (_pos + 1 < _text.Length && _text[_pos + 1] == ']')
                    break;

                var token = new Token(
                    TokenType.StructuredReferenceSelector,
                    _text.AsSpan(selectorStart, _pos - selectorStart).ToString(),
                    start);
                _pos++;
                return token;
            }

            _pos++;
        }

        _pos = selectorStart;
        return ReadStructuredReferenceSelectorSlow(start);
    }

    private Token ReadStructuredReferenceSelectorSlow(int start)
    {
        var depth = 1;
        var sb = new StringBuilder();

        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            // R12-xlsx-tables-3: an apostrophe immediately before '[', ']', '#', or another
            // apostrophe escapes THAT character as a literal — this is how a column name containing
            // one of those characters round-trips through a structured reference without the
            // escaped '[' opening a (nested/combined-selector) bracket group or the escaped '#'
            // being mistaken for a "#Data"/"#Totals"/etc. section keyword. Mirrors Excel's own
            // structured-reference escaping convention. An apostrophe NOT followed by one of those
            // characters (e.g. a plain "It's") is not an escape prefix and is copied through as-is.
            if (c == '\'' && IsEscapableStructuredReferenceChar(_pos + 1))
            {
                sb.Append(_text[_pos + 1]);
                _pos += 2;
                continue;
            }

            if (c == '[')
            {
                depth++;
                sb.Append(c);
                _pos++;
                continue;
            }
            else if (c == ']')
            {
                _pos++;
                if (depth > 1)
                {
                    depth--;
                    sb.Append(c);
                    continue;
                }

                if (_pos < _text.Length && _text[_pos] == ']')
                {
                    sb.Append(']');
                    _pos++;
                    continue;
                }

                return new Token(TokenType.StructuredReferenceSelector, sb.ToString(), start);
            }

            sb.Append(c);
            _pos++;
        }

        throw new FormulaParseException($"Unterminated structured reference starting at position {start}");
    }

    /// <summary>
    /// True when <paramref name="index"/> is in range and the character there is one of the four
    /// characters a leading apostrophe can escape inside a structured reference selector: '[', ']',
    /// '#', or an apostrophe itself. Used by <see cref="ReadStructuredReferenceSelector"/> and
    /// <see cref="ReadStructuredReferenceSelectorSlow"/> to distinguish an escape-prefix apostrophe
    /// from a plain literal apostrophe (e.g. in "It's") elsewhere in a column name.
    /// </summary>
    private bool IsEscapableStructuredReferenceChar(int index) =>
        index < _text.Length && _text[index] is '[' or ']' or '#' or '\'';

    private Token ReadIdentifierOrRef()
    {
        var start = _pos;

        // Allow $ for absolute references. The non-'$' half of this condition is exactly
        // SheetNameFormatter.IsUnquotedSheetNameChar -- reusing it (rather than a second
        // hand-written char.IsLetterOrDigit(c)||c=='_'||c=='.' copy) is what keeps this scan and
        // SheetNameFormatter.NeedsQuoting from silently drifting apart on which characters an
        // unquoted sheet-qualified reference may contain (this token doubles as CellRef/NamedRange
        // scanning, hence '$' staying a local addition rather than joining the shared predicate).
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (SheetNameFormatter.IsUnquotedSheetNameChar(c) || c == '$')
            {
                _pos++;
            }
            else
            {
                break;
            }
        }

        var valueSpan = _text.AsSpan(start, _pos - start);

        if (_pos < _text.Length && _text[_pos] == '!')
        {
            _pos++;
            return new Token(TokenType.SheetQualifier, valueSpan.ToString(), start);
        }

        // Check if it's a function name (followed by open paren) — must come before boolean check
        // so that TRUE() and FALSE() are treated as zero-arg function calls.
        var lookAhead = _pos;
        while (lookAhead < _text.Length && char.IsWhiteSpace(_text[lookAhead]))
            lookAhead++;

        if (lookAhead < _text.Length && _text[lookAhead] == '(')
            return new Token(TokenType.FunctionName, NormalizeFunctionName(ToUpperInvariantIfNeeded(valueSpan)), start);

        // Check for boolean literals
        if (valueSpan.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return new Token(TokenType.Boolean, "TRUE", start);
        if (valueSpan.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return new Token(TokenType.Boolean, "FALSE", start);

        // Otherwise it's a cell reference
        if (IsCellReference(valueSpan))
            return new Token(TokenType.CellRef, ToUpperInvariantIfNeeded(valueSpan), start);

        // Named range (identifier that is not a cell reference, function, or boolean).
        //
        // Exception: when this identifier is immediately followed by ':' or '#', it may be the
        // start sheet name of a 3-D sheet-span reference (e.g. Sheet1:Sheet3!A1 — see Parser's
        // TryParseSheetSpanReference) or a named-range spill anchor (e.g. MyCell# — see Parser's
        // WrapSpillAnchor) rather than a plain named-range identifier looked up case-insensitively.
        // Named ranges preserve their defined display case (like sheet names) when round-tripping
        // through FormulaSerializer for these shapes — e.g. FormulaSerializer prints the
        // ANCHORARRAY(NamedRangeNode) case back as "<Name>#" verbatim — so skip the uppercasing
        // here and let the original source casing flow through. This also covers the
        // full-column/full-row-range start token (e.g. the "A" in A:A) for the same reason:
        // TryParseColumnToken/TryParseRowToken already re-normalize case themselves.
        var value = _pos < _text.Length && (_text[_pos] == ':' || _text[_pos] == '#')
            ? valueSpan.ToString()
            : ToUpperInvariantIfNeeded(valueSpan);
        return new Token(TokenType.NamedRange, value, start);
    }

    private Token ReadQuotedSheetQualifier()
    {
        var start = _pos;
        _pos++; // skip opening apostrophe
        var sb = new StringBuilder();

        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c == '\'')
            {
                _pos++;
                if (_pos < _text.Length && _text[_pos] == '\'')
                {
                    sb.Append('\'');
                    _pos++;
                    continue;
                }

                if (_pos < _text.Length && _text[_pos] == '!')
                {
                    _pos++;
                    return new Token(TokenType.SheetQualifier, sb.ToString(), start);
                }

                throw new FormulaParseException($"Expected '!' after quoted sheet name at position {_pos}");
            }

            sb.Append(c);
            _pos++;
        }

        throw new FormulaParseException($"Unterminated quoted sheet name starting at position {start}");
    }

    private Token ReadErrorLiteral()
    {
        var start = _pos;

        foreach (var error in KnownErrors)
        {
            if (_text.AsSpan(_pos).StartsWith(error, StringComparison.OrdinalIgnoreCase))
            {
                _pos += error.Length;
                return new Token(TokenType.Error, error, start);
            }
        }

        // A lone '#' that isn't the start of a known error literal is the A1# spill-anchor operator
        // (e.g. =A1#, =SUM(A1#)) — Excel's syntax for "the current spill range of A1". The Parser
        // only accepts this token immediately after a cell/range reference; anywhere else it's a
        // parse error there, same as before this token type existed.
        if (_pos + 1 >= _text.Length || !char.IsLetter(_text[_pos + 1]))
        {
            _pos++;
            return new Token(TokenType.Hash, "#", start);
        }

        throw new FormulaParseException($"Unknown error literal at position {start}");
    }

    /// <summary>
    /// True when <paramref name="value"/> is a well-formed cell reference (e.g. "A1", "$B$2").
    /// Internal (not private) so FormulaEvaluator.References.cs can reuse this same shape test
    /// to detect the "&lt;cellref&gt;.&lt;field&gt;" linked-data-type field-access syntax
    /// (e.g. "A1.PRICE") without duplicating the column/row validation logic — see
    /// R35-deferred-field-error-1.
    /// </summary>
    internal static bool IsCellReference(ReadOnlySpan<char> value)
    {
        int i = 0;
        if (i < value.Length && value[i] == '$')
            i++;

        int colStart = i;
        while (i < value.Length && char.IsLetter(value[i]))
            i++;

        // Column names can be at most 3 letters (A-XFD = columns 1-16384)
        var columnLength = i - colStart;
        if (columnLength is 0 or > 3 || i == value.Length)
            return false;

        int digitStart = i;
        if (i < value.Length && value[i] == '$')
        {
            i++;
            digitStart = i;
        }

        while (i < value.Length && char.IsDigit(value[i]))
            i++;

        if (i != value.Length || digitStart >= value.Length)
            return false;

        var columnName = value[colStart..(digitStart > colStart && value[digitStart - 1] == '$' ? digitStart - 1 : digitStart)];
        var columnNumber = FreeX.Core.Model.CellAddress.ColumnNameToNumber(columnName.ToString());
        if (columnNumber is 0 || columnNumber > FreeX.Core.Model.CellAddress.MaxCol)
            return false;

        return uint.TryParse(value[digitStart..], out var row) &&
               row is >= 1 and <= FreeX.Core.Model.CellAddress.MaxRow;
    }

    private static string ToUpperInvariantIfNeeded(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsLower(c))
            {
                return string.Create(value.Length, value, static (destination, source) =>
                {
                    for (var index = 0; index < source.Length; index++)
                        destination[index] = char.ToUpperInvariant(source[index]);
                });
            }
        }

        return value.ToString();
    }

    private static string NormalizeFunctionName(string name)
    {
        const string futureFunctionPrefix = "_XLFN.";
        const string worksheetFunctionPrefix = "_XLWS.";

        if (name.StartsWith(futureFunctionPrefix, StringComparison.Ordinal))
            name = name[futureFunctionPrefix.Length..];
        if (name.StartsWith(worksheetFunctionPrefix, StringComparison.Ordinal))
            name = name[worksheetFunctionPrefix.Length..];

        return name;
    }

    private Token ReadLessThanOrComposite()
    {
        var start = _pos;
        _pos++; // skip '<'

        if (_pos < _text.Length)
        {
            if (_text[_pos] == '=')
            {
                _pos++;
                return new Token(TokenType.LessOrEqual, "<=", start);
            }
            if (_text[_pos] == '>')
            {
                _pos++;
                return new Token(TokenType.NotEqual, "<>", start);
            }
        }

        return new Token(TokenType.LessThan, "<", start);
    }

    private Token ReadGreaterThanOrComposite()
    {
        var start = _pos;
        _pos++; // skip '>'

        if (_pos < _text.Length && _text[_pos] == '=')
        {
            _pos++;
            return new Token(TokenType.GreaterOrEqual, ">=", start);
        }

        return new Token(TokenType.GreaterThan, ">", start);
    }

    private void SkipWhitespace()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            _pos++;
    }
}

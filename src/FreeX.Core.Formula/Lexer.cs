using System.Text;

namespace FreeX.Core.Formula;

/// <summary>
/// Tokenizes a formula string into a stream of tokens.
/// Handles numbers, strings, cell references, operators, and function names.
/// </summary>
public sealed class Lexer
{
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
        var tokens = new List<Token>(Math.Min(_text.Length + 1, FormulaSafetyLimits.MaxParseTokens + 1));

        while (_pos < _text.Length)
        {
            SkipWhitespace();
            if (_pos >= _text.Length)
                break;

            var token = ReadNextToken();
            tokens.Add(token);
        }

        tokens.Add(new Token(TokenType.EndOfFormula, "", _pos));
        return tokens;
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
            '(' => SingleChar(TokenType.OpenParen),
            ')' => SingleChar(TokenType.CloseParen),
            '{' => SingleChar(TokenType.OpenBrace),
            '}' => SingleChar(TokenType.CloseBrace),
            '[' => ReadStructuredReferenceSelector(),
            ',' => SingleChar(TokenType.Comma),
            ';' => SingleChar(TokenType.Semicolon),
            ':' => SingleChar(TokenType.Colon),
            '=' => SingleChar(TokenType.Equal),
            '<' => ReadLessThanOrComposite(),
            '>' => ReadGreaterThanOrComposite(),
            _ when char.IsDigit(c) || c == '.' => ReadNumber(),
            _ when char.IsLetter(c) || c == '_' || c == '$' => ReadIdentifierOrRef(),
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

    private Token ReadStructuredReferenceSelector()
    {
        var start = _pos;
        _pos++; // skip opening bracket
        var selectorStart = _pos;

        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c == '[')
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

    private Token ReadIdentifierOrRef()
    {
        var start = _pos;

        // Allow $ for absolute references
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '.')
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
        while (lookAhead < _text.Length && _text[lookAhead] == ' ')
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

        // Named range (identifier that is not a cell reference, function, or boolean)
        return new Token(TokenType.NamedRange, ToUpperInvariantIfNeeded(valueSpan), start);
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

        throw new FormulaParseException($"Unknown error literal at position {start}");
    }

    private static bool IsCellReference(ReadOnlySpan<char> value)
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

        return i == value.Length && digitStart < value.Length;
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

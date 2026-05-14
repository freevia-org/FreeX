using System.Text;

namespace Freexcel.Core.Formula;

/// <summary>
/// Tokenizes a formula string into a stream of tokens.
/// Handles numbers, strings, cell references, operators, and function names.
/// </summary>
public sealed class Lexer
{
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
        var tokens = new List<Token>();

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
            '"' => ReadString(),
            '+' => SingleChar(TokenType.Plus),
            '-' => SingleChar(TokenType.Minus),
            '*' => SingleChar(TokenType.Multiply),
            '/' => SingleChar(TokenType.Divide),
            '^' => SingleChar(TokenType.Power),
            '&' => SingleChar(TokenType.Ampersand),
            '%' => SingleChar(TokenType.Percent),
            '(' => SingleChar(TokenType.OpenParen),
            ')' => SingleChar(TokenType.CloseParen),
            ',' => SingleChar(TokenType.Comma),
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
        var token = new Token(type, _text[_pos].ToString(), _pos);
        _pos++;
        return token;
    }

    private Token ReadNumber()
    {
        var start = _pos;
        var hasDecimal = false;
        var hasExponent = false;

        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (char.IsDigit(c))
            {
                _pos++;
            }
            else if (c == '.' && !hasDecimal && !hasExponent)
            {
                hasDecimal = true;
                _pos++;
            }
            else if ((c == 'e' || c == 'E') && !hasExponent)
            {
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

    private Token ReadIdentifierOrRef()
    {
        var start = _pos;
        var sb = new StringBuilder();

        // Allow $ for absolute references
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '.')
            {
                sb.Append(c);
                _pos++;
            }
            else
            {
                break;
            }
        }

        var value = sb.ToString();
        var upper = value.ToUpperInvariant();

        // Phase 2: quoted sheet names ('My Sheet'!A1) are not supported.
        // A leading '\'' triggers FormulaParseException, which surfaces as #VALUE!.
        if (_pos < _text.Length && _text[_pos] == '!')
        {
            _pos++;
            return new Token(TokenType.SheetQualifier, value, start);
        }

        // Check for boolean literals
        if (upper == "TRUE")
            return new Token(TokenType.Boolean, "TRUE", start);
        if (upper == "FALSE")
            return new Token(TokenType.Boolean, "FALSE", start);

        // Check if it's a function name (followed by open paren)
        var lookAhead = _pos;
        while (lookAhead < _text.Length && _text[lookAhead] == ' ')
            lookAhead++;

        if (lookAhead < _text.Length && _text[lookAhead] == '(')
            return new Token(TokenType.FunctionName, upper, start);

        // Otherwise it's a cell reference
        if (IsCellReference(value))
            return new Token(TokenType.CellRef, value.ToUpperInvariant(), start);

        // Named range (identifier that is not a cell reference, function, or boolean)
        return new Token(TokenType.NamedRange, upper, start);
    }

    private static bool IsCellReference(string value)
    {
        // Strip $ signs for validation
        var clean = value.Replace("$", "").ToUpperInvariant();

        var i = 0;
        // Must start with letters
        while (i < clean.Length && char.IsLetter(clean[i]))
            i++;

        if (i == 0 || i == clean.Length)
            return false;

        // Must end with digits
        while (i < clean.Length && char.IsDigit(clean[i]))
            i++;

        return i == clean.Length;
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
        while (_pos < _text.Length && _text[_pos] == ' ')
            _pos++;
    }
}

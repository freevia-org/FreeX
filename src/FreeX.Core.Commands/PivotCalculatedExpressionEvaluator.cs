using System.Globalization;

namespace FreeX.Core.Commands;

internal static class PivotCalculatedExpressionEvaluator
{
    public static double Evaluate(string formula, Func<string, double> fieldValue)
    {
        var parser = new Parser(formula, fieldValue);
        return parser.Parse();
    }

    private sealed class Parser
    {
        /// <summary>
        /// Maximum parenthesis/unary-sign nesting descended. Each level costs a stack frame, and the
        /// formula text is not bounded: a calculated field can be typed by the user or read from the
        /// pivot definition in an opened .xlsx. Without a cap, "((((…1…))))" nested deep enough
        /// overflows the stack, and StackOverflowException is uncatchable — it kills the process
        /// rather than surfacing as a bad formula. FreeW's table-formula parser caps for the same
        /// reason. Real formulas nest a handful of levels.
        /// </summary>
        private const int MaxParseDepth = 128;

        private readonly string _text;
        private readonly Func<string, double> _fieldValue;
        private int _position;
        private int _depth;

        public Parser(string text, Func<string, double> fieldValue)
        {
            _text = text ?? "";
            _fieldValue = fieldValue;
        }

        public double Parse()
        {
            var value = ParseAddSubtract();
            SkipWhitespace();
            return value;
        }

        private double ParseAddSubtract()
        {
            var value = ParseMultiplyDivide();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('+'))
                    value += ParseMultiplyDivide();
                else if (TryConsume('-'))
                    value -= ParseMultiplyDivide();
                else
                    return value;
            }
        }

        private double ParseMultiplyDivide()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('*'))
                    value *= ParseUnary();
                else if (TryConsume('/'))
                {
                    var denominator = ParseUnary();
                    value = Math.Abs(denominator) < double.Epsilon ? 0 : value / denominator;
                }
                else
                    return value;
            }
        }

        private double ParseUnary()
        {
            if (!TryEnter())
                return 0;

            try
            {
                SkipWhitespace();
                if (TryConsume('+'))
                    return ParseUnary();
                if (TryConsume('-'))
                    return -ParseUnary();
                return ParsePrimary();
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>
        /// Enters one nesting level, or abandons the parse when it is already too deep. Abandoning
        /// consumes the rest of the text so the operator loops above cannot spin on the remainder;
        /// this parser reports malformed input as a zero result rather than by throwing.
        /// </summary>
        private bool TryEnter()
        {
            if (_depth >= MaxParseDepth)
            {
                _position = _text.Length;
                return false;
            }

            _depth++;
            return true;
        }

        private double ParsePrimary()
        {
            if (!TryEnter())
                return 0;

            try
            {
                return ParsePrimaryCore();
            }
            finally
            {
                _depth--;
            }
        }

        private double ParsePrimaryCore()
        {
            SkipWhitespace();
            if (TryConsume('('))
            {
                var value = ParseAddSubtract();
                TryConsume(')');
                return value;
            }

            if (Peek() == '[')
                return _fieldValue(ReadBracketedIdentifier());
            if (char.IsLetter(Peek()) || Peek() == '_')
                return _fieldValue(ReadIdentifier());
            return ReadNumber();
        }

        private string ReadBracketedIdentifier()
        {
            TryConsume('[');
            var start = _position;
            while (_position < _text.Length && _text[_position] != ']')
                _position++;
            var value = _text[start.._position].Trim();
            TryConsume(']');
            return value;
        }

        private string ReadIdentifier()
        {
            var start = _position;
            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_' || _text[_position] == ' '))
                _position++;
            return _text[start.._position].Trim();
        }

        private double ReadNumber()
        {
            var start = _position;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
                _position++;
            return double.TryParse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        private char Peek() => _position < _text.Length ? _text[_position] : '\0';

        private bool TryConsume(char ch)
        {
            SkipWhitespace();
            if (Peek() != ch)
                return false;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                _position++;
        }
    }
}

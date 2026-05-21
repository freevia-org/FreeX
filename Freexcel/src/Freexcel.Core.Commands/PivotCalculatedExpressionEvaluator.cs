using System.Globalization;

namespace Freexcel.Core.Commands;

internal static class PivotCalculatedExpressionEvaluator
{
    public static double Evaluate(string formula, Func<string, double> fieldValue)
    {
        var parser = new Parser(formula, fieldValue);
        return parser.Parse();
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly Func<string, double> _fieldValue;
        private int _position;

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
            SkipWhitespace();
            if (TryConsume('+'))
                return ParseUnary();
            if (TryConsume('-'))
                return -ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
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

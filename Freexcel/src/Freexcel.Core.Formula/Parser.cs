namespace Freexcel.Core.Formula;

/// <summary>
/// Recursive descent parser that converts a token stream into an AST.
/// Handles operator precedence: comparison &lt; concatenation &lt; addition &lt; multiplication &lt; power &lt; unary &lt; postfix.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    /// <summary>Parse the token stream into an AST.</summary>
    public FormulaNode Parse()
    {
        var node = ParseExpression();

        if (Current.Type != TokenType.EndOfFormula)
            throw new FormulaParseException($"Unexpected token '{Current.Value}' at position {Current.Position}");

        return node;
    }

    private Token Current => _tokens[_pos];

    private Token Peek(int offset = 1)
    {
        var index = _pos + offset;
        return index < _tokens.Count ? _tokens[index] : _tokens[^1];
    }

    private Token Advance()
    {
        var token = _tokens[_pos];
        _pos++;
        return token;
    }

    private Token Expect(TokenType type)
    {
        if (Current.Type != type)
            throw new FormulaParseException(
                $"Expected {type} but got {Current.Type} ('{Current.Value}') at position {Current.Position}");
        return Advance();
    }

    // Expression → Comparison
    private FormulaNode ParseExpression() => ParseComparison();

    // Comparison → Concatenation (( '=' | '<>' | '<' | '>' | '<=' | '>=' ) Concatenation)*
    private FormulaNode ParseComparison()
    {
        var left = ParseConcatenation();

        while (Current.Type is TokenType.Equal or TokenType.NotEqual or
               TokenType.LessThan or TokenType.GreaterThan or
               TokenType.LessOrEqual or TokenType.GreaterOrEqual)
        {
            var op = Current.Type switch
            {
                TokenType.Equal => BinaryOperator.Equal,
                TokenType.NotEqual => BinaryOperator.NotEqual,
                TokenType.LessThan => BinaryOperator.LessThan,
                TokenType.GreaterThan => BinaryOperator.GreaterThan,
                TokenType.LessOrEqual => BinaryOperator.LessOrEqual,
                TokenType.GreaterOrEqual => BinaryOperator.GreaterOrEqual,
                _ => throw new InvalidOperationException()
            };
            Advance();
            var right = ParseConcatenation();
            left = new BinaryOpNode(left, op, right);
        }

        return left;
    }

    // Concatenation → Addition ( '&' Addition )*
    private FormulaNode ParseConcatenation()
    {
        var left = ParseAddition();

        while (Current.Type == TokenType.Ampersand)
        {
            Advance();
            var right = ParseAddition();
            left = new BinaryOpNode(left, BinaryOperator.Concatenate, right);
        }

        return left;
    }

    // Addition → Multiplication ( ('+' | '-') Multiplication )*
    private FormulaNode ParseAddition()
    {
        var left = ParseMultiplication();

        while (Current.Type is TokenType.Plus or TokenType.Minus)
        {
            var op = Current.Type == TokenType.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
            Advance();
            var right = ParseMultiplication();
            left = new BinaryOpNode(left, op, right);
        }

        return left;
    }

    // Multiplication -> Power ( ('*' | '/') Power )*
    private FormulaNode ParseMultiplication()
    {
        var left = ParsePower();

        while (Current.Type is TokenType.Multiply or TokenType.Divide)
        {
            var op = Current.Type == TokenType.Multiply ? BinaryOperator.Multiply : BinaryOperator.Divide;
            Advance();
            var right = ParsePower();
            left = new BinaryOpNode(left, op, right);
        }

        return left;
    }

    // Power -> Unary ( '^' Power )? - right-associative: 2^3^2 = 2^(3^2) = 512
    // Excel gives unary signs higher precedence than exponentiation: -2^2 = (-2)^2.
    private FormulaNode ParsePower()
    {
        var left = ParseUnary();

        if (Current.Type == TokenType.Power)
        {
            Advance();
            var right = ParsePower();
            return new BinaryOpNode(left, BinaryOperator.Power, right);
        }

        return left;
    }

    // Unary -> ('-' | '+') Unary | Postfix
    private FormulaNode ParseUnary()
    {
        if (Current.Type == TokenType.Minus)
        {
            Advance();
            var operand = ParseUnary();
            return new UnaryOpNode(UnaryOperator.Negate, operand);
        }

        if (Current.Type == TokenType.Plus)
        {
            Advance();
            return ParseUnary();
        }

        return ParsePostfix();
    }

    // Postfix → Primary ( '%' )*
    private FormulaNode ParsePostfix()
    {
        var node = ParsePrimary();

        while (Current.Type == TokenType.Percent)
        {
            Advance();
            node = new UnaryOpNode(UnaryOperator.Percent, node);
        }

        return node;
    }

    // Primary → Number | String | Boolean | FunctionCall | CellRef (potentially with ':' range) | '(' Expression ')'
    private FormulaNode ParsePrimary()
    {
        switch (Current.Type)
        {
            case TokenType.Number:
            {
                if (Peek().Type == TokenType.Colon && TryParseFullRowRange(null, out var fullRowRange))
                    return fullRowRange;

                var token = Advance();
                return new NumberNode(double.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture));
            }

            case TokenType.String:
            {
                var token = Advance();
                return new StringNode(token.Value);
            }

            case TokenType.Boolean:
            {
                var token = Advance();
                return new BooleanNode(token.Value == "TRUE");
            }

            case TokenType.Error:
            {
                var token = Advance();
                return new ErrorNode(ParseErrorValue(token.Value));
            }

            case TokenType.FunctionName:
            {
                var name = Advance();
                Expect(TokenType.OpenParen);
                var args = ParseArgumentList();
                Expect(TokenType.CloseParen);
                return new FunctionCallNode(name.Value, args);
            }

            case TokenType.SheetQualifier:
            {
                var sheetToken = Advance();
                return ParseSheetQualifiedReference(sheetToken.Value);
            }

            case TokenType.CellRef:
            {
                var cellRef = ParseCellRef(Advance());
                if (cellRef is not CellRefNode rangeStartRef)
                    return cellRef;

                // Check for range operator ':'
                if (Current.Type == TokenType.Colon)
                {
                    Advance();
                    if (Current.Type != TokenType.CellRef)
                        throw new FormulaParseException(
                            $"Expected cell reference after ':' at position {Current.Position}");
                    var endRef = ParseCellRef(Advance());
                    if (endRef is not CellRefNode rangeEndRef)
                        return endRef;
                    return new RangeRefNode(rangeStartRef, rangeEndRef);
                }

                return cellRef;
            }

            case TokenType.NamedRange:
            {
                if (Peek().Type == TokenType.Colon && TryParseFullColumnRange(null, out var fullColumnRange))
                    return fullColumnRange;

                if (Peek().Type == TokenType.Colon && TryParseFullRowRange(null, out var fullRowRange))
                    return fullRowRange;

                var token = Advance();
                if (Current.Type == TokenType.StructuredReferenceSelector)
                {
                    var selector = Advance();
                    if (string.IsNullOrWhiteSpace(selector.Value))
                        throw new FormulaParseException(
                            $"Expected structured reference column name at position {selector.Position}");
                    if (selector.Value.Trim().StartsWith('@'))
                        return new StructuredCurrentRowReferenceNode(
                            selector.Value.Trim()[1..].Trim(),
                            token.Value);
                    return new StructuredReferenceNode(token.Value, selector.Value.Trim());
                }

                return new NamedRangeNode(token.Value);
            }

            case TokenType.StructuredReferenceSelector:
            {
                var selector = Advance();
                var value = selector.Value.Trim();
                if (value.StartsWith('@') && value.Length > 1)
                    return new StructuredCurrentRowReferenceNode(value[1..].Trim());
                if (value.Contains("#This Row", StringComparison.OrdinalIgnoreCase))
                    return new StructuredReferenceNode("", value);

                throw new FormulaParseException(
                    $"Expected current-row structured reference at position {selector.Position}");
            }

            case TokenType.OpenParen:
            {
                Advance();
                var expr = ParseExpression();
                Expect(TokenType.CloseParen);
                return expr;
            }

            default:
                throw new FormulaParseException(
                    $"Unexpected token '{Current.Value}' at position {Current.Position}");
        }
    }

    private FormulaNode ParseSheetQualifiedReference(string sheetName)
    {
        if (TryParseFullColumnRange(sheetName, out var fullColumnRange))
            return fullColumnRange;

        if (TryParseFullRowRange(sheetName, out var fullRowRange))
            return fullRowRange;

        if (Current.Type != TokenType.CellRef)
            throw new FormulaParseException(
                $"Expected cell reference after '{sheetName}!' at position {Current.Position}");

        var startRef = ParseCellRefWithSheet(Advance(), sheetName);
        if (startRef is not CellRefNode rangeStartRef)
            return startRef;

        if (Current.Type == TokenType.Colon)
        {
            Advance();
            if (Current.Type == TokenType.SheetQualifier)
                ExpectMatchingSheetQualifier(sheetName);

            if (Current.Type != TokenType.CellRef)
                throw new FormulaParseException(
                    $"Expected cell reference after ':' at position {Current.Position}");
            var endRef = ParseCellRef(Advance());
            if (endRef is not CellRefNode rangeEndRef)
                return endRef;
            return new RangeRefNode(rangeStartRef, rangeEndRef, sheetName);
        }

        return startRef;
    }

    private bool TryParseFullColumnRange(string? sheetName, out FormulaNode range)
    {
        range = null!;
        if (!TryParseColumnToken(Current, out var startColumn, out var isStartAbsolute))
            return false;

        if (Peek().Type != TokenType.Colon)
            return false;

        var saved = _pos;
        Advance();
        Advance();

        if (Current.Type == TokenType.SheetQualifier)
            ExpectMatchingSheetQualifier(sheetName);

        if (!TryParseColumnToken(Current, out var endColumn, out var isEndAbsolute))
        {
            _pos = saved;
            return false;
        }

        Advance();
        range = new FullColumnRangeRefNode(startColumn, endColumn, isStartAbsolute, isEndAbsolute, sheetName);
        return true;
    }

    private bool TryParseFullRowRange(string? sheetName, out FormulaNode range)
    {
        range = null!;
        if (!TryParseRowToken(Current, out var startRow, out var isStartAbsolute))
            return false;

        if (Peek().Type != TokenType.Colon)
            return false;

        var saved = _pos;
        Advance();
        Advance();

        if (Current.Type == TokenType.SheetQualifier)
            ExpectMatchingSheetQualifier(sheetName);

        if (!TryParseRowToken(Current, out var endRow, out var isEndAbsolute))
        {
            _pos = saved;
            return false;
        }

        Advance();
        range = new FullRowRangeRefNode(startRow, endRow, isStartAbsolute, isEndAbsolute, sheetName);
        return true;
    }

    private void ExpectMatchingSheetQualifier(string? sheetName)
    {
        var endSheetToken = Advance();
        if (sheetName is null)
            throw new FormulaParseException(
                $"Unexpected sheet qualifier '{endSheetToken.Value}!' at position {endSheetToken.Position}");

        if (!string.Equals(endSheetToken.Value, sheetName, StringComparison.OrdinalIgnoreCase))
            throw new FormulaParseException(
                $"Range start and end must be on the same sheet; got '{sheetName}' and '{endSheetToken.Value}'");
    }

    private static FormulaNode ParseCellRef(Token token)
    {
        var value = token.Value;   // e.g. "$B$3", "$B3", "B$3", "B3"
        var i = 0;

        bool isColAbs = false;
        if (i < value.Length && value[i] == '$') { isColAbs = true; i++; }

        int colStart = i;
        while (i < value.Length && char.IsLetter(value[i])) i++;
        var colName = value[colStart..i];

        // No column letters parsed — not a valid cell reference
        if (colStart == i) return new ErrorNode(Model.ErrorValue.Ref);

        bool isRowAbs = false;
        if (i < value.Length && value[i] == '$') { isRowAbs = true; i++; }

        if (!uint.TryParse(value[i..], out var row) || row == 0 || row > Model.CellAddress.MaxRow)
            return new ErrorNode(Model.ErrorValue.Ref);

        var colNum = Model.CellAddress.ColumnNameToNumber(colName);
        if (colNum == 0 || colNum > Model.CellAddress.MaxCol)
            return new ErrorNode(Model.ErrorValue.Ref);

        return new CellRefNode(colName, row, isColAbs, isRowAbs);
    }

    private static bool TryParseColumnToken(Token token, out string columnName, out bool isAbsolute)
    {
        columnName = "";
        isAbsolute = false;
        if (token.Type != TokenType.NamedRange)
            return false;

        var value = token.Value;
        if (value.StartsWith('$'))
        {
            isAbsolute = true;
            value = value[1..];
        }

        if (value.Length == 0 || value.Length > 3 || !value.All(char.IsLetter))
            return false;

        var colNum = Model.CellAddress.ColumnNameToNumber(value);
        if (colNum == 0 || colNum > Model.CellAddress.MaxCol)
            return false;

        columnName = value.ToUpperInvariant();
        return true;
    }

    private static bool TryParseRowToken(Token token, out uint row, out bool isAbsolute)
    {
        row = 0;
        isAbsolute = false;
        var value = token.Value;

        if (token.Type == TokenType.NamedRange && value.StartsWith('$'))
        {
            isAbsolute = true;
            value = value[1..];
        }
        else if (token.Type != TokenType.Number)
        {
            return false;
        }

        return uint.TryParse(value, out row) &&
               row is > 0 and <= Model.CellAddress.MaxRow;
    }

    private static FormulaNode ParseCellRefWithSheet(Token token, string sheetName)
    {
        var node = ParseCellRef(token);
        return node is CellRefNode cellRef
            ? cellRef with { SheetName = sheetName }
            : node;
    }

    private static Model.ErrorValue ParseErrorValue(string code) => code.ToUpperInvariant() switch
    {
        "#DIV/0!" => Model.ErrorValue.DivByZero,
        "#VALUE!" => Model.ErrorValue.Value,
        "#REF!" => Model.ErrorValue.Ref,
        "#NAME?" => Model.ErrorValue.Name,
        "#NULL!" => Model.ErrorValue.Null,
        "#N/A" => Model.ErrorValue.NA,
        "#NUM!" => Model.ErrorValue.Num,
        "#SPILL!" => Model.ErrorValue.Spill,
        "#CALC!" => Model.ErrorValue.Calc,
        _ => new Model.ErrorValue(code)
    };

    private List<FormulaNode> ParseArgumentList()
    {
        var args = new List<FormulaNode>();

        if (Current.Type == TokenType.CloseParen)
            return args;

        args.Add(Current.Type == TokenType.Comma
            ? new OmittedArgumentNode()
            : ParseExpression());

        while (Current.Type == TokenType.Comma)
        {
            Advance();
            args.Add(Current.Type is TokenType.Comma or TokenType.CloseParen
                ? new OmittedArgumentNode()
                : ParseExpression());
        }

        return args;
    }
}

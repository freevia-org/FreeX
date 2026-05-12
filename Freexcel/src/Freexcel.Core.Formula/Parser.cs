namespace Freexcel.Core.Formula;

/// <summary>
/// Recursive descent parser that converts a token stream into an AST.
/// Handles operator precedence: comparison &lt; concatenation &lt; addition &lt; multiplication &lt; power &lt; unary.
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

    // Multiplication → Power ( ('*' | '/') Power )*
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

    // Power → Unary ( '^' Unary )*
    private FormulaNode ParsePower()
    {
        var left = ParseUnary();

        while (Current.Type == TokenType.Power)
        {
            Advance();
            var right = ParseUnary();
            left = new BinaryOpNode(left, BinaryOperator.Power, right);
        }

        return left;
    }

    // Unary → ('-' | '+') Unary | Postfix
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
            return ParseUnary(); // unary plus is a no-op
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
                var sheetName = sheetToken.Value;

                if (Current.Type != TokenType.CellRef)
                    throw new FormulaParseException(
                        $"Expected cell reference after '{sheetName}!' at position {Current.Position}");

                var startRef = ParseCellRefWithSheet(Advance(), sheetName);

                if (Current.Type == TokenType.Colon)
                {
                    Advance();
                    if (Current.Type == TokenType.SheetQualifier)
                    {
                        var endSheetToken = Advance();
                        if (!string.Equals(endSheetToken.Value, sheetName, StringComparison.OrdinalIgnoreCase))
                            throw new FormulaParseException(
                                $"Range start and end must be on the same sheet; got '{sheetName}' and '{endSheetToken.Value}'");
                    }
                    if (Current.Type != TokenType.CellRef)
                        throw new FormulaParseException(
                            $"Expected cell reference after ':' at position {Current.Position}");
                    var endRef = ParseCellRef(Advance());
                    return new RangeRefNode(startRef, endRef, sheetName);
                }

                return startRef;
            }

            case TokenType.CellRef:
            {
                var cellRef = ParseCellRef(Advance());

                // Check for range operator ':'
                if (Current.Type == TokenType.Colon)
                {
                    Advance();
                    if (Current.Type != TokenType.CellRef)
                        throw new FormulaParseException(
                            $"Expected cell reference after ':' at position {Current.Position}");
                    var endRef = ParseCellRef(Advance());
                    return new RangeRefNode(cellRef, endRef);
                }

                return cellRef;
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

    private static CellRefNode ParseCellRef(Token token)
    {
        var value = token.Value;
        var i = 0;
        while (i < value.Length && char.IsLetter(value[i]))
            i++;

        var colName = value[..i];
        var row = uint.Parse(value[i..]);
        return new CellRefNode(colName, row);
    }

    private static CellRefNode ParseCellRefWithSheet(Token token, string sheetName)
    {
        var node = ParseCellRef(token);
        return node with { SheetName = sheetName };
    }

    private List<FormulaNode> ParseArgumentList()
    {
        var args = new List<FormulaNode>();

        if (Current.Type == TokenType.CloseParen)
            return args;

        args.Add(ParseExpression());

        while (Current.Type == TokenType.Comma)
        {
            Advance();
            args.Add(ParseExpression());
        }

        return args;
    }
}

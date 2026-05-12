namespace Freexcel.Core.Formula;

/// <summary>Base class for all AST nodes in a parsed formula.</summary>
public abstract record FormulaNode;

/// <summary>A numeric literal (e.g. 42, 3.14).</summary>
public sealed record NumberNode(double Value) : FormulaNode;

/// <summary>A string literal (e.g. "hello").</summary>
public sealed record StringNode(string Value) : FormulaNode;

/// <summary>A boolean literal (TRUE or FALSE).</summary>
public sealed record BooleanNode(bool Value) : FormulaNode;

/// <summary>A cell reference (e.g. A1, B5, Sheet2!A1).</summary>
public sealed record CellRefNode(string ColumnName, uint Row, string? SheetName = null) : FormulaNode
{
    /// <summary>Get the column as a 1-based number.</summary>
    public uint ColumnNumber => Model.CellAddress.ColumnNameToNumber(ColumnName);
}

/// <summary>A range reference (e.g. A1:C3, Sheet2!A1:A10).</summary>
public sealed record RangeRefNode(CellRefNode Start, CellRefNode End, string? SheetName = null) : FormulaNode;

/// <summary>A binary operation (e.g. A1 + B1).</summary>
public sealed record BinaryOpNode(FormulaNode Left, BinaryOperator Operator, FormulaNode Right) : FormulaNode;

/// <summary>A unary operation (e.g. -A1).</summary>
public sealed record UnaryOpNode(UnaryOperator Operator, FormulaNode Operand) : FormulaNode;

/// <summary>A function call (e.g. SUM(A1:A3)).</summary>
public sealed record FunctionCallNode(string FunctionName, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

/// <summary>Binary operators.</summary>
public enum BinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Power,
    Concatenate,
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessOrEqual,
    GreaterOrEqual
}

/// <summary>Unary operators.</summary>
public enum UnaryOperator
{
    Negate,
    Percent
}

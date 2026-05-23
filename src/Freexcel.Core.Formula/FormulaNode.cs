namespace Freexcel.Core.Formula;

/// <summary>Base class for all AST nodes in a parsed formula.</summary>
public abstract record FormulaNode;

/// <summary>A numeric literal (e.g. 42, 3.14).</summary>
public sealed record NumberNode(double Value) : FormulaNode;

/// <summary>A string literal (e.g. "hello").</summary>
public sealed record StringNode(string Value) : FormulaNode;

/// <summary>A boolean literal (TRUE or FALSE).</summary>
public sealed record BooleanNode(bool Value) : FormulaNode;

/// <summary>An omitted function argument, such as the empty slot in EXPAND(A1:B1,,3).</summary>
public sealed record OmittedArgumentNode : FormulaNode;

/// <summary>A cell reference (e.g. A1, $B$3, Sheet2!A1).</summary>
public sealed record CellRefNode(
    string  ColumnName,
    uint    Row,
    bool    IsColAbsolute = false,
    bool    IsRowAbsolute = false,
    string? SheetName = null
) : FormulaNode
{
    /// <summary>Get the column as a 1-based number.</summary>
    public uint ColumnNumber => Model.CellAddress.ColumnNameToNumber(ColumnName);
}

/// <summary>A range reference (e.g. A1:C3, Sheet2!A1:A10).</summary>
public sealed record RangeRefNode(CellRefNode Start, CellRefNode End, string? SheetName = null) : FormulaNode;

/// <summary>A whole-column range reference (e.g. A:A, Sheet2!A:B).</summary>
public sealed record FullColumnRangeRefNode(
    string StartColumnName,
    string EndColumnName,
    bool IsStartAbsolute = false,
    bool IsEndAbsolute = false,
    string? SheetName = null
) : FormulaNode
{
    public uint StartColumnNumber => Model.CellAddress.ColumnNameToNumber(StartColumnName);
    public uint EndColumnNumber => Model.CellAddress.ColumnNameToNumber(EndColumnName);
}

/// <summary>A whole-row range reference (e.g. 1:1, Sheet2!1:2).</summary>
public sealed record FullRowRangeRefNode(
    uint StartRow,
    uint EndRow,
    bool IsStartAbsolute = false,
    bool IsEndAbsolute = false,
    string? SheetName = null
) : FormulaNode;

/// <summary>A binary operation (e.g. A1 + B1).</summary>
public sealed record BinaryOpNode(FormulaNode Left, BinaryOperator Operator, FormulaNode Right) : FormulaNode;

/// <summary>A unary operation (e.g. -A1).</summary>
public sealed record UnaryOpNode(UnaryOperator Operator, FormulaNode Operand) : FormulaNode;

/// <summary>A function call (e.g. SUM(A1:A3)).</summary>
public sealed record FunctionCallNode(string FunctionName, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

/// <summary>
/// A named range reference (e.g. MyData). Resolved to a GridRange at evaluation time.
/// </summary>
public sealed record NamedRangeNode(string Name) : FormulaNode;

/// <summary>A table structured reference to one data-body column (e.g. Sales[Amount]).</summary>
public sealed record StructuredReferenceNode(string TableName, string ColumnName) : FormulaNode;

/// <summary>A structured reference to the current table row (e.g. [@Amount]).</summary>
public sealed record StructuredCurrentRowReferenceNode(string ColumnName, string? TableName = null) : FormulaNode;

/// <summary>A formula-level error literal produced by reference rewriting (e.g. #REF!).</summary>
public sealed record ErrorNode(Model.ErrorValue Error) : FormulaNode;

/// <summary>Binary operators.</summary>
public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide, Power, Concatenate,
    Equal, NotEqual, LessThan, GreaterThan, LessOrEqual, GreaterOrEqual
}

/// <summary>Unary operators.</summary>
public enum UnaryOperator { Negate, Percent }

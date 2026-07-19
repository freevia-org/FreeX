namespace FreeX.Core.Formula;

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

/// <summary>An inline Excel array constant, such as {1,2;3,4}.</summary>
public sealed record ArrayConstantNode(IReadOnlyList<IReadOnlyList<FormulaNode>> Rows) : FormulaNode;

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
    public uint ColumnNumber { get; } = Model.CellAddress.ColumnNameToNumber(ColumnName);
}

/// <summary>
/// A range reference (e.g. A1:C3, Sheet2!A1:A10). When <paramref name="EndSheetName"/> is
/// non-null, this represents a 3-D sheet-span reference (e.g. Sheet1:Sheet3!A1 or
/// Sheet1:Sheet3!A1:B5) — <see cref="SheetName"/> is the span's start sheet and
/// <paramref name="EndSheetName"/> is its end sheet; the reference covers every sheet from
/// start to end inclusive, in workbook tab order (a reversed span like Sheet3:Sheet1!A1 is
/// normalized the same way Excel does). A bare single-cell 3-D reference (no ':A1:B5' range
/// part, e.g. Sheet1:Sheet3!A1) is represented with Start == End and
/// <paramref name="IsSingleCellSpan"/> = true, so FormulaSerializer can reprint it without a
/// synthesized ":A1" that was never in the original text — every other consumer (evaluator,
/// dependency collector, rewriter) only cares about Start/End/SheetName/EndSheetName and can
/// ignore this flag.
/// </summary>
public sealed record RangeRefNode(
    CellRefNode Start,
    CellRefNode End,
    string? SheetName = null,
    string? EndSheetName = null,
    bool IsSingleCellSpan = false) : FormulaNode;

/// <summary>A whole-column range reference (e.g. A:A, Sheet2!A:B).</summary>
public sealed record FullColumnRangeRefNode(
    string StartColumnName,
    string EndColumnName,
    bool IsStartAbsolute = false,
    bool IsEndAbsolute = false,
    string? SheetName = null
) : FormulaNode
{
    public uint StartColumnNumber { get; } = Model.CellAddress.ColumnNameToNumber(StartColumnName);
    public uint EndColumnNumber { get; } = Model.CellAddress.ColumnNameToNumber(EndColumnName);
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
/// <param name="SheetQualifier">
/// The sheet name the reference was explicitly qualified with (e.g. the "Sheet2" in
/// "Sheet2!MyName"), or <c>null</c> when the name was written unqualified. Trailing/optional
/// so every existing positional construction (<c>new NamedRangeNode(name)</c>) keeps compiling
/// unchanged. NOTE: as of this change the evaluator (FormulaEvaluator.References.cs -
/// EvaluateNamedRange / ResolveNamedRangeNodeAsReference / IsSheetScopedName) does not yet
/// consult this field — it still resolves purely against the formula's own current-sheet scope.
/// Threading it into name-scope resolution is a residual follow-up in that file.
/// </param>
public sealed record NamedRangeNode(string Name, string? SheetQualifier = null) : FormulaNode;

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
public enum UnaryOperator { Negate, Percent, ImplicitIntersection }

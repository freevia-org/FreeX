namespace FreeX.Core.Model;

public enum FormulaTraceArrowKind
{
    Precedent,
    Dependent
}

public sealed record FormulaTraceArrow(
    CellAddress From,
    CellAddress To,
    FormulaTraceArrowKind Kind = FormulaTraceArrowKind.Precedent);

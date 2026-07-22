using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R74-formula-reference-fns-4-1: BuiltInFunctions.Lookup.Indirect.cs's
/// TryResolveIndirectRangeReference gated all three name-lookup branches on
/// <c>sheetName is null</c>, so a sheet-qualified name reference like
/// <c>INDIRECT("Sheet2!Rate")</c> was never resolved as a name at all -- it fell straight through
/// to the raw cell-address parse, which also fails for plain name text (no digits/colon), yielding
/// #REF! even though Excel resolves a sheet-scoped name used off its own sheet exactly via that
/// "SheetName!Name" syntax (mirroring how a direct <c>=Sheet2!Rate</c> formula reference resolves
/// via FormulaEvaluator.TryResolveSheetQualifiedName). The fix adds a name-lookup branch for the
/// sheet-qualified case, using Workbook.TryGetNamedRange(name, sheetId)'s own
/// scoped-then-global precedence.
/// </summary>
public sealed class R74_IndirectSheetQualifiedNameTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static (Workbook workbook, Sheet sheet1, Sheet sheet2) MakeWorkbookWithScopedRateName()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(5));
        workbook.DefineNamedRange(
            "Rate",
            new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1)),
            metadata: null,
            scopeSheetId: sheet2.Id);

        return (workbook, sheet1, sheet2);
    }

    [Fact]
    public void Indirect_SheetQualifiedName_ResolvesAgainstThatSheetsScope()
    {
        var (workbook, sheet1, _) = MakeWorkbookWithScopedRateName();

        // Evaluated from Sheet1 -- Rate is scoped to Sheet2, so only the "Sheet2!Rate" qualified
        // form can see it. INDIRECT of a name always resolves to a (possibly 1x1) RangeValue --
        // see Indirect_OfPlainNamedRange_StillResolves in R20_DefinedNameEvalTests.cs for the same
        // convention -- so wrap in SUM to unwrap the scalar for comparison.
        var result = _evaluator.Evaluate("=SUM(INDIRECT(\"Sheet2!Rate\"))", sheet1, workbook);

        result.Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Indirect_SheetQualifiedCellReference_StillWorks_SiblingNoRegression()
    {
        var (workbook, sheet1, sheet2) = MakeWorkbookWithScopedRateName();
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(42));

        // A genuine cross-sheet cell reference (not a name) must be unaffected by the new
        // name-lookup branch.
        var result = _evaluator.Evaluate("=INDIRECT(\"Sheet2!A2\")", sheet1, workbook);

        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Indirect_UnqualifiedName_StillWorks_SiblingNoRegression()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(7));
        workbook.DefineNamedRange(
            "Rate",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1)));

        var result = _evaluator.Evaluate("=SUM(INDIRECT(\"Rate\"))", sheet1, workbook);

        result.Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Indirect_SheetQualifiedNonexistentName_ReturnsRefError()
    {
        var (workbook, sheet1, _) = MakeWorkbookWithScopedRateName();

        var result = _evaluator.Evaluate("=INDIRECT(\"Sheet2!Nonexistent\")", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }
}

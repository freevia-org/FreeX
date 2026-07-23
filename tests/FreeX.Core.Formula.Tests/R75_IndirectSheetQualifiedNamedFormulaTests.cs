using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R75-meta-2: the R74 INDIRECT sheet-qualified fix (see R74_IndirectSheetQualifiedNameTests)
/// only resolves a named RANGE via Workbook.TryGetNamedRange(name, sheetId) -- it never consults
/// ScopedNamedFormulas, so INDIRECT("Sheet2!GrownName") where GrownName is a sheet-scoped dynamic
/// named FORMULA (e.g. built with OFFSET/COUNTA) fell through to #REF!, even though the
/// equivalent unqualified INDIRECT("GrownName") evaluated from Sheet2 (R23_IndirectSheetScopedNamedFormulaTests)
/// and the direct =Sheet2!GrownName formula reference (FormulaEvaluator.TryResolveSheetQualifiedName)
/// both already resolve it. Fixed by adding a scoped-formula-first branch (mirroring
/// TryResolveSheetQualifiedName's own precedence) that resolves against the QUALIFIED sheet's
/// scope via the new FormulaEvaluator.TryResolveIndirectNamedFormulaScoped helper before falling
/// back to the named-range lookup.
/// </summary>
public sealed class R75_IndirectSheetQualifiedNamedFormulaTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void Indirect_SheetQualifiedNamedFormula_Resolves()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(2));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new NumberValue(3));
        workbook.DefineNamedFormula(
            "Grown",
            "OFFSET(Sheet2!$A$1,0,0,COUNTA(Sheet2!$A:$A),1)",
            sheet2.Id);

        // Evaluated from Sheet1 -- Grown is scoped to Sheet2, so only "Sheet2!Grown" can see it.
        var result = _evaluator.Evaluate("=SUM(INDIRECT(\"Sheet2!Grown\"))", sheet1, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Indirect_SheetQualifiedPlainRangeName_StillWorks_SiblingNoRegression()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(5));
        workbook.DefineNamedRange(
            "PlainRangeName",
            new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1)),
            metadata: null,
            scopeSheetId: sheet2.Id);

        var result = _evaluator.Evaluate("=SUM(INDIRECT(\"Sheet2!PlainRangeName\"))", sheet1, workbook);

        result.Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Indirect_SheetQualifiedMissingName_ReturnsRefError_SiblingNoRegression()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        var result = _evaluator.Evaluate("=INDIRECT(\"Sheet2!Missing\")", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Indirect_UnqualifiedNamedFormula_StillWorks_SiblingNoRegression()
    {
        var workbook = new Workbook("Test");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(2));
        workbook.DefineNamedFormula(
            "Grown",
            "OFFSET(Sheet2!$A$1,0,0,COUNTA(Sheet2!$A:$A),1)",
            sheet2.Id);

        // Evaluated directly on Sheet2 -- the unqualified path (already fixed in R23) must be unaffected.
        var result = _evaluator.Evaluate("=SUM(INDIRECT(\"Grown\"))", sheet2, workbook);

        result.Should().Be(new NumberValue(3));
    }
}

using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 156 remediation (G2): FormulaAuditingService.References.cs's private CollectReferences /
/// CollectReferenceRegions carried the same pre-R156 INDIRECT gap that RecalcEngine.CollectReferences
/// had (see R156_IndirectMixedCircularReferenceTests) -- a literal-string INDIRECT("A1") target
/// dropped out silently because the FunctionCallNode case only recursed into arguments, and a
/// StringNode argument has no nested FormulaNode reference to find. That backs GetDirectPrecedents,
/// GetDirectPrecedentRegions, GetPrecedentTraceArrows, and GetDirectDependents -- i.e. the ribbon
/// Trace Precedents/Trace Dependents buttons and the Error Checking "Trace Error" action -- so the
/// exact case a user reaches for right after seeing the new #CIRCULAR! (A1=B1+1, B1=INDIRECT("A1"))
/// showed an empty precedent list for B1 instead of [A1]. Fixed by sharing
/// BuiltInFunctions.TryResolveIndirectStaticCellTarget, the same resolver RecalcEngine now uses,
/// instead of restating the rule a third time.
/// </summary>
public sealed class R156_FormulaAuditingIndirectPrecedentTests
{
    /// <summary>
    /// Bug case (the auditor's exact repro): GetDirectPrecedents(B1) for B1=INDIRECT("A1") must
    /// return [A1], not an empty list.
    /// </summary>
    [Fact]
    public void GetDirectPrecedents_ResolvesLiteralStringIndirectTarget()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, Cell.FromFormula("B1+1"));
        sheet.SetCell(b1, Cell.FromFormula("INDIRECT(\"A1\")"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(wb, b1);

        precedents.Should().Equal(a1);
    }

    /// <summary>
    /// Sheet-qualified literal INDIRECT target (INDIRECT("Sheet2!A1")) must resolve to the other
    /// sheet, mirroring TryResolveIndirectStaticCellTarget's sheet-qualifier handling.
    /// </summary>
    [Fact]
    public void GetDirectPrecedents_ResolvesSheetQualifiedLiteralIndirectTarget()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var target = new CellAddress(sheet2.Id, 1, 1);
        var formulaAddress = new CellAddress(sheet1.Id, 1, 2);
        sheet1.SetCell(formulaAddress, Cell.FromFormula("INDIRECT(\"Sheet2!A1\")"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress);

        precedents.Should().Equal(target);
    }

    /// <summary>
    /// Sibling of the precedents fix: GetDirectDependents walks the same CollectReferences path in
    /// reverse (it computes every OTHER cell's precedents and checks membership -- see
    /// FormulaAuditingService.cs), so A1 must now report B1 as a dependent once B1's literal
    /// INDIRECT("A1") registers as a real precedent edge.
    /// </summary>
    [Fact]
    public void GetDirectDependents_ResolvesLiteralStringIndirectTarget()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("INDIRECT(\"A1\")"));

        var dependents = FormulaAuditingService.GetDirectDependents(wb, a1);

        dependents.Should().Equal(b1);
    }

    /// <summary>
    /// Sibling of the precedents fix, for the region-based walk that backs the Trace Precedents
    /// arrow overlay (CollectReferenceRegions / GetPrecedentTraceArrows), not just the flattened
    /// per-cell GetDirectPrecedents list.
    /// </summary>
    [Fact]
    public void GetPrecedentTraceArrows_ResolvesLiteralStringIndirectTarget()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("INDIRECT(\"A1\")"));

        var arrows = FormulaAuditingService.GetPrecedentTraceArrows(wb, b1);

        arrows.Should().Equal(new FormulaTraceArrow(a1, b1, FormulaTraceArrowKind.Precedent));
    }

    /// <summary>
    /// No-regression sibling: a non-literal INDIRECT argument (e.g. INDIRECT(B1), a cell reference
    /// rather than a constant string) never matches the new StringNode-guarded case -- it falls
    /// through to the unchanged generic FunctionCallNode case exactly as before the fix, which
    /// recurses into the argument itself and picks up B1 (the argument reference) as the
    /// precedent. It cannot resolve INDIRECT's dynamic TARGET (whatever cell B1's text names at
    /// runtime, e.g. "A1") -- TryResolveIndirectStaticCellTarget only resolves a literal constant
    /// string, matching RecalcEngine's identical scope decision -- so A1 must NOT appear here.
    /// </summary>
    [Fact]
    public void GetDirectPrecedents_NonLiteralIndirectArgument_StillYieldsOnlyTheArgumentReference()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, new TextValue("A1"));
        sheet.SetCell(c1, Cell.FromFormula("INDIRECT(B1)"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(wb, c1);

        precedents.Should().Equal(b1);
    }

    /// <summary>
    /// No-regression sibling: an ordinary plain cell-reference precedent chain (no INDIRECT
    /// involved at all) must keep working unaffected by the new INDIRECT case sitting alongside it
    /// in the same switch.
    /// </summary>
    [Fact]
    public void GetDirectPrecedents_PlainCellReference_StillResolvesUnaffected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(wb, b1);

        precedents.Should().Equal(a1);
    }
}

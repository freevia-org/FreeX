using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for round-172 meta finding F2: NodeReachesAnotherSheet's FunctionCallNode
/// case only special-cased INDIRECT/OFFSET as "might reach outward" and otherwise recursed into
/// only the call's own argument nodes -- so a Formula-type CF rule that CALLS a Name-Manager custom
/// function (a workbook/sheet-scoped defined name whose RefersTo is a LAMBDA, invoked with call
/// syntax -- see FormulaEvaluator.Functions.cs's EvaluateFunction lines 21-38) was invisible to it:
/// "=MYCALC(A1)" has no SheetName anywhere in its own argument list, even though MYCALC's LAMBDA
/// body genuinely reads another sheet at evaluation time. SheetHasConditionalFormatReachingAnother
/// Sheet said false, so BuildConditionalFormatContext never folded the cross-sheet checksum into
/// its cache key, and editing the other sheet's cell alone left the CF's cached color stale.
///
/// The fix treats any function-call name that is neither a real built-in (BuiltInFunctions.TryGet)
/// nor one of the evaluator's AST-aware special forms (LET/LAMBDA/SINGLE/ANCHORARRAY) as
/// conservatively "might reach outward" -- exactly like a bare NamedRangeNode is already treated a
/// few lines below -- without ever having to resolve what the Name Manager entry's body actually
/// contains.
/// </summary>
public sealed class R172_CfFormulaCrossSheetLambdaCacheTests
{
    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    [Fact]
    public void FormulaRule_CallingNameManagerLambdaReadingOtherSheet_RefreshesAfterTargetCellEdited()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var sheet2A1 = new CellAddress(sheet2.Id, 1, 1);
        sheet2.SetCell(sheet2A1, Cell.FromValue(new NumberValue(5)));

        // Name Manager custom function: MYCALC(n) = n + Sheet2!$A$1 (Excel's documented
        // "custom function via Name Manager" pattern -- see R28_LambdaNamedFormulaCallTests).
        wb.NamedFormulas["MYCALC"] = "LAMBDA(n,n+Sheet2!$A$1)";

        var sheet1A1 = new CellAddress(sheet1.Id, 1, 1);
        sheet1.SetCell(sheet1A1, Cell.FromValue(new NumberValue(1)));

        var highlight = new CellColor(255, 0, 0);
        sheet1.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(sheet1A1, sheet1A1),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            // 1 + 5 = 6 > 10 is false initially.
            FormulaText = "MYCALC(A1)>10",
            FormatIfTrue = new CellStyle { FillColor = highlight }
        });

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        var vpBefore = svc.GetViewport(wb, sheet1.Id, request);
        GetCell(vpBefore, 1, 1).Style?.FillColor.Should().NotBe(highlight, "1 + 5 = 6 is not > 10");

        // Edit ONLY Sheet2!A1 (what MYCALC's own LAMBDA body reads) -- Sheet1 itself, and the CF
        // rule's own formula text, are never touched.
        sheet2.SetCell(sheet2A1, Cell.FromValue(new NumberValue(50)));

        var vpAfter = svc.GetViewport(wb, sheet1.Id, request);
        GetCell(vpAfter, 1, 1).Style!.FillColor.Should().Be(highlight,
            "1 + 50 = 51 is now > 10 -- Excel would apply the highlight immediately, but a stale " +
            "cache keyed only on Sheet1's own (unchanged) ContentVersion, blind to the Name-Manager " +
            "LAMBDA's own cross-sheet read, would keep serving the pre-edit 'false' result");
    }

    [Fact]
    public void VolatileRuleWithNoFormulaCells_DoesNotRerollWhenUnrelatedSheetCallsBuiltInFunction()
    {
        // Sibling no-regression, mirroring R147_ShiftF9RerollsVolatileCfWithNoFormulaCellsTests'
        // contract: a volatile CF rule that reaches nothing outside its own cell (an ordinary
        // built-in call, not a Name-Manager custom function) must NOT be treated as cross-sheet
        // reaching just because this fix now also inspects function calls more closely. Editing an
        // unrelated sheet must not force a rebuild of this sheet's cached CF context.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), Cell.FromValue(new NumberValue(5)));

        sheet1.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet1.Id, 1, 1),
                new CellAddress(sheet1.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            // RAND() is volatile but reaches nothing outside its own cell; SUM is an ordinary
            // built-in, not a Name-Manager custom function, so it must NOT trip the new
            // "unrecognized callee" conservative branch.
            FormulaText = "SUM(RAND(),0)>=0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(0, 255, 0) }
        });

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        svc.GetViewport(wb, sheet1.Id, request);
        var buildCountBefore = svc.CfContextBuildCount;

        // Edit the OTHER sheet -- Sheet1's own ContentVersion/ConditionalFormats.Version are
        // untouched, and the rule's only function calls (SUM, RAND) are real built-ins, so its
        // cached context must survive.
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), Cell.FromValue(new NumberValue(999)));

        svc.GetViewport(wb, sheet1.Id, request);
        var buildCountAfter = svc.CfContextBuildCount;

        buildCountAfter.Should().Be(buildCountBefore,
            "SUM/RAND are ordinary built-ins that reach nothing outside their own cell, so editing " +
            "Sheet2 must not force a rebuild of Sheet1's cached CF context");
    }
}

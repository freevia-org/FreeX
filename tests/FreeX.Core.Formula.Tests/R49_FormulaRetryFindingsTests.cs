using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-49 "formula-retry" bucket fixes (a second pass over two findings that a prior agent had
/// to skip purely because the file scope granted to it didn't include everything the fix needed;
/// that scope has since been widened for this pass):
///
///  - R49-docs-parity-vs-reality-sweep-1: BuiltInFunctions.Names (used by the Insert Function
///    dialog) is computed by an eager static field initializer that runs before the static
///    constructor on the Engineering partial (BuiltInFunctions.Engineering.cs) registers
///    BESSELI/BESSELJ/BESSELK/BESSELY into the Functions dictionary, so those four real, callable
///    functions never appeared in Names. Fixed by making Names lazily computed on first access
///    (BuiltInFunctions.cs), which only ever runs after the type is fully initialized. Also added
///    the 4 missing "Implemented" rows to docs/parity/functions.md and bumped the Engineering
///    category / TOTAL counts (53/492 -> 57/496) plus the FormulaParityCatalogTests in-scope
///    count literal, since those documentation counts are enforced by
///    FormulaParityCatalogTests.FunctionParityDocument_TotalMatchesImplementedRows /
///    _CategorySummariesMatchFunctionRows and would otherwise fail once Names exposes BESSEL*.
///
///  - R49-io-defined-name-scope-3-1: SheetName!Name (a sheet-qualified reference to a defined
///    name) used to drop the sheet qualifier in Parser.ParseSheetQualifiedReference and return a
///    bare NamedRangeNode, so the name always resolved against the *current* formula's sheet scope
///    instead of the qualifier's sheet. NamedRangeNode now carries an optional SheetQualifier
///    (FormulaNode.cs) and Parser.cs threads the qualifying sheet name into it. A prior pass SKIPPED
///    the behavioral fix because the actual scope-resolution logic (EvaluateNamedRange /
///    ResolveNamedRangeNodeAsReference / IsSheetScopedName, all in
///    src/FreeX.Core.Formula/FormulaEvaluator.References.cs) still resolved purely against the
///    context's current sheet and never read the new SheetQualifier field, and that file was
///    outside that pass's edit scope. This pass adds TryResolveSheetQualifiedName to
///    FormulaEvaluator.References.cs and wires it into EvaluateNamedRange, the NamedRangeNode
///    branch of EvaluateArrayOperand, and ResolveNamedRangeNodeAsReference, so a sheet-qualified
///    name now resolves against THAT sheet's own scope (falling back to workbook-global scope)
///    instead of the calling formula's own current sheet. The behavioral tests below (in the
///    "io-defined-name-scope-3-1 (behavioral)" region) exercise that resolution directly.
/// </summary>
public sealed class R49_FormulaRetryFindingsTests
{
    // ── docs-parity-vs-reality-sweep-1: BESSEL* visible in BuiltInFunctions.Names ──

    [Fact]
    public void Names_ContainsBesselFunctions()
    {
        // Pre-fix: FunctionNames was an eager static field initializer computed from
        // Functions.Keys before the Engineering partial's static constructor added
        // BESSELI/J/K/Y, so none of the four appeared here even though =BESSELJ(1,2) evaluated
        // correctly and BuiltInFunctions.Exists("BESSELJ") was true.
        BuiltInFunctions.Names.Should().Contain("BESSELJ");
        BuiltInFunctions.Names.Should().Contain("BESSELI");
        BuiltInFunctions.Names.Should().Contain("BESSELK");
        BuiltInFunctions.Names.Should().Contain("BESSELY");
    }

    [Fact]
    public void Names_StillContainsOrdinaryFunctions_NoRegression()
    {
        // Sibling no-regression case: making Names lazy must not lose or duplicate any of the
        // ordinary, always-registered functions from the Functions dictionary literal.
        BuiltInFunctions.Names.Should().Contain("SUM");
        BuiltInFunctions.Names.Should().Contain("VLOOKUP");
        BuiltInFunctions.Names.Should().OnlyHaveUniqueItems();
    }

    // ── io-defined-name-scope-3-1: NamedRangeNode carries the sheet qualifier (AST plumbing only) ──

    [Fact]
    public void SheetQualifiedNamedRange_ParsesWithSheetQualifierOnNode()
    {
        // Pre-fix: NamedRangeNode had no sheet-qualifier slot at all, so
        // ParseSheetQualifiedReference could only ever construct new NamedRangeNode(name) and the
        // qualifier was discarded at parse time already, before evaluation even had a chance to
        // use it.
        var node = new Parser(new Lexer("=Sheet2!MyName").Tokenize()).Parse();

        node.Should().BeOfType<NamedRangeNode>();
        var named = (NamedRangeNode)node;
        named.Name.Should().Be("MYNAME");
        named.SheetQualifier.Should().Be("Sheet2");
    }

    [Fact]
    public void UnqualifiedNamedRange_HasNullSheetQualifier_NoRegression()
    {
        // Sibling no-regression case: an ordinary, unqualified named-range reference must keep
        // parsing exactly as before (null qualifier), and the record's new optional trailing
        // parameter must not disturb its existing positional-construction sites.
        var node = new Parser(new Lexer("=MyName").Tokenize()).Parse();

        node.Should().BeOfType<NamedRangeNode>();
        var named = (NamedRangeNode)node;
        named.Name.Should().Be("MYNAME");
        named.SheetQualifier.Should().BeNull();
    }

    // ── io-defined-name-scope-3-1 (behavioral): sheet-qualified name actually resolves
    //    against the qualified sheet's own scope, not the calling formula's current sheet ──

    [Fact]
    public void SheetQualifiedName_ResolvesQualifiedSheetsOwnScope_NotCurrentSheetsOwnScope()
    {
        // Sheet1 has its own sheet-scoped "Rate" = 100 ...
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet1A1 = new CellAddress(sheet1.Id, 1, 1);
        sheet1.SetCell(sheet1A1, new NumberValue(100));
        workbook.DefineNamedRange("Rate", new GridRange(sheet1A1, sheet1A1), metadata: null, scopeSheetId: sheet1.Id);

        // ... and Sheet2 has its OWN, unrelated sheet-scoped "Rate" = 5.
        var sheet2A1 = new CellAddress(sheet2.Id, 1, 1);
        sheet2.SetCell(sheet2A1, new NumberValue(5));
        workbook.DefineNamedRange("Rate", new GridRange(sheet2A1, sheet2A1), metadata: null, scopeSheetId: sheet2.Id);

        // A formula on Sheet1 referencing "Sheet2!Rate" -- the exact syntax real Excel always
        // writes for a name scope-limited to a sheet other than the one using it -- must resolve
        // to Sheet2's own local Rate (5), not Sheet1's own local Rate (100).
        var result = _evaluator.Evaluate("=Sheet2!Rate", sheet1, workbook);

        result.Should().BeOfType<RangeValue>()
            .Subject.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void SheetQualifiedName_WithNoLocalNameOnCurrentSheet_StillPrefersQualifiedSheetsScope()
    {
        // Only Sheet2 defines a sheet-scoped "Rate" (Sheet1 has no local name of that text at
        // all, and there is no workbook-global "Rate" either). Pre-fix, this used to fall through
        // to a bare current-sheet-then-workbook lookup that found nothing and returned #NAME?,
        // even though real Excel resolves it via Sheet2's own scope.
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet2A1 = new CellAddress(sheet2.Id, 1, 1);
        sheet2.SetCell(sheet2A1, new NumberValue(5));
        workbook.DefineNamedRange("Rate", new GridRange(sheet2A1, sheet2A1), metadata: null, scopeSheetId: sheet2.Id);

        var result = _evaluator.Evaluate("=Sheet2!Rate", sheet1, workbook);

        result.Should().BeOfType<RangeValue>()
            .Subject.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void SheetQualifiedName_FallsBackToWorkbookGlobalScope_WhenQualifiedSheetHasNoLocalName()
    {
        // A workbook-scoped name is unaffected by the sheet-qualifier fix: "Sheet2!Global" must
        // still resolve via ordinary workbook-global scope when Sheet2 has no local name of that
        // text of its own (mirrors the pre-existing R43 sheet-qualified-global-name behavior, now
        // reached through the same qualified-sheet-scope-then-workbook-fallback code path instead
        // of the qualifier simply being dropped).
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var globalCell = new CellAddress(sheet1.Id, 5, 5);
        sheet1.SetCell(globalCell, new NumberValue(42));
        workbook.DefineNamedRange("Global", new GridRange(globalCell, globalCell));

        var result = _evaluator.Evaluate("=Sheet2!Global", sheet1, workbook);

        result.Should().BeOfType<RangeValue>()
            .Subject.Cells[0, 0].Should().Be(new NumberValue(42));
    }

    [Fact]
    public void UnqualifiedName_StillUsesCurrentSheetThenWorkbookResolution_NoRegression()
    {
        // Sibling no-regression case for the evaluator (not just the parser): an ordinary
        // unqualified name must still resolve via the formula's own current-sheet scope first,
        // ignoring an unrelated same-named scoped definition on a different sheet.
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet1A1 = new CellAddress(sheet1.Id, 1, 1);
        sheet1.SetCell(sheet1A1, new NumberValue(100));
        workbook.DefineNamedRange("Rate", new GridRange(sheet1A1, sheet1A1), metadata: null, scopeSheetId: sheet1.Id);

        var sheet2A1 = new CellAddress(sheet2.Id, 1, 1);
        sheet2.SetCell(sheet2A1, new NumberValue(5));
        workbook.DefineNamedRange("Rate", new GridRange(sheet2A1, sheet2A1), metadata: null, scopeSheetId: sheet2.Id);

        var result = _evaluator.Evaluate("=Rate", sheet1, workbook);

        result.Should().BeOfType<RangeValue>()
            .Subject.Cells[0, 0].Should().Be(new NumberValue(100));
    }

    [Fact]
    public void SheetQualifiedName_NonexistentSheet_ReturnsRefError()
    {
        // The qualifying sheet name itself doesn't resolve to a real sheet -- matches Excel's
        // #REF! for a reference to a nonexistent sheet, rather than silently falling back to the
        // current sheet's own scope or surfacing #NAME?.
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=NoSuchSheet!Rate", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    private readonly FormulaEvaluator _evaluator = new();
}

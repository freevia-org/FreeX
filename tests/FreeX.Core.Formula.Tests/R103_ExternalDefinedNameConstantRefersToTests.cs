using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R103-formula-external-defined-name-constant: <see cref="ExternalSheetReferenceResolver.TryResolveExternalDefinedName"/>
/// (the resolver for the sheet-less <c>[n]!Name</c> external-workbook defined-name reference shape,
/// and -- via <c>TryResolveExternalSheetQualifiedDefinedName</c> in FormulaEvaluator.References.cs --
/// also for the sheet-qualified <c>[n]Sheet!Name</c> shape) required the matched
/// <see cref="ExternalDefinedNameModel.RefersTo"/> text to be sheet-qualified ("Sheet!Ref"). A
/// workbook-scoped external name whose cached RefersTo is a plain constant or non-reference formula
/// (e.g. <c>&lt;definedName name="TaxRate" refersTo="0.08"/&gt;</c> -- ECMA-376 18.14.4
/// CT_ExternalDefinedName places no shape requirement on refersTo, and workbook names commonly hold
/// literal constants) has no '!' at all, so the sheet-split always failed and the method returned
/// false unconditionally -- the caller then fell through to an ordinary name lookup keyed on the
/// opaque literal "[1]!TaxRate", which can never match, yielding #NAME? even though FreeX already
/// parsed and cached the exact RefersTo text and Excel itself would show 0.08.
///
/// Fixed by falling back to handing the raw RefersTo text straight through as the named-formula
/// text when it isn't sheet-qualified, exactly as an ordinary local named formula's bare RefersTo
/// is already evaluated (GetOrParseFormula + EvaluateNamedFormulaText).
/// </summary>
public sealed class R103_ExternalDefinedNameConstantRefersToTests
{
    private static Workbook BuildWorkbookWithConstantExternalDefinedName(out Sheet sheet)
    {
        var workbook = new Workbook("Test");
        sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(50)); // local B2 = 50

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Book1.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        link.DefinedNames.Add(new ExternalDefinedNameModel
        {
            Name = "TaxRate",
            RefersTo = "0.08", // workbook-scoped CONSTANT (no sheet segment at all)
        });
        workbook.ExternalLinks.Add(link);

        return workbook;
    }

    [Fact]
    public void SheetLessExternalDefinedName_ConstantRefersTo_ResolvesCachedConstant_InsteadOfName()
    {
        // The end-to-end mirror of the finding's scenario: =[1]!TaxRate+B2 where TaxRate's cached
        // RefersTo ("0.08") has no sheet segment at all. Before the fix this evaluated to
        // ErrorValue.Name; Excel itself would show 0.08+50 = 50.08.
        var workbook = BuildWorkbookWithConstantExternalDefinedName(out var sheet);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]!TaxRate+B2", sheet, workbook);

        result.Should().Be(new NumberValue(50.08));
    }

    [Fact]
    public void SheetLessExternalDefinedName_ConstantRefersTo_Bare_ResolvesDirectly()
    {
        var workbook = BuildWorkbookWithConstantExternalDefinedName(out var sheet);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]!TaxRate", sheet, workbook);

        result.Should().Be(new NumberValue(0.08));
    }

    [Fact]
    public void SheetQualifiedExternalDefinedName_ConstantRefersTo_ResolvesDirectly()
    {
        // The sheet-QUALIFIED reference form (=[1]Sheet1!TaxRate) reaches the very same
        // TryResolveExternalDefinedName via TryResolveExternalSheetQualifiedDefinedName in
        // FormulaEvaluator.References.cs, so a constant RefersTo must resolve through that path too.
        var workbook = BuildWorkbookWithConstantExternalDefinedName(out var sheet);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]Sheet1!TaxRate", sheet, workbook);

        result.Should().Be(new NumberValue(0.08));
    }

    // ── Sibling no-regression case ──────────────────────────────────────────

    [Fact]
    public void SheetQualifiedExternalDefinedName_RefersTo_StillResolvesAsCellReference_AfterConstantFix()
    {
        // Neighbouring behaviour that must not break: an external defined name whose RefersTo IS
        // sheet-qualified (a real cell reference into the external sheet's cached data) must
        // continue to resolve via the cached-value lookup, not be short-circuited into being
        // treated as a raw formula/constant text.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(50)); // local B2 = 50

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Book1.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        link.DefinedNames.Add(new ExternalDefinedNameModel
        {
            Name = "TaxRate",
            RefersTo = "Sheet1!$B$2", // sheet-qualified cell reference (R58's original shape)
        });

        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(2u, 2u)] = new NumberValue(100); // external Sheet1!B2 cached = 100
        link.CachedSheetData.Add(cachedSheet);
        workbook.ExternalLinks.Add(link);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]!TaxRate+B2", sheet, workbook);

        result.Should().Be(new NumberValue(150));
    }
}

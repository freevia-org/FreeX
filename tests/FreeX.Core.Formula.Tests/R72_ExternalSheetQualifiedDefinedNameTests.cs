using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R72-io-external-links-4-1: a sheet-qualified external defined-name reference (e.g.
/// <c>=[1]Sheet1!TaxRate</c>) evaluated to #REF! because
/// <c>FormulaEvaluator.References.cs</c>'s <c>TryResolveSheetQualifiedName</c> resolved
/// <see cref="FreeX.Core.Formula.NamedRangeNode.SheetQualifier"/> only via
/// <c>Workbook.GetSheet</c> -- a LOCAL sheet-name lookup that can never match the bracketed literal
/// <c>[1]Sheet1</c> -- and returned <c>#REF!</c> on a null match without ever consulting the
/// external-link resolver the plain-cell path (<c>SheetEvalContext.GetCellValue</c>) already uses.
/// Fixed by routing a bracket-prefixed qualifier through
/// <see cref="ExternalSheetReferenceResolver.TryResolve"/> and resolving the name against the
/// external link's cached <see cref="ExternalLinkModel.DefinedNames"/> (mirroring
/// <see cref="ExternalSheetReferenceResolver.TryResolveExternalDefinedName"/>, which already
/// handles the sheet-less <c>[n]!Name</c> form).
/// </summary>
public sealed class R72_ExternalSheetQualifiedDefinedNameTests
{
    private static Workbook BuildWorkbookWithExternalSheetQualifiedDefinedName(out Sheet sheet)
    {
        var workbook = new Workbook("Test");
        sheet = workbook.AddSheet("Sheet1");

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
            RefersTo = "Sheet1!$B$2",
            SheetId = 0, // sheet-scoped to the external workbook's own Sheet1 (index 0)
        });

        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(2u, 2u)] = new NumberValue(100); // external Sheet1!B2 cached = 100
        link.CachedSheetData.Add(cachedSheet);
        workbook.ExternalLinks.Add(link);

        return workbook;
    }

    [Fact]
    public void SheetQualifiedExternalDefinedName_ResolvesCachedValue_InsteadOfRef()
    {
        var workbook = BuildWorkbookWithExternalSheetQualifiedDefinedName(out var sheet);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]Sheet1!TaxRate", sheet, workbook);

        result.Should().Be(new NumberValue(100));
    }

    [Fact]
    public void SheetQualifiedExternalDefinedName_MixedWithLocalCellRef_RecalculatesLocalHalf()
    {
        var workbook = BuildWorkbookWithExternalSheetQualifiedDefinedName(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5)); // local A1 = 5

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]Sheet1!TaxRate*A1", sheet, workbook);
        result.Should().Be(new NumberValue(500));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(6));
        var updated = evaluator.Evaluate("=[1]Sheet1!TaxRate*A1", sheet, workbook);
        updated.Should().Be(new NumberValue(600));
    }

    // ── Sibling no-regression cases ─────────────────────────────────────────

    [Fact]
    public void SheetLessExternalDefinedName_StillWorksAfterSheetQualifiedFix()
    {
        // The sheet-less "[n]!Name" form (R58) is a wholly different code path
        // (ExternalSheetReferenceResolver.TryResolveExternalDefinedName reached directly from
        // SheetEvalContext.TryGetNamedFormulaText, never through TryResolveSheetQualifiedName at
        // all) and must still resolve after teaching the sheet-qualified form to also succeed.
        var workbook = BuildWorkbookWithExternalSheetQualifiedDefinedName(out var sheet);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]!TaxRate", sheet, workbook);

        result.Should().Be(new NumberValue(100));
    }

    [Fact]
    public void SheetQualifiedExternalReference_GenuinelyMissingName_StillReturnsRef()
    {
        var workbook = BuildWorkbookWithExternalSheetQualifiedDefinedName(out var sheet);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]Sheet1!NotDefinedAnywhere", sheet, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void SheetQualifiedExternalReference_UnknownExternalLink_StillReturnsRef()
    {
        var workbook = BuildWorkbookWithExternalSheetQualifiedDefinedName(out var sheet);

        var evaluator = new FormulaEvaluator();
        // Index 2 doesn't exist -- only one external link (index 1) was registered.
        var result = evaluator.Evaluate("=[2]Sheet1!TaxRate", sheet, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void RealLocalSheetQualifiedNamedRange_StillResolves_AfterExternalFix()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var a1 = new CellAddress(sheet2.Id, 1, 1);
        var a2 = new CellAddress(sheet2.Id, 2, 1);
        sheet2.SetCell(a1, new NumberValue(10));
        sheet2.SetCell(a2, new NumberValue(20));
        workbook.DefineNamedRange("LocalRange", new GridRange(a1, a2));

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=SUM(Sheet2!LocalRange)", sheet1, workbook);

        result.Should().Be(new NumberValue(30));
    }
}

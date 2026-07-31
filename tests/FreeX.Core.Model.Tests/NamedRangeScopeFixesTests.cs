using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for review-3 findings H5/H6/H7/H42 (defined-name scope correctness):
///   H5  — DefineNamedRangeCommand must be able to define a real sheet-scoped name, not
///         always fall through to the workbook-global store.
///   H6  — Defining a *new* name that collides with an existing name in the exact same
///         scope must be rejected with a clear error; cross-scope same-text names must
///         still coexist (Excel allows this) and redefining the very same (name, scope)
///         entry (an edit) must still succeed.
///   H7  — RenameSheetCommand must rewrite sheet-scoped named formulas that reference the
///         renamed sheet, and Revert must restore them.
///   H42 — DuplicateSheetCommand must copy the source sheet's sheet-scoped named ranges and
///         named formulas onto the new sheet, re-scoped to the copy.
/// </summary>
public sealed class NamedRangeScopeFixesTests
{
    private static (Workbook Workbook, TestCommandContext Ctx) CreateContext()
    {
        var wb = new Workbook("scope-fix-test");
        wb.AddSheet("Sheet1");
        return (wb, new TestCommandContext(wb));
    }

    // ── H5: scope picker must wire into real sheet-scoped storage ───────────────

    [Fact]
    public void DefineNamedRangeCommand_WithScopeSheetId_StoresInScopedDictionary_NotGlobal()
    {
        var (wb, ctx) = CreateContext();
        var sheet2 = wb.AddSheet("Sheet2");
        var range = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1));

        var cmd = new DefineNamedRangeCommand(
            "Region", range, new NamedRangeMetadata("Sheet2", ""), scopeSheetId: sheet2.Id);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        wb.NamedRanges.Should().NotContainKey("Region");
        wb.ScopedNamedRanges.Should().ContainKey(("Region", sheet2.Id));
        wb.ScopedNamedRanges[("Region", sheet2.Id)].Should().Be(range);
    }

    [Fact]
    public void DefineNamedRangeCommand_WithScopeSheetId_IsInvisibleFromOtherSheets()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        var sheet2 = wb.AddSheet("Sheet2");
        var range = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(42));

        new DefineNamedRangeCommand("Region", range, new NamedRangeMetadata("Sheet2", ""), sheet2.Id)
            .Apply(ctx).Success.Should().BeTrue();

        var eval = new FormulaEvaluator();
        // The raw evaluator returns a 1x1 RangeValue for a bare range name (cell recalc
        // dereferences it downstream) — assert on the dereferenced content.
        var resolved = eval.Evaluate("=Region", sheet2, wb);
        var scalar = resolved is RangeValue rangeValue ? rangeValue.At(1, 1) : resolved;
        scalar.Should().Be(new NumberValue(42));
        // A name scoped to Sheet2 must not resolve from Sheet1 (Excel per-sheet scoping).
        eval.Evaluate("=Region", sheet1, wb).Should().BeOfType<ErrorValue>();
    }

    [Fact]
    public void DefineNamedRangeCommand_Revert_WithScopeSheetId_RemovesScopedEntry()
    {
        var (wb, ctx) = CreateContext();
        var sheet2 = wb.AddSheet("Sheet2");
        var range = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1));

        var cmd = new DefineNamedRangeCommand("Region", range, new NamedRangeMetadata("Sheet2", ""), sheet2.Id);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.ScopedNamedRanges.Should().NotContainKey(("Region", sheet2.Id));
    }

    [Fact]
    public void RemoveNamedRangeCommand_WithScopeSheetId_RemovesOnlyTheScopedEntry()
    {
        var (wb, ctx) = CreateContext();
        var sheet2 = wb.AddSheet("Sheet2");
        var rangeGlobal = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1));
        var rangeScoped = new GridRange(new CellAddress(sheet2.Id, 2, 2), new CellAddress(sheet2.Id, 2, 2));

        wb.DefineNamedRange("Region", rangeGlobal);
        wb.DefineNamedRange("Region", rangeScoped, metadata: null, sheet2.Id);

        var outcome = new RemoveNamedRangeCommand("Region", sheet2.Id).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        wb.ScopedNamedRanges.Should().NotContainKey(("Region", sheet2.Id));
        // The workbook-global "Region" must be untouched.
        wb.TryGetNamedRange("Region", out var stillGlobal).Should().BeTrue();
        stillGlobal.Should().Be(rangeGlobal);
    }

    // ── H6: same-scope duplicate rejected; cross-scope coexistence allowed; edits succeed ──

    [Fact]
    public void DefineNamedRangeCommand_NewName_SameScopeDuplicate_IsRejected()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        var range1 = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        var range2 = new GridRange(new CellAddress(sheet1.Id, 2, 2), new CellAddress(sheet1.Id, 2, 2));

        new DefineNamedRangeCommand("Rate", range1, allowRedefine: false).Apply(ctx)
            .Success.Should().BeTrue();

        var outcome = new DefineNamedRangeCommand("Rate", range2, allowRedefine: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        // The original definition must be untouched by the rejected attempt.
        wb.TryGetNamedRange("Rate", out var stillRange1).Should().BeTrue();
        stillRange1.Should().Be(range1);
    }

    [Fact]
    public void DefineNamedRangeCommand_NewName_CrossScopeDuplicate_CoexistsWithoutError()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        var sheet2 = wb.AddSheet("Sheet2");
        var rangeGlobal = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        var rangeScoped = new GridRange(new CellAddress(sheet2.Id, 2, 2), new CellAddress(sheet2.Id, 2, 2));

        new DefineNamedRangeCommand("Rate", rangeGlobal, allowRedefine: false)
            .Apply(ctx).Success.Should().BeTrue();

        // Same text, different (sheet) scope: Excel allows these to coexist.
        var outcome = new DefineNamedRangeCommand(
            "Rate", rangeScoped, new NamedRangeMetadata("Sheet2", ""), sheet2.Id, allowRedefine: false)
            .Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        wb.TryGetNamedRange("Rate", out var globalRange).Should().BeTrue();
        globalRange.Should().Be(rangeGlobal);
        wb.ScopedNamedRanges[("Rate", sheet2.Id)].Should().Be(rangeScoped);
    }

    [Fact]
    public void DefineNamedRangeCommand_EditingSameEntry_AllowRedefineTrue_Succeeds()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        var original = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        var updated = new GridRange(new CellAddress(sheet1.Id, 5, 5), new CellAddress(sheet1.Id, 5, 5));

        new DefineNamedRangeCommand("Rate", original, allowRedefine: false).Apply(ctx)
            .Success.Should().BeTrue();

        // Editing the same name (same scope) must be allowed to replace the value.
        var outcome = new DefineNamedRangeCommand("Rate", updated, allowRedefine: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        wb.TryGetNamedRange("Rate", out var stored).Should().BeTrue();
        stored.Should().Be(updated);
    }

    [Fact]
    public void RemoveNamedRangeCommand_ScopedName_DoesNotExist_Fails()
    {
        var (wb, ctx) = CreateContext();
        var sheet2 = wb.AddSheet("Sheet2");

        var outcome = new RemoveNamedRangeCommand("Ghost", sheet2.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
    }

    // ── H7: RenameSheetCommand must rewrite sheet-scoped named formulas ─────────────

    [Fact]
    public void RenameSheetCommand_RewritesScopedNamedFormulaReferencingRenamedSheet_AndUndoRestores()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0]; // "Sheet1"
        var sheet2 = wb.AddSheet("Sheet2");

        // A named formula scoped to Sheet1 whose refers-to references Sheet2 by name.
        wb.DefineNamedFormula("LocalTotal", "SUM(Sheet2!A1:A10)", sheet1.Id);

        var command = new RenameSheetCommand(sheet2.Id, "Data");
        command.Apply(ctx).Success.Should().BeTrue();

        wb.ScopedNamedFormulas[("LocalTotal", sheet1.Id)].Should().Be("SUM(Data!A1:A10)");

        command.Revert(ctx);

        wb.ScopedNamedFormulas[("LocalTotal", sheet1.Id)].Should().Be("SUM(Sheet2!A1:A10)");
    }

    [Fact]
    public void RenameSheetCommand_LeavesUnrelatedScopedNamedFormulasUntouched()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        var sheet2 = wb.AddSheet("Sheet2");

        wb.DefineNamedFormula("Unrelated", "1+1", sheet1.Id);
        wb.DefineNamedFormula("Referenced", "Sheet2!B2", sheet1.Id);

        var command = new RenameSheetCommand(sheet2.Id, "Data");
        command.Apply(ctx).Success.Should().BeTrue();

        wb.ScopedNamedFormulas[("Unrelated", sheet1.Id)].Should().Be("1+1");
        wb.ScopedNamedFormulas[("Referenced", sheet1.Id)].Should().Be("Data!B2");

        command.Revert(ctx);

        wb.ScopedNamedFormulas[("Unrelated", sheet1.Id)].Should().Be("1+1");
        wb.ScopedNamedFormulas[("Referenced", sheet1.Id)].Should().Be("Sheet2!B2");
    }

    // ── H42: DuplicateSheetCommand must copy sheet-scoped names onto the new sheet ──

    [Fact]
    public void DuplicateSheetCommand_CopiesScopedNamedRangeOntoTheCopy()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        var range = new GridRange(new CellAddress(sheet1.Id, 2, 2), new CellAddress(sheet1.Id, 2, 2));
        wb.DefineNamedRange("LocalRate", range, new NamedRangeMetadata("Sheet1", "note"), sheet1.Id);

        var command = new DuplicateSheetCommand(sheet1.Id);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var copy = wb.Sheets[1];
        wb.ScopedNamedRanges.Should().ContainKey(("LocalRate", copy.Id));
        // R106: a same-sheet scoped name's range is rebased onto the COPY's own sheet (matching
        // Excel's Move-or-Copy, which rebases a local name's RefersTo when it targets its own
        // host sheet) -- comment/metadata still carries over verbatim.
        var expectedRange = new GridRange(new CellAddress(copy.Id, 2, 2), new CellAddress(copy.Id, 2, 2));
        wb.ScopedNamedRanges[("LocalRate", copy.Id)].Should().Be(expectedRange);
        wb.TryGetScopedNamedRangeMetadata("LocalRate", copy.Id, out var metadata).Should().BeTrue();
        metadata.Comment.Should().Be("note");

        // The source sheet's own scoped name must be unaffected.
        wb.ScopedNamedRanges.Should().ContainKey(("LocalRate", sheet1.Id));
        wb.ScopedNamedRanges[("LocalRate", sheet1.Id)].Should().Be(range);
    }

    [Fact]
    public void DuplicateSheetCommand_CrossSheetScopedNamedRange_KeepsPointingAtOriginalSheet()
    {
        // R106 sibling coverage: a sheet-scoped name that deliberately targets ANOTHER sheet's
        // cells (not its own host sheet) must NOT be rebased onto the copy -- only a same-sheet
        // reference travels with the duplicate, mirroring Sheet.Clone.ClonePivotTable's SourceRange
        // handling for a pivot table whose data lives on a different sheet than the pivot itself.
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        var dataSheet = wb.AddSheet("Data");
        var range = new GridRange(new CellAddress(dataSheet.Id, 0, 0), new CellAddress(dataSheet.Id, 0, 0));
        wb.DefineNamedRange("ExternalRate", range, new NamedRangeMetadata("Sheet1", ""), sheet1.Id);

        var command = new DuplicateSheetCommand(sheet1.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets.Single(s => s.Id != sheet1.Id && s.Id != dataSheet.Id);
        wb.ScopedNamedRanges.Should().ContainKey(("ExternalRate", copy.Id));
        // Unchanged: still points at the Data sheet, not remapped onto the copy.
        wb.ScopedNamedRanges[("ExternalRate", copy.Id)].Should().Be(range);
    }

    [Fact]
    public void DuplicateSheetCommand_CopiesScopedNamedFormulaOntoTheCopy_AndFormulaResolvesThere()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(10));
        wb.DefineNamedFormula("LocalRate", "A1*2", sheet1.Id);

        var command = new DuplicateSheetCommand(sheet1.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.SetCell(new CellAddress(copy.Id, 1, 1), new NumberValue(25));

        wb.ScopedNamedFormulas.Should().ContainKey(("LocalRate", copy.Id));

        var eval = new FormulaEvaluator();
        // A formula on the copy referencing the sheet-local name must resolve there too.
        eval.Evaluate("=LocalRate", copy, wb).Should().Be(new NumberValue(50));
    }

    [Fact]
    public void DuplicateSheetCommand_Revert_RemovesCopiedScopedNamesToo()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        var range = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        wb.DefineNamedRange("LocalRate", range, metadata: null, sheet1.Id);

        var command = new DuplicateSheetCommand(sheet1.Id);
        command.Apply(ctx);
        var copyId = wb.Sheets[1].Id;

        command.Revert(ctx);

        wb.Sheets.Should().HaveCount(1);
        wb.ScopedNamedRanges.Should().NotContainKey(("LocalRate", copyId));
        // The source sheet's own scoped name must still be present.
        wb.ScopedNamedRanges.Should().ContainKey(("LocalRate", sheet1.Id));
    }

    [Fact]
    public void DuplicateSheetCommand_SheetWithNoScopedNames_DoesNotIntroduceAny()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];

        var command = new DuplicateSheetCommand(sheet1.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        wb.ScopedNamedRanges.Should().BeEmpty();
        wb.ScopedNamedFormulas.Should().BeEmpty();
    }
}

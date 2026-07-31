using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R106: DuplicateSheetCommand's CopyScopedNamedRangesAndFormulas copied a sheet-scoped named
/// range/formula onto the duplicated sheet by re-keying it to the copy's SheetId, but left the
/// GridRange/formula text VERBATIM -- when the source sheet-scoped name's range/formula targeted
/// cells on the source sheet itself (the overwhelmingly common case for a sheet-local name, e.g.
/// a per-sheet "TaxRate"), the copy's re-scoped name kept pointing at the ORIGINAL sheet's cells
/// forever. Real Excel's Move-or-Copy "Create a copy" rebases a local name's RefersTo onto the new
/// copy when it targets the sheet's own cells -- this is the entire purpose of sheet-scoped names
/// in templates. This mirrors the same command's/Sheet.Clone's already-established same-sheet-
/// qualified rebase for cell formulas, CF/DV formula text, hyperlinks, and pivot table source
/// ranges.
/// </summary>
public sealed class R106_DuplicateSheetScopedNamedRangeRebaseTests
{
    private static (Workbook Workbook, TestCommandContext Ctx) CreateContext()
    {
        var wb = new Workbook("r106-scoped-name-rebase-test");
        wb.AddSheet("Sheet1");
        return (wb, new TestCommandContext(wb));
    }

    [Fact]
    public void DuplicateSheetCommand_SameSheetScopedNamedRange_IsRebasedOntoTheCopy()
    {
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        var range = new GridRange(new CellAddress(sheet1.Id, 2, 2), new CellAddress(sheet1.Id, 2, 2));
        wb.DefineNamedRange("TaxRate", range, new NamedRangeMetadata("Sheet1", ""), sheet1.Id);

        var command = new DuplicateSheetCommand(sheet1.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        wb.ScopedNamedRanges.Should().ContainKey(("TaxRate", copy.Id));

        var copiedRange = wb.ScopedNamedRanges[("TaxRate", copy.Id)];
        copiedRange.Start.Sheet.Should().Be(copy.Id, "a same-sheet scoped name must follow the copy, not keep pointing at the source sheet");
        copiedRange.Start.Row.Should().Be(2);
        copiedRange.Start.Col.Should().Be(2);

        // The source sheet's own scoped name must be entirely unaffected.
        wb.ScopedNamedRanges[("TaxRate", sheet1.Id)].Should().Be(range);
    }

    [Fact]
    public void DuplicateSheetCommand_SameSheetScopedNamedRange_FormulaUsingItResolvesAgainstTheCopy()
    {
        // End-to-end through the real evaluator (not just state) -- a formula on the COPY that
        // references the copy's own rebased local name must read the COPY's cell, not the source
        // sheet's cell.
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 2), new NumberValue(111));
        var range = new GridRange(new CellAddress(sheet1.Id, 2, 2), new CellAddress(sheet1.Id, 2, 2));
        wb.DefineNamedRange("TaxRate", range, metadata: null, sheet1.Id);

        var command = new DuplicateSheetCommand(sheet1.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.SetCell(new CellAddress(copy.Id, 2, 2), new NumberValue(222));

        var eval = new FormulaEvaluator();
        eval.Evaluate("=SUM(TaxRate)", copy, wb).Should().Be(new NumberValue(222),
            "the copy's own rebased local name must read the COPY's cell, not the source sheet's");
        // The source sheet's formula must still read its own (unrebased) local name.
        eval.Evaluate("=SUM(TaxRate)", sheet1, wb).Should().Be(new NumberValue(111));
    }

    [Fact]
    public void DuplicateSheetCommand_SameSheetQualifiedScopedNamedFormula_IsRebasedOntoTheCopy()
    {
        // A scoped named FORMULA (not a plain range) that explicitly sheet-qualifies its own host
        // sheet (e.g. "=Sheet1!A1*2", as opposed to the already-correctly-handled unqualified
        // "=A1*2") must have that explicit qualifier rebased onto the copy's own sheet name, the
        // same way Sheet.Clone already rebases an explicit same-sheet-qualified cell formula.
        var (wb, ctx) = CreateContext();
        var sheet1 = wb.Sheets[0];
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(10));
        wb.DefineNamedFormula("LocalRate", "Sheet1!A1*2", sheet1.Id);

        var command = new DuplicateSheetCommand(sheet1.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.SetCell(new CellAddress(copy.Id, 1, 1), new NumberValue(25));

        // The copy's sheet name (e.g. "Sheet1 (2)") needs quoting since it contains a space, so
        // the rebased qualifier is 'Sheet1 (2)'! -- just assert the source qualifier is gone and
        // the copy's bare name appears somewhere in the rebased qualifier.
        wb.ScopedNamedFormulas[("LocalRate", copy.Id)].Should().Contain(copy.Name);
        wb.ScopedNamedFormulas[("LocalRate", copy.Id)].Should().NotContain("Sheet1!");

        var eval = new FormulaEvaluator();
        eval.Evaluate("=LocalRate", copy, wb).Should().Be(new NumberValue(50));
        // Source sheet's own formula must still resolve against Sheet1's own cell.
        eval.Evaluate("=LocalRate", sheet1, wb).Should().Be(new NumberValue(20));
    }
}

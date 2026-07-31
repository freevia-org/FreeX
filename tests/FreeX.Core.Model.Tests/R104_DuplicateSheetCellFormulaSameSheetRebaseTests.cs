using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the finding: <c>Sheet.Clone</c> (used exclusively by
/// <see cref="DuplicateSheetCommand"/> for "Move or Copy &gt; Create a copy") copied every ordinary
/// cell formula VERBATIM via <c>CopyCellContentTo -&gt; Cell.Clone()</c>, with no rebase of an
/// explicit same-sheet-qualified reference (e.g. "Sheet1!A1" typed/pasted on Sheet1 itself) --
/// unlike every OTHER formula-bearing feature on the same sheet, which already gets exactly this
/// rebase on duplicate: <see cref="ConditionalFormat.FormulaText"/> / <see cref="DataValidation.Formula1"/>/
/// <see cref="DataValidation.Formula2"/> (see <c>ModelTests.SheetClone.SameSheetFormulaRebase.cs</c>),
/// hyperlink targets/bookmarks, and chart verbatim ranges. Real Excel's "Move or Copy &gt; Create a
/// copy" rebases an explicit same-sheet-qualified formula reference to follow the new copy (e.g.
/// "Sheet1!A1" on Sheet1 becomes "'Sheet1 (2)'!A1" on the duplicate), while a reference qualified
/// with a genuinely DIFFERENT sheet's name is left unchanged. Tested through
/// <see cref="DuplicateSheetCommand"/> -- the real command "Create a copy" reaches -- not by calling
/// <c>Sheet.Clone</c> directly.
/// </summary>
public sealed class R104_DuplicateSheetCellFormulaSameSheetRebaseTests
{
    [Fact]
    public void DuplicateSheet_RebasesCellFormulaSameSheetQualifiedReferenceToCopy()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 0, 0), Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("Sheet1!A1+1"));

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.Name.Should().Be("Sheet1 (2)");

        // Source untouched.
        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.FormulaText.Should().Be("Sheet1!A1+1");

        // Copy's formula must follow the copy, quoted because the auto-generated name needs it.
        copy.GetCell(new CellAddress(copy.Id, 1, 1))!.FormulaText.Should().Be("'Sheet1 (2)'!A1+1");
    }

    [Fact]
    public void DuplicateSheet_RebaseClearsCachedAstSoRewrittenTextReparses()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var formulaCell = Cell.FromFormula("Sheet1!A1+1");
        // Simulate a previously-parsed formula the way the real calc engine would leave it.
        formulaCell.CachedAst = new object();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), formulaCell);

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedCell = copy.GetCell(new CellAddress(copy.Id, 1, 1))!;
        copiedCell.FormulaText.Should().Be("'Sheet1 (2)'!A1+1");
        // A stale AST parsed from the OLD text must not survive the rewrite -- otherwise the calc
        // engine would keep resolving the pre-rewrite reference despite the text now being correct.
        copiedCell.CachedAst.Should().BeNull();
    }

    [Fact]
    public void DuplicateSheet_LeavesCellFormulaUnqualifiedReferenceUnchanged()
    {
        // Sibling already-working case: an unqualified reference implicitly means "this sheet"
        // both before and after duplication, so it must survive untouched.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("A1+1"));

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.GetCell(new CellAddress(copy.Id, 1, 1))!.FormulaText.Should().Be("A1+1");
    }

    [Fact]
    public void DuplicateSheet_LeavesCellFormulaOtherSheetQualifiedReferenceUnchanged()
    {
        // Sibling already-working case: a reference qualified with a DIFFERENT sheet's name must
        // keep pointing at that other sheet, not follow the duplicate.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("Sheet2!A1+1"));

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.GetCell(new CellAddress(copy.Id, 1, 1))!.FormulaText.Should().Be("Sheet2!A1+1");
    }

    [Fact]
    public void DuplicateSheet_PreservesLegacyArrayFormulaExtentThroughRebase()
    {
        // No-regression sibling: rebasing must not clobber legacy CSE array-formula metadata that
        // the FormulaText setter would otherwise reset to "freshly authored" defaults.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var arrayCell = Cell.FromFormula("Sheet1!A1:A2");
        arrayCell.ArrayMode = FormulaArrayMode.Implicit;
        arrayCell.LegacyArrayRows = 2;
        arrayCell.LegacyArrayCols = 1;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), arrayCell);

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedCell = copy.GetCell(new CellAddress(copy.Id, 1, 1))!;
        copiedCell.FormulaText.Should().Be("'Sheet1 (2)'!A1:A2");
        copiedCell.ArrayMode.Should().Be(FormulaArrayMode.Implicit);
        copiedCell.LegacyArrayRows.Should().Be(2u);
        copiedCell.LegacyArrayCols.Should().Be(1u);
    }
}

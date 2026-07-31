using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the finding: r104's <c>CopyCellContentTo</c> rebase of an explicit
/// same-sheet-qualified cell formula (<see cref="Sheet.Clone"/> via
/// <c>RewriteSameSheetQualifiedFormula</c>) matched only a plain qualifier like "Sheet1!A1" --
/// a 3-D sheet-span reference such as "Sheet1:Sheet3!A1", where "Sheet1" is the same sheet the
/// formula lives on but is followed by ':' rather than '!', fell through both the quoted and bare
/// regex patterns and was left pointing at the ORIGINAL sheet after Duplicate Sheet, unlike an
/// ordinary same-sheet reference in an adjacent cell on the same copy. FreeX's own AST-based
/// <c>FormulaRewriter.RewriteRange</c> (used for Rename/Delete Sheet) already rebases a same-sheet
/// 3-D span endpoint, establishing that Excel's "Move or Copy &gt; Create a copy" is understood to
/// treat a same-sheet span endpoint the same as a simple same-sheet qualifier. Tested through
/// <see cref="DuplicateSheetCommand"/> -- the real command "Create a copy" reaches -- not by
/// calling <c>Sheet.Clone</c> directly.
/// </summary>
public sealed class R106_DuplicateSheetCellFormula3DSpanSameSheetRebaseTests
{
    [Fact]
    public void DuplicateSheet_RebasesBareThreeDSpanStartEndpointToCopy()
    {
        // "Sheet1" is the START endpoint of the span and is the sheet being duplicated -- neither
        // "Sheet1" nor "Sheet3" needs quoting, so the pre-fix bare pattern should have matched...
        // except the bare pattern only looked for the source name immediately followed by '!', and
        // here it's followed by ':' instead. This is the exact gap the finding describes.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        wb.AddSheet("Sheet3");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("SUM(Sheet1:Sheet3!A1)"));

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        // Source is at index 0, so the copy is inserted right after it at index 1.
        var copy = wb.Sheets[1];
        copy.Name.Should().Be("Sheet1 (2)");

        // Source untouched.
        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.FormulaText.Should().Be("SUM(Sheet1:Sheet3!A1)");

        // Copy's span start endpoint must follow the copy. The auto-generated copy name needs
        // quoting (space + parens), and Excel quotes the WHOLE span as one token in that case.
        copy.GetCell(new CellAddress(copy.Id, 1, 1))!.FormulaText.Should().Be("SUM('Sheet1 (2):Sheet3'!A1)");
    }

    [Fact]
    public void DuplicateSheet_RebasesBareThreeDSpanEndEndpointToCopy()
    {
        // No-regression sibling: "Sheet1" as the END endpoint of a bare span (already reachable by
        // the pre-fix bare pattern in the simple case) must still work with the new span-aware
        // rewrite, and produce the correctly whole-span-quoted form now that the copy's name needs
        // quoting (the pre-fix code would have produced the malformed "Sheet2:'Sheet1 (2)'!A1").
        var wb = new Workbook("test");
        wb.AddSheet("Sheet2");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("SUM(Sheet2:Sheet1!A1)"));

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        // Source ("Sheet1") is at index 1, so the copy is inserted right after it at index 2.
        var copy = wb.Sheets[2];
        copy.Name.Should().Be("Sheet1 (2)");

        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.FormulaText.Should().Be("SUM(Sheet2:Sheet1!A1)");
        copy.GetCell(new CellAddress(copy.Id, 1, 1))!.FormulaText.Should().Be("SUM('Sheet2:Sheet1 (2)'!A1)");
    }

    [Fact]
    public void DuplicateSheet_LeavesThreeDSpanNotIncludingSourceSheetUnchanged()
    {
        // Sibling already-working case: a 3-D span that does not name the sheet being duplicated at
        // all must be left completely untouched.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        wb.AddSheet("Sheet3");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("SUM(Sheet2:Sheet3!A1)"));

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        // Source ("Sheet1") is at index 0, so the copy is inserted right after it at index 1.
        var copy = wb.Sheets[1];
        copy.GetCell(new CellAddress(copy.Id, 1, 1))!.FormulaText.Should().Be("SUM(Sheet2:Sheet3!A1)");
    }

    [Fact]
    public void DuplicateSheet_RebasesQuotedThreeDSpanStartEndpointToCopy()
    {
        // The source sheet's own name needs quoting (space), so the ORIGINAL formula's span is
        // already whole-span-quoted. Rebasing the start endpoint must keep the result whole-span
        // quoted around both names.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet 1");
        wb.AddSheet("Sheet3");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("SUM('Sheet 1:Sheet3'!A1)"));

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        // Source ("Sheet 1") is at index 0, so the copy is inserted right after it at index 1.
        var copy = wb.Sheets[1];
        copy.Name.Should().Be("Sheet 1 (2)");

        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.FormulaText.Should().Be("SUM('Sheet 1:Sheet3'!A1)");
        copy.GetCell(new CellAddress(copy.Id, 1, 1))!.FormulaText.Should().Be("SUM('Sheet 1 (2):Sheet3'!A1)");
    }
}

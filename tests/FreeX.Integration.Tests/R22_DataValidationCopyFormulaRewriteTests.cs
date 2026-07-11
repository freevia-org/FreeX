using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R22-cell-reference-rewrite-2 / R22-data-validation-2: DataValidationCopySupport.RewriteValidationFormula
/// gated the paste-offset rewrite on the stored formula starting with '=' and bailed out entirely if the
/// text contained a comma. Real DV formulas are stored per OOXML with NO leading '=' (see
/// XlsxDataValidationClosedXmlMapper.Load, DataValidationBoundsParser.TryEvaluateBoundFormula, and
/// DataValidationService.ValidateCustom, which all defensively prepend '=' before parsing for exactly this
/// reason), so the leading-'=' guard made the rewrite a no-op for any real-world rule. The comma guard also
/// bailed on completely ordinary multi-argument formulas like AND(A1&gt;0,B1&gt;0), even though
/// FormulaRewriter is a full AST rewriter that handles function-argument commas fine.
/// </summary>
public sealed class R22_DataValidationCopyFormulaRewriteTests
{
    [Fact]
    public void PasteDataValidationCommand_RebasesCellReferenceBoundsStoredWithoutLeadingEquals()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 2, 2); // B2

        // Formula1/Formula2 stored per real OOXML convention: no leading '='.
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "A1",
            Formula2 = "A2"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 5, 4), // D5 (rowDelta=3, colDelta=2)
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 5 && rule.AppliesTo.Start.Col == 4)
            .Which;

        // Real Excel shifts the relative bounds along with the paste, exactly like any other
        // pasted formula: A1 -> C4, A2 -> C5.
        pasted.Formula1.Should().Be("C4");
        pasted.Formula2.Should().Be("C5");
    }

    [Fact]
    public void PasteDataValidationCommand_RebasesCustomFormulaWithCommaArgumentsWhenPasted()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.Custom,
            Formula1 = "=AND(A1>0,B1>0)"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 5, 3), // C5 (rowDelta=4, colDelta=2)
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 5 && rule.AppliesTo.Start.Col == 3)
            .Which;

        // The comma is a function-argument separator, not a list literal -- it must not block the
        // rewrite. A1 -> C5, B1 -> D5.
        pasted.Formula1.Should().Be("=AND(C5>0,D5>0)");
    }

    [Fact]
    public void PasteDataValidationCommand_StillPreservesNumericInlineListLiteralWhenPasted()
    {
        // Regression guard: an explicit List-type value list like "1,2,3" must NOT be
        // misinterpreted as a rewritable formula just because digits (and now commas) no longer
        // short-circuit the rewrite -- it fails to parse as a formula expression and so falls
        // through unchanged.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.List,
            Formula1 = "1,2,3"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 5, 3), // C5
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 5 && rule.AppliesTo.Start.Col == 3)
            .Which;

        pasted.Formula1.Should().Be("1,2,3");
    }
}
